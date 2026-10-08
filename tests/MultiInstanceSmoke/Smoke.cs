using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Environment = System.Environment;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

[ModInitializer(nameof(Initialize))]
public static class Smoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Type Controller = null!;
    private static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).First(t => t != null)!;
    private static object? Field(object target, string name) => target.GetType().GetField(name, Any)!.GetValue(target);
    private static object? Static(string name) => Controller.GetField(name, Any)!.GetValue(null);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Any)!.Invoke(target, args);
    private static bool Verified => Static("_mirror") is { } m && (bool)m.GetType().GetProperty("Verified", Any)!.GetValue(m)!;
    public static void Initialize()
    {
        var args = OS.GetCmdlineArgs(); int force = Array.IndexOf(args, "--force-steam");
        if (Environment.GetEnvironmentVariable("RMP_MULTI_TEST") != "1" || Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer" || force < 0 || args[force+1] != "off") return;
        TaskHelper.RunSafely(Run());
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); GD.Print("[MultiInstanceSmoke] PASS " + label); }
    private static async Task Frames(int n) { for(int i=0;i<n;i++) { Engine.MaxFps=DisplayServer.GetName()=="headless" ? 30 : 120; await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame); } }
    private static async Task Until(Func<bool> predicate, string label, int limit=2400)
    {
        for(int i=0;i<limit && !predicate();i++)
        {
            if (Static("_mirror") is { } mirror && (string)mirror.GetType().GetProperty("Error",Any)!.GetValue(mirror)! is { Length: > 0 } error) throw new Exception(error);
            await Frames(1);
        }
        Check(predicate(),label);
    }
    private static void IdentityChecks()
    {
        var identity = Activator.CreateInstance(Type("MirrorIdentity"),Any,null,new object[]{"one","secret",1UL},null)!;
        object Message(long sequence, string session="one", ulong source=1) { var m=Activator.CreateInstance(Type("MirrorMessage"))!; foreach(var pair in new[]{("Session",(object)session),("Secret",(object)"secret"),("Source",(object)source),("Sequence",(object)sequence)}) m.GetType().GetProperty(pair.Item1)!.SetValue(m,pair.Item2); return m; }
        void Reject(object message,string label) { bool rejected=false; try{Call(identity,"Validate",message);}catch(TargetInvocationException e) when(e.InnerException is InvalidDataException){rejected=true;} Check(rejected,label); }
        Reject(Message(1,"wrong"),"different session rejected"); Reject(Message(1,source:2),"different player rejected");
        Call(identity,"Validate",Message(1)); Reject(Message(1),"duplicate packet rejected"); Reject(Message(3),"out of order packet rejected");
        var old=Message(2); old.GetType().GetProperty("Generation")!.SetValue(old,1L); identity.GetType().GetField("Generation",Any)!.SetValue(identity,2L);
        Check(!(bool)Call(identity,"Current",old)!,"old checkpoint frame rejected");
    }
    private static async Task Run()
    {
        try
        {
            for(int i=0;i<1800 && NGame.Instance?.MainMenu==null;i++) await Frames(1);
            await Frames(180); Controller=Type("LiveSharingController"); IdentityChecks();
            Controller.GetMethod("Close",Any)!.Invoke(null,null);
            SaveManager.Instance.SetFtuesEnabled(false);
            var character=ModelDb.Character<Ironclad>();
            var state=RunState.CreateForNewRun(new[]{Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1)},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPMULTITEST");
            RunManager.Instance.SetUpNewSingleplayer(state,true); await PreloadManager.LoadRunAssets(new[]{character}); RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state)); await RunManager.Instance.SetActInternal(0);
            var point=state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster);
            await RunManager.Instance.EnterMapCoord(point.coord); await Frames(180);
            Check(NCombatRoom.Instance != null && state.Players[0].PlayerCombatState!=null,"source native combat ready");
            Controller.GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            await Until(()=>Verified,"second native process reconstructs exact initial combat state");
            var mirror=Static("_mirror")!; var process=Field(mirror,"Process")!;
            var child=(System.Diagnostics.Process)process.GetType().GetProperty("Child",Any)!.GetValue(process)!;
            int childId=child.Id;
            Check(childId!=Environment.ProcessId,"renderer owns a different operating system process");
            string profile=(string)Field(process,"DirectoryPath")!;
            Check(profile.StartsWith(OS.GetUserDataDir().Replace('/',Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase),"renderer profile belongs to its disposable session");
            Check(!Directory.EnumerateFiles(profile,"rmp-spectator.json",SearchOption.AllDirectories).Any(),"renderer does not inherit spectator auto-open configuration");
            var view=Static("_view")!; var panel=(Control)Field(view,"_panel")!;
            Check(panel.GetNodeOrNull<Control>("SpectatorControlToggle")!=null && panel.GetNodeOrNull<Control>("SpectatorPin")!=null && panel.GetNodeOrNull<Control>("SpectatorClose")!=null,"watch control pin and close shell retained");
            Check(panel.GetNode<Control>("SpectatorContent").ClipContents,"native picture remains clipped inside shell");
            var content=(Control)Field(view,"_content")!;
            Check(!content.GetChildren().Any(n=>n is SubViewportContainer or TextureRect or NCard) && Type("MirrorRenderer").GetMethod("CaptureFrame",Any)==null,"parent has no screenshot surface or PNG capture path");
            var source=Static("_source")!; Call(view,"SetControlEnabled",true); long epoch=(long)Field(view,"_epoch")!;
            var snapshot=Call(source,"CaptureCommands",state)!; var control=snapshot.GetType().GetProperty("Control")!.GetValue(snapshot)!;
            var actions=(System.Collections.IEnumerable)control.GetType().GetProperty("Actions")!.GetValue(control)!;
            var end=actions.Cast<object>().First(a=>(string)a.GetType().GetProperty("Kind")!.GetValue(a)! =="endTurn" && (bool)a.GetType().GetProperty("Enabled")!.GetValue(a)!);
            long request=0;
            object Command(string session) { var c=Activator.CreateInstance(Type("SpectatorCommand"))!; void Set(string name,object value)=>c.GetType().GetProperty(name)!.SetValue(c,value); Set("Session",session); Set("SourceId",snapshot.GetType().GetProperty("SourceId")!.GetValue(snapshot)!); Set("Context",control.GetType().GetProperty("Context")!.GetValue(control)!); Set("Epoch",epoch); Set("RequestId",++request); Set("ActionId",end.GetType().GetProperty("Id")!.GetValue(end)!); return c; }
            var execute=Controller.GetMethod("Execute",Any)!;
            var wrong=execute.Invoke(null,new[]{(object)state,Command("wrong")})!;
            Check(!(bool)wrong.GetType().GetProperty("Accepted")!.GetValue(wrong)!,"wrong source session cannot execute a game command");
            int turn=state.Players[0].PlayerCombatState!.TurnNumber;
            var result=execute.Invoke(null,new[]{(object)state,Command((string)snapshot.GetType().GetProperty("Session")!.GetValue(snapshot)! )})!;
            Check((bool)result.GetType().GetProperty("Accepted")!.GetValue(result)!,"verified control request executes only on authoritative source");
            await Until(()=>state.Players[0].PlayerCombatState?.TurnNumber>turn && Verified,"ordered events reproduce enemy turn and next player hand");
            bool HasHits() => ((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().Any(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == "play" && ((float[])h.GetType().GetProperty("Rect")!.GetValue(h)!)[2]>0);
            await Until(()=>HasHits() && Verified,"replica reports actual native card hitboxes with matching state");
            snapshot=Call(source,"CaptureCommands",state)!; control=snapshot.GetType().GetProperty("Control")!.GetValue(snapshot)!;
            actions=(System.Collections.IEnumerable)control.GetType().GetProperty("Actions")!.GetValue(control)!;
            end=actions.Cast<object>().First(a=>(string)a.GetType().GetProperty("Kind")!.GetValue(a)! == "play" && (bool)a.GetType().GetProperty("Enabled")!.GetValue(a)! && (bool)a.GetType().GetProperty("RequiresTarget")!.GetValue(a)!);
            var attack=Command((string)snapshot.GetType().GetProperty("Session")!.GetValue(snapshot)!);
            attack.GetType().GetProperty("TargetId")!.SetValue(attack,((System.Collections.IEnumerable)end.GetType().GetProperty("TargetIds")!.GetValue(end)!).Cast<string>().First());
            string matching=(string)Field(process,"LastHash")!;
            process.GetType().GetField("LastHash",Any)!.SetValue(process,"wrong-hash");
            var mismatch=execute.Invoke(null,new[]{(object)state,attack})!;
            Check(!(bool)mismatch.GetType().GetProperty("Accepted")!.GetValue(mismatch)!,"different replica state blocks authoritative control");
            process.GetType().GetField("LastHash",Any)!.SetValue(process,matching);
            await Until(()=>(bool)Call(mirror,"Authorize",state)!,"attack submission rechecks the current authoritative hash");
            snapshot=Call(source,"CaptureCommands",state)!; control=snapshot.GetType().GetProperty("Control")!.GetValue(snapshot)!;
            // Capture refreshes native playability. Build a fresh command from
            // the latest permission context, then wait for its state to match.
            attack=Command((string)snapshot.GetType().GetProperty("Session")!.GetValue(snapshot)!);
            attack.GetType().GetProperty("TargetId")!.SetValue(attack,((System.Collections.IEnumerable)end.GetType().GetProperty("TargetIds")!.GetValue(end)!).Cast<string>().First());
            await Until(()=>(bool)Call(mirror,"Authorize",state)!,"card playability and current source state both ready");
            int energy=state.Players[0].PlayerCombatState!.Energy;
            result=execute.Invoke(null,new[]{(object)state,attack})!;
            Check((bool)result.GetType().GetProperty("Accepted")!.GetValue(result)!,"targeted attack resolves source-owned card and enemy identifiers: " + result.GetType().GetProperty("Message")!.GetValue(result));
            await Until(()=>state.Players[0].PlayerCombatState!.Energy<energy && (bool)Call(mirror,"Authorize",state)!,"attack damage energy and hand changes match in replica");
            string before=Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!.ToString()!;
            object Intent(string kind, int index=-1, int target=-1)
            {
                var message=Activator.CreateInstance(Type("MirrorMessage"))!;
                void Set(string name,object value)=>message.GetType().GetProperty(name)!.SetValue(message,value);
                Set("Kind","intent"); Set("Model",kind); Set("Epoch",epoch); Set("Request",++request); Set("Index",index); Set("TargetIndex",target);
                Set("Hash",Field(process,"LastHash")!); Set("Events",Field(process,"LastEvents")!);
                Set("Generation",Field(Field(process,"Wire")!,"Identity")!.GetType().GetField("Generation",Any)!.GetValue(Field(Field(process,"Wire")!,"Identity"))!);
                return message;
            }
            void RejectIntent(object intent,string label)
            {
                var rejected=Controller.GetMethod("ResolveIntent",Any)!.Invoke(null,new[]{(object)state,intent})!;
                Check(!(bool)rejected.GetType().GetProperty("Accepted")!.GetValue(rejected)!,label);
            }
            var stale=Intent("endTurn"); stale.GetType().GetProperty("Generation")!.SetValue(stale,-1L); RejectIntent(stale,"old native-window generation rejected");
            stale=Intent("endTurn"); stale.GetType().GetProperty("Epoch")!.SetValue(stale,-1L); RejectIntent(stale,"old native-window control epoch rejected");
            stale=Intent("endTurn"); stale.GetType().GetProperty("Hash")!.SetValue(stale,"wrong"); RejectIntent(stale,"native-window intent with stale state rejected");
            RejectIntent(Intent("play",99999),"invalid native hand index rejected");
            Call(view,"SetControlEnabled",false); RejectIntent(Intent("endTurn"),"watch mode cannot execute native-window intents");
            Call(view,"SetControlEnabled",true); epoch=(long)Field(view,"_epoch")!;
            await Frames(15);
            Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"rejected native-window input cannot mutate source gameplay");
            await Until(()=>(bool)Call(mirror,"Authorize",state)!,"native intent source is idle and consistent");
            turn=state.Players[0].PlayerCombatState!.TurnNumber;
            var nativeIntent=Intent("endTurn");
            var nativeResult=Controller.GetMethod("ResolveIntent",Any)!.Invoke(null,new[]{(object)state,nativeIntent})!;
            Check((bool)nativeResult.GetType().GetProperty("Accepted")!.GetValue(nativeResult)!,"native indices resolve to authoritative end-turn command");
            await Until(()=>state.Players[0].PlayerCombatState!.TurnNumber>turn && (bool)Call(mirror,"Authorize",state)!,"native intent result reaches both models");
            if(DisplayServer.GetName()!="headless") await NativeWindowChecks(state,mirror,process,view);
            Controller.GetMethod("Close",Any)!.Invoke(null,null); await Frames(10);
            bool gone=false; try { using var stopped=System.Diagnostics.Process.GetProcessById(childId); gone=stopped.HasExited; } catch(ArgumentException){gone=true;}
            Check(gone,"closing panel terminates only its owned renderer");
            Check(CombatManager.Instance.IsInProgress,"closing renderer leaves source combat running");
            string previousSession=(string)Field(Field(process,"Wire")!,"Identity")!.GetType().GetField("Session",Any)!.GetValue(Field(Field(process,"Wire")!,"Identity"))!;
            Controller.GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            await Until(()=>Verified,"reopening reconstructs mid-combat event prefix");
            var reopened=Field(Field(Static("_mirror")!,"Process")!,"Wire")!;
            Check((string)Field(reopened,"Identity")!.GetType().GetField("Session",Any)!.GetValue(Field(reopened,"Identity"))! != previousSession,"reopened renderer uses a new authenticated session");
            Controller.GetMethod("Close",Any)!.Invoke(null,null);
            GD.Print("[MultiInstanceSmoke] ALL PASSED"); ((SceneTree)Engine.GetMainLoop()).Quit();
        }
        catch(Exception e) { GD.PrintErr("[MultiInstanceSmoke] FAIL "+e); Controller?.GetMethod("Close",Any)?.Invoke(null,null); ((SceneTree)Engine.GetMainLoop()).Quit(1); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint window,uint message,nuint param,nint data,uint flags,uint timeout,out nuint result);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    private static async Task NativeWindowChecks(RunState state,object mirror,object process,object view)
    {
        await Until(()=>mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror) is { } window && (bool)window.GetType().GetProperty("Attached",Any)!.GetValue(window)! && (bool)Field(process,"NativeAttached")!,"renderer HWND embedded in source clipping container");
        var host=mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror)!;
        nint child=(nint)(long)Field(process,"Window")!;
        await Until(()=>IsWindowVisible(child) && (long)Field(process,"DrawFrames")! > 0,"embedded battle HWND is visible and has a draw callback (visual pixels still require inspection)");
        nint container=(nint)host.GetType().GetProperty("Container",Any)!.GetValue(host)!;
        GetWindowThreadProcessId(child,out uint owner);
        Check(owner==((System.Diagnostics.Process)process.GetType().GetProperty("Child",Any)!.GetValue(process)!).Id && GetParent(child)==container,"only owned renderer HWND is attached");
        var panel=(Control)Field(view,"_panel")!; var content=(Control)Field(view,"_content")!;
        var saved=panel.Position; panel.Position+=new Vector2(20,10); await Frames(4);
        GetClientRect(container,out var size); GetClientRect(child,out var childSize);
        Check(size.Right==childSize.Right && size.Bottom==childSize.Bottom && size.Right>100,"native window follows content dimensions");
        panel.Position=saved; await Frames(4);
        await Until(()=>((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().Any(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == "play") && (bool)Call(mirror,"Authorize",state)!,"native hand hitboxes ready after dealing animation");
        var before=(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!;
        void Send(uint message,float x,float y)
        {
            GetClientRect(child,out var rect);
            int px=(int)(x*rect.Right),py=(int)(y*rect.Bottom);
            Check(SendMessageTimeoutW(child,message,message==0x201 ? 1u : 0u,(nint)((py<<16)|(px&0xFFFF)),2,1000,out _)!=0,"owned offscreen native message dispatched");
        }
        var hits=((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().ToArray();
        var card=hits.Last(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == "play");
        int index=(int)card.GetType().GetProperty("Index")!.GetValue(card)!;
        var bounds=(float[])card.GetType().GetProperty("Rect")!.GetValue(card)!;
        Send(0x200,bounds[0]+bounds[2]/2,Math.Min(.97f,bounds[1]+bounds[3]/3)); await Frames(30);
        await Until(()=>((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().Any(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == "play" && ((float[])h.GetType().GetProperty("Rect")!.GetValue(h)!)[3]>bounds[3]*1.1f),"native mouse motion raises original card without a pipe pointer message",600);
        Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"local native hover does not mutate authoritative combat");
        Call(view,"SetControlEnabled",false); await Frames(20);
        Send(0x201,.89f,.82f); Send(0x202,.89f,.82f); await Frames(20);
        Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})! && (bool)Call(mirror,"Authorize",state)!,"watch-mode native clicks alter neither source nor replica");
        Call(view,"SetControlEnabled",true); await Frames(20);
        var fps=new System.Collections.Generic.List<double>(); var sourceFps=new System.Collections.Generic.List<double>();
        for(int i=0;i<20;i++) { await Frames(30); fps.Add((double)Field(process,"Fps")!); sourceFps.Add(Performance.GetMonitor(Performance.Monitor.TimeFps)); }
        fps.Sort(); sourceFps.Sort();
        GD.Print("[MultiInstanceSmoke] NATIVE FPS source median="+sourceFps[10]+" renderer median="+fps[10]+" renderer process_ms="+Field(process,"ProcessMs"));
        Check(Engine.MaxFps==120 && fps[10]>0,"120 FPS cadence enabled with live native telemetry");
        await Until(()=>(bool)Call(mirror,"Authorize",state)!,"native window ready for end-turn input");
        hits=((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().ToArray();
        var end=hits.First(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == "endTurn");
        bounds=(float[])end.GetType().GetProperty("Rect")!.GetValue(end)!;
        int turn=state.Players[0].PlayerCombatState!.TurnNumber;
        Send(0x201,bounds[0]+bounds[2]/2,bounds[1]+bounds[3]/2); Send(0x202,bounds[0]+bounds[2]/2,bounds[1]+bounds[3]/2);
        await Until(()=>state.Players[0].PlayerCombatState!.TurnNumber>turn && (bool)Call(mirror,"Authorize",state)!,"native HWND click reaches authority once and replays consistently");
    }
}
