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
public static partial class Smoke
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
        if(Environment.GetEnvironmentVariable("RMP_MULTI_FULL_TEST")=="1" || Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")=="1")
            new HarmonyLib.Harmony("MultiInstanceSmoke.StateDiagnostics").Patch(Type("MirrorState").GetMethod("Hash",Any)!,
                postfix:new HarmonyLib.HarmonyMethod(typeof(Smoke).GetMethod(nameof(SaveDiagnostic),Any)!));
        if(Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")=="renderer" && Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")=="1")
        { TaskHelper.RunSafely(RendererVisuals()); TaskHelper.RunSafely(RendererFeedback()); return; }
        var args = OS.GetCmdlineArgs(); int force = Array.IndexOf(args, "--force-steam");
        if (Environment.GetEnvironmentVariable("RMP_MULTI_TEST") != "1" || Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer" || force < 0 || args[force+1] != "off") return;
        if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        TaskHelper.RunSafely(Environment.GetEnvironmentVariable("RMP_MULTI_FEEDBACK_TEST") == "1" ? RunFeedback() : Environment.GetEnvironmentVariable("RMP_MULTI_FULL_TEST") == "1" ? RunFullNative() : Run());
    }
    private static string _diagnosticHash="";
    private static void SaveDiagnostic(string __result)
    {
        if(NRun.Instance?.TreasureRoom==null && NRun.Instance?.RestSiteRoom==null) return;
        if(__result==_diagnosticHash) return;
        _diagnosticHash=__result;
        var save=RunManager.Instance.ToSave(null);
        save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0; save.NumReloads=0; save.MapDrawings=null;
        File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"native-state.json"),JsonSerializationUtility.ToJson(save));
    }
    // Test-only local artifacts, never pixels in the production mirror protocol.
    private static async Task RendererVisuals()
    {
        var saved=new System.Collections.Generic.HashSet<string>();
        while(saved.Count<3)
        {
            await ((SceneTree)Engine.GetMainLoop()).ToSignal(Engine.GetMainLoop(),SceneTree.SignalName.ProcessFrame);
            if(NGame.Instance==null || NRun.Instance?.IsNodeReady()!=true || NCombatRoom.Instance?.IsNodeReady()!=true) continue;
            var renderer=Type("MirrorRenderer").GetField("Active",Any)!.GetValue(null);
            if(renderer==null || !(bool)Field(renderer,"_sceneReady")!) continue;
            var arrow=NRun.Instance.GlobalUi.TargetManager.GetNodeOrNull<MegaCrit.Sts2.Core.Nodes.Combat.NTargetingArrow>("TargetingArrow");
            string label=arrow?.IsVisibleInTree()==true ? "arrow" : ReplicaSettingsVisible() ? "settings" : "combat";
            if(saved.Contains(label)) continue;
            if(label is "arrow" or "settings")
            {
                var settling=System.Diagnostics.Stopwatch.StartNew();
                while(settling.Elapsed.TotalMilliseconds<300)
                    await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            string path=Path.Combine(Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!,"visual-"+label+".png");
            using var pixels=NGame.Instance.GetViewport().GetTexture().GetImage();
            pixels.SavePng(path); saved.Add(label); GD.Print("[MultiInstanceSmoke] VISUAL "+label);
        }
    }
    private static bool ReplicaSettingsVisible()
    {
        var monitor=Type("LiveSharingController").Assembly.GetType("RemoveMultiplayerPlayerLimit.Infrastructure.SceneMonitor")!;
        return monitor.GetMethod("FindSettingsScreen",Any)!.Invoke(null,null) is Control control && control.IsVisibleInTree();
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); GD.Print("[MultiInstanceSmoke] PASS " + label); }
    private static async Task Frames(int n)
    {
        for(int i=0;i<n;i++)
        {
            if(DisplayServer.GetName()!="headless")
            {
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus,true);
                if (DisplayServer.WindowGetMode()!=DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                if (DisplayServer.WindowGetPosition()!=new Vector2I(-30000,-30000)) DisplayServer.WindowSetPosition(new Vector2I(-30000,-30000));
            }
            Engine.MaxFps=DisplayServer.GetName()=="headless" ? 30 : 120;
            await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
    private static async Task Until(Func<bool> predicate, string label, int limit=2400)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew(); double diagnosticAt=5;
        for(int i=0;i<limit && clock.Elapsed.TotalSeconds<45 && !predicate();i++)
        {
            if (Static("_mirror") is { } mirror && (string)mirror.GetType().GetProperty("Error",Any)!.GetValue(mirror)! is { Length: > 0 } error) throw new Exception(error);
            if(clock.Elapsed.TotalSeconds>=diagnosticAt && Static("_mirror") is { } current)
            {
                diagnosticAt+=5; var process=Field(current,"Process")!;
                GD.Print("[MultiInstanceSmoke] WAIT "+label+" presentation="+Field(process,"Presentation")+" idle="+Field(process,"LastIdle")+" sourceFps="+Performance.GetMonitor(Performance.Monitor.TimeFps));
            }
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
            Check(!child.HasExited && ReferenceEquals(Static("_mirror"),mirror),"F8 close retains its authenticated renderer process");
            Check(Static("_view")==null,"F8 close removes shell and control permission");
            turn=state.Players[0].PlayerCombatState!.TurnNumber;
            NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
            await Until(()=>state.Players[0].PlayerCombatState!.TurnNumber>turn && Verified,"hidden replica keeps following authority events");
            string previousSession=(string)Field(Field(process,"Wire")!,"Identity")!.GetType().GetField("Session",Any)!.GetValue(Field(Field(process,"Wire")!,"Identity"))!;
            var reopenClock=System.Diagnostics.Stopwatch.StartNew();
            Controller.GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            view=Static("_view")!; Call(view,"SetControlEnabled",true); epoch=(long)Field(view,"_epoch")!;
            bool Exposed() => DisplayServer.GetName()=="headless" || mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror) is { } window && (bool)window.GetType().GetProperty("Visible",Any)!.GetValue(window)!;
            bool PermissionReady() => (long)Field(process,"LastControlEpoch")! == epoch && (bool)Field(process,"LastControlEnabled")!;
            await Until(()=>Verified && Exposed() && PermissionReady(),"warm reopen exposes consistent native window with active renderer permission");
            turn=state.Players[0].PlayerCombatState!.TurnNumber;
            var reopenedIntent=Intent("endTurn");
            var reopenedResult=Controller.GetMethod("ResolveIntent",Any)!.Invoke(null,new[]{(object)state,reopenedIntent})!;
            Check((bool)reopenedResult.GetType().GetProperty("Accepted")!.GetValue(reopenedResult)!,"warm reopen accepts a legitimate authority operation");
            Check(reopenClock.Elapsed.TotalMilliseconds<=1000,"warm F8 visible and first operation accepted within 1 second: "+reopenClock.Elapsed.TotalMilliseconds);
            var reopened=Field(Field(Static("_mirror")!,"Process")!,"Wire")!;
            Check((string)Field(reopened,"Identity")!.GetType().GetField("Session",Any)!.GetValue(Field(reopened,"Identity"))! == previousSession,"F8 reuses the same authenticated session");
            await Until(()=>state.Players[0].PlayerCombatState!.TurnNumber>turn && Verified,"warm reopened operation converges into the next actionable turn");
            var oldIntent=Intent("endTurn");
            var quickSl=Controller.Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")!;
            int confirmationEvents=(int)Field(process,"LastEvents")!;
            quickSl.GetMethod("TriggerRequested",Any)!.Invoke(null,null);
            await Until(()=>MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance?.OpenModal is Node { } modal && modal.IsNodeReady(),"native F5 confirmation opens");
            await Frames(15);
            Check(!Exposed(),"native SL confirmation is not covered by mirror window");
            var popup=(Node)MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance.OpenModal;
            popup!.GetNode<MegaCrit.Sts2.Core.Nodes.CommonUi.NPopupYesNoButton>("VerticalPopup/NoButton").ForceClick();
            await Until(()=>MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance.OpenModal==null && Exposed() && Verified,"cancelled SL returns to usable native window");
            Check((int)Field(process,"LastEvents")! == confirmationEvents,"cancelled SL does not add an unavailable replica modal operation");
            bool SourceOperable() => (bool)Type("MirrorHost").GetProperty("SourceIdle",Any)!.GetValue(null)! && NCombatRoom.Instance?.Ui.EndTurnButton.IsEnabled==true;
            var primaryRestoreClock=System.Diagnostics.Stopwatch.StartNew();
            await (Task)quickSl.GetMethod("RunSingleplayerSlAsync",Any)!.Invoke(null,null)!;
            await Until(SourceOperable,"primary SL reaches original actionable player phase");
            GD.Print("[MultiInstanceSmoke] PRIMARY SL from invocation to actionable="+primaryRestoreClock.Elapsed.TotalMilliseconds+" ms");
            var restoreClock=System.Diagnostics.Stopwatch.StartNew();
            state=RunManager.Instance.DebugOnlyGetState()!;
            await Until(()=>Static("_view")!=null && Verified,"singleplayer SL restores consistent native combat");
            view=Static("_view")!; Call(view,"SetControlEnabled",true); epoch=(long)Field(view,"_epoch")!;
            RejectIntent(oldIntent,"pre-SL intent rejected after restoring the same checkpoint");
            await Until(()=>Exposed() && PermissionReady() && (long)Field(process,"DrawFrames")!>0 && (bool)Field(process,"WindowVisible")!,"SL native window becomes visible, draws and has active renderer permission");
            var restoreResult=Controller.GetMethod("ResolveIntent",Any)!.Invoke(null,new[]{(object)state,Intent("endTurn")})!;
            Check((bool)restoreResult.GetType().GetProperty("Accepted")!.GetValue(restoreResult)!,"SL replica accepts first legitimate authority operation");
            Check(restoreClock.Elapsed.TotalMilliseconds<=1000,"SL additional wait and first accepted operation within 1 second: "+restoreClock.Elapsed.TotalMilliseconds);
            Check(primaryRestoreClock.Elapsed.TotalMilliseconds<=1000,"SL total restore and first accepted operation within 1 second: "+primaryRestoreClock.Elapsed.TotalMilliseconds);
            Check(!child.HasExited && ReferenceEquals(Static("_mirror"),mirror),"F5 preserves the exact renderer process");
            // The accepted operation is still starting its turn transition:
            // another SL must cancel that generation and reuse the same child.
            oldIntent=Intent("endTurn");
            primaryRestoreClock.Restart();
            await (Task)quickSl.GetMethod("RunSingleplayerSlAsync",Any)!.Invoke(null,null)!;
            await Until(SourceOperable,"consecutive primary SL reaches original actionable player phase");
            GD.Print("[MultiInstanceSmoke] PRIMARY consecutive SL from invocation to actionable="+primaryRestoreClock.Elapsed.TotalMilliseconds+" ms");
            restoreClock.Restart(); state=RunManager.Instance.DebugOnlyGetState()!;
            await Until(()=>Static("_view")!=null && Verified,"consecutive SL cancels pending old replay and converges");
            view=Static("_view")!; Call(view,"SetControlEnabled",true); epoch=(long)Field(view,"_epoch")!;
            RejectIntent(oldIntent,"previous SL generation cannot control the next reload");
            await Until(()=>Exposed() && PermissionReady() && (long)Field(process,"DrawFrames")!>0 && (bool)Field(process,"WindowVisible")!,"consecutive SL exposes current window, draws and has permission");
            restoreResult=Controller.GetMethod("ResolveIntent",Any)!.Invoke(null,new[]{(object)state,Intent("endTurn")})!;
            Check((bool)restoreResult.GetType().GetProperty("Accepted")!.GetValue(restoreResult)!,"consecutive SL accepts the first current operation");
            Check(restoreClock.Elapsed.TotalMilliseconds<=1000,"consecutive SL additional wait within 1 second: "+restoreClock.Elapsed.TotalMilliseconds);
            Check(primaryRestoreClock.Elapsed.TotalMilliseconds<=1000,"consecutive SL total restore within 1 second: "+primaryRestoreClock.Elapsed.TotalMilliseconds);
            Check(!child.HasExited && ReferenceEquals(Static("_mirror"),mirror),"consecutive SL retains exact renderer process");
            Controller.GetMethod("Close",Any)!.Invoke(null,null);
            Controller.GetMethod("Suspend",Any)!.Invoke(null,null);
            GD.Print("[MultiInstanceSmoke] ALL PASSED"); ((SceneTree)Engine.GetMainLoop()).Quit();
        }
        catch(Exception e) { GD.PrintErr("[MultiInstanceSmoke] FAIL "+e); Controller?.GetMethod("Suspend",Any)?.Invoke(null,null); ((SceneTree)Engine.GetMainLoop()).Quit(1); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(nint window,out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(nint window,out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(nint window,ref NativePoint point);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint window,uint message,nuint param,nint data,uint flags,uint timeout,out nuint result);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    private static async Task NativeWindowChecks(RunState state,object mirror,object process,object view)
    {
        await Until(()=>mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror) is { } window && (bool)window.GetType().GetProperty("Attached",Any)!.GetValue(window)! && (bool)Field(process,"NativeAttached")!,"renderer popup HWND is owned by the source window");
        var host=mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror)!;
        nint child=(nint)(long)Field(process,"Window")!;
        await Until(()=>IsWindowVisible(child) && (long)Field(process,"DrawFrames")! > 0,"embedded battle HWND is visible and has a draw callback (visual pixels still require inspection)");
        nint ownerWindow=(nint)host.GetType().GetProperty("OwnerWindow",Any)!.GetValue(host)!;
        GetWindowThreadProcessId(child,out uint owner);
        Check(owner==((System.Diagnostics.Process)process.GetType().GetProperty("Child",Any)!.GetValue(process)!).Id && GetParent(child)==ownerWindow,"only owned renderer HWND is attached");
        var panel=(Control)Field(view,"_panel")!; var content=(Control)Field(view,"_content")!;
        var saved=panel.Position; panel.Position+=new Vector2(20,10); await Frames(4);
        GetClientRect(child,out var childSize); GetWindowRect(child,out var placed);
        var transform=content.GetViewport().GetFinalTransform()*content.GetGlobalTransformWithCanvas();
        var expected=transform*Vector2.Zero; var expectedEnd=transform*content.Size;
        var screenOrigin=new NativePoint { X=(int)Math.Round(expected.X),Y=(int)Math.Round(expected.Y) }; ClientToScreen(ownerWindow,ref screenOrigin);
        Check(Math.Abs(childSize.Right-(expectedEnd.X-expected.X))<=2 && Math.Abs(childSize.Bottom-(expectedEnd.Y-expected.Y))<=2 && childSize.Right>100,"native popup follows content dimensions");
        Check(Math.Abs(placed.Left-screenOrigin.X)<=2 && Math.Abs(placed.Top-screenOrigin.Y)<=2,"native popup aligns with the displayed panel in OS screen coordinates");
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
        GD.Print("[MultiInstanceSmoke] SKIP physical hover: synthetic messages cannot move the desktop cursor; native drag is tested separately");
        Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"native motion message does not mutate authoritative combat");
        Call(view,"SetControlEnabled",false); await Frames(20);
        Send(0x201,.89f,.82f); Send(0x202,.89f,.82f); await Frames(20);
        Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})! && (bool)Call(mirror,"Authorize",state)!,"watch-mode native clicks alter neither source nor replica");
        Call(view,"SetControlEnabled",true); await Frames(20);
        async Task ClickUi(string kind)
        {
            // Native submenu buttons animate into position for 0.35 seconds.
            // Wait for that animation before using the latest reported hitbox.
            var settling=System.Diagnostics.Stopwatch.StartNew();
            while(settling.Elapsed.TotalMilliseconds<600) await Frames(1);
            var hit=((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().First(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == kind);
            var rect=(float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!;
            Send(0x201,rect[0]+rect[2]/2,rect[1]+rect[3]/2); Send(0x202,rect[0]+rect[2]/2,rect[1]+rect[3]/2);
        }
        await ClickUi("ui.pause"); await Until(()=>(string)Field(process,"Presentation")! == "menu","native pause button opens replica menu");
        await ClickUi("ui.settings"); await Until(()=>(string)Field(process,"Presentation")! == "settings","native settings button opens original settings screen");
        await Frames(12);
        await ClickUi("ui.back"); await Until(()=>(string)Field(process,"Presentation")! == "menu","settings native back control responds");
        await ClickUi("ui.resume"); await Until(()=>(string)Field(process,"Presentation")! == "combat" && Verified,"native resume returns to consistent combat");
        Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"presentation clicks do not change authoritative combat");
        await ClickUi("ui.pause"); await Until(()=>(string)Field(process,"Presentation")! == "menu","pause menu ready for authoritative action");
        await ClickUi("ui.menu.GiveUp");
        await Until(()=>MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance?.OpenModal is Node node && node.IsNodeReady(),"mirror give-up button opens original source confirmation");
        await Frames(15);
        Check(!IsWindowVisible(child) && !(bool)((CanvasLayer)Field(view,"_overlay")!).Visible,"source confirmation is not covered by native window or panel");
        ((Node)MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance.OpenModal).GetNode<MegaCrit.Sts2.Core.Nodes.CommonUi.NPopupYesNoButton>("VerticalPopup/NoButton").ForceClick();
        System.Collections.Generic.IEnumerable<Node> All(Node root)
        { yield return root; foreach(var nested in root.GetChildren().SelectMany(All)) yield return nested; }
        await Frames(15);
        All((Node)MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance.CurrentCapstoneScreen)
            .OfType<MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu.NPauseMenuButton>().First(b=>b.Name.ToString()=="Resume").ForceClick();
        await Until(()=>IsWindowVisible(child) && Verified,"cancelled give-up resumes the usable native mirror");
        Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"cancelled give-up leaves source and replica models unchanged");
        var attack=state.Players[0].PlayerCombatState!.Hand.Cards.First(c=>c.TargetType==MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy && NCombatRoom.Instance!.CreatureNodes.Any(n=>n.Entity.IsEnemy && c.CanPlayTargeting(n.Entity)));
        int attackIndex=state.Players[0].PlayerCombatState!.Hand.Cards.ToList().IndexOf(attack);
        var enemy=NCombatRoom.Instance!.CreatureNodes.First(n=>n.Entity.IsEnemy && attack.CanPlayTargeting(n.Entity)).Entity;
        int enemyIndex=enemy.CombatState.Creatures.ToList().IndexOf(enemy);
        float[] HitRect(string kind,int nativeIndex)
        {
            var hit=((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().First(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == kind && (int)h.GetType().GetProperty("Index")!.GetValue(h)! == nativeIndex);
            return (float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!;
        }
        var attackBounds=HitRect("play",attackIndex); var enemyBounds=HitRect("target",enemyIndex);
        int oldEnergy=state.Players[0].PlayerCombatState!.Energy;
        int oldHealth=enemy.CurrentHp;
        Send(0x201,attackBounds[0]+attackBounds[2]/2,attackBounds[1]+attackBounds[3]/2);
        Send(0x200,enemyBounds[0]+enemyBounds[2]/2,enemyBounds[1]+enemyBounds[3]/2);
        await Until(()=>(bool)Field(process,"TargetArrowVisible")!,"HWND drag activates original targeting arrow");
        await Frames(12);
        Send(0x202,enemyBounds[0]+enemyBounds[2]/2,enemyBounds[1]+enemyBounds[3]/2);
        await Until(()=>state.Players[0].PlayerCombatState!.Energy<oldEnergy && enemy.CurrentHp<oldHealth && Verified,"HWND attack drag damages target on authority and converges");
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
