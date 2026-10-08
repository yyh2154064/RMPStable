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
    private static async Task Frames(int n) { for(int i=0;i<n;i++) { Engine.MaxFps=30; await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame); } }
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
            var viewport=(SubViewport)Field(view,"_viewport")!;
            Check(viewport.GetChild<TextureRect>(0).Name=="NativeGameFrame" && !viewport.GetChildren().OfType<NCard>().Any(),"parent displays native image rather than reconstructed cards");
            var source=Static("_source")!; long epoch=(long)Call(source,"SetControlMode",true)!;
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
            var motion=Activator.CreateInstance(Type("MirrorMessage"))!;
            motion.GetType().GetProperty("Kind")!.SetValue(motion,"pointer"); motion.GetType().GetProperty("X")!.SetValue(motion,.7f); motion.GetType().GetProperty("Y")!.SetValue(motion,.5f);
            Call(mirror,"Presentation",motion);
            await Frames(15);
            Check(before==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"presentation traffic cannot mutate source gameplay");
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
}
