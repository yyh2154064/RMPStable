using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment=System.Environment;

[ModInitializer(nameof(Initialize))]
public static class TimerSyncProbe
{
    private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static Type Rmp(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing."+name)).First(t=>t!=null)!;
    private static object? Static(string name)=>Rmp("LiveSharingController").GetField(name,Any)!.GetValue(null);
    private static bool Renderer=>Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")=="renderer";
    private static string Output=>Renderer?Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!:Environment.GetEnvironmentVariable("RMP_TIMER_EVIDENCE")!;
    private static string Text=>NRun.Instance.GlobalUi.TopBar.Timer.GetNode<Label>("TimerLabel").Text;
    private static async Task Frames(int count){var tree=(SceneTree)Engine.GetMainLoop();for(int i=0;i<count;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    private static async Task Pause(int ms){ulong end=Time.GetTicksMsec()+(ulong)ms;while(Time.GetTicksMsec()<end)await Frames(1);}
    private static async Task Wait(Func<bool> condition,string label){ulong end=Time.GetTicksMsec()+90000;while(Time.GetTicksMsec()<end){if(condition())return;await Frames(1);}throw new Exception("Timeout "+label);}
    private static void ShiftLocalClock(long seconds)
    {
        var f=typeof(RunManager).GetField("_prevSessionRunTime",Any)??typeof(RunManager).GetField("_prevRunTime",Any);
        if(f==null)throw new Exception("Timer fields: "+string.Join(";",typeof(RunManager).GetFields(Any).Select(x=>x.Name+":"+x.FieldType.FullName)));
        f.SetValue(RunManager.Instance,(long)f.GetValue(RunManager.Instance)!+seconds);
    }
    public static void Initialize()
    {
        if(Renderer&&Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")=="1")_ = TaskHelper.RunSafely(Observe());
        else if(Environment.GetEnvironmentVariable("RMP_MULTI_TIMER_TEST")=="1")_ = TaskHelper.RunSafely(Run());
    }
    private static async Task Observe()
    {
        await Wait(()=>NRun.Instance?.GlobalUi?.TopBar?.Timer!=null,"replica timer");
        ShiftLocalClock(120); // Wrong local clock must never affect the native label.
        while(true){await Frames(6);if(NRun.Instance==null)continue;File.WriteAllText(Path.Combine(Output,"clock-state.json"),JsonSerializer.Serialize(new{text=Text,localTime=RunManager.Instance.RunTime}));}
    }
    private static async Task Run()
    {
        int exit=0;try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Pause(2000);SaveManager.Instance.SetFtuesEnabled(false);
            var canvas=new CanvasLayer{Layer=120};NGame.Instance.AddChild(canvas);var label=new Label{Text="计时专项：无鼠标或键盘输入，窗口跟随本体计时",Position=new Vector2(20,75),MouseFilter=Control.MouseFilterEnum.Ignore};label.AddThemeFontSizeOverride("font_size",26);canvas.AddChild(label);
            var character=ModelDb.Character<Ironclad>();var state=RunState.CreateForNewRun(new[]{Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1)},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPTIMER13");
            RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
            // Establish a nonzero authority clock before the replica is started.
            ShiftLocalClock(60);
            Rmp("LiveSharingController").GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            var mirror=Static("_mirror")!;await Wait(()=>(bool)mirror.GetType().GetProperty("Verified",Any)!.GetValue(mirror)!,"verified");
            object process=mirror.GetType().GetField("Process",Any)!.GetValue(mirror)!;
            string child=(string)process.GetType().GetField("DirectoryPath",Any)!.GetValue(process)!;
            string path=Path.Combine(child,"clock-state.json");await Wait(()=>File.Exists(path),"replica observation");await Pause(500);
            int checks=0;long injectedOffset=0;
            for(int i=0;i<12;i++)
            {
                if(i==6){ShiftLocalClock(-20);injectedOffset=-20;}
                // Observe the original one-second timer update, then allow the
                // pipe/observer to settle; no synthetic timer or UI input.
                string before=Text;await Wait(()=>Text!=before,"original clock tick");await Pause(250);
                using var sample=JsonDocument.Parse(File.ReadAllText(path));string replica=sample.RootElement.GetProperty("text").GetString()!;
                long local=sample.RootElement.GetProperty("localTime").GetInt64();bool verified=(bool)mirror.GetType().GetProperty("Verified",Any)!.GetValue(mirror)!;
                bool equal=replica==Text;
                File.AppendAllText(Path.Combine(Output,"checks.jsonl"),JsonSerializer.Serialize(new{sample=i,source=Text,replica,local,sourceTime=RunManager.Instance.RunTime,injectedOffset,equal,verified})+"\n");
                if(!equal||!verified)throw new Exception("Clock mismatch: source="+Text+" replica="+replica+" verified="+verified);
                checks++;
            }
            File.WriteAllText(Path.Combine(Output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks,replicaClockOffset=120,sourceRewind=20,noInjectedInput=true}));
            GD.Print("[TimerSync] RESULT checks="+checks+" local offset ignored; authority rewind followed");
        }
        catch(Exception e){exit=1;GD.Print("[TimerSync] FAIL "+e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
        Rmp("LiveSharingController").GetMethod("Suspend",Any)!.Invoke(null,null);((SceneTree)Engine.GetMainLoop()).Quit(exit);
    }
}
