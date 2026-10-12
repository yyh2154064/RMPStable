using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

public static partial class MultiplayerVisibleProbe
{
    sealed class OpeningTrace { public string Tag {get;set;}=""; public string Card {get;set;}=""; public ulong Start {get;set;} public ulong End {get;set;} public bool Fast {get;set;} public bool Visible {get;set;} public ulong Owner {get;set;} }
    static bool OpeningCase => System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("opening");
    static string OpeningTracePath(string role,bool replica) => Path.Combine(Shared,role+(replica?"-replica":"")+"-opening-trace.jsonl");
    static void InstallOpeningPlayback()
    {
        if(!OpeningCase)return;
        new Harmony("RMP.OpeningPlayback19.Probe").Patch(typeof(CardModel).GetMethod("OnPlayWrapper")!,
            prefix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(OpeningBegan),Any)!),
            postfix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(OpeningTask),Any)!));
    }
    static void OpeningBegan(CardModel __instance,bool isAutoPlay,out OpeningTrace? __state)
    {
        __state=null;if(!isAutoPlay||__instance.Owner.NetId!=(Renderer?LocalContext.NetId:Id))return;
        var phase=ReadPath(Path.Combine(Shared,Role+"-opening-phase.json"));if(phase.ValueKind!=JsonValueKind.Object)return;
        __state=new(){Tag=phase.GetProperty("tag").GetString()!,Card=__instance.GetType().Name,Owner=__instance.Owner.NetId,
            Start=Time.GetTicksMsec(),Fast=Get(null,"Replica",Rmp("MirrorFastRestore")) is true||SaveManager.Instance.PrefsSave.FastMode==FastModeType.Instant,
            Visible=Renderer?Get(Get(null,"Active",Rmp("MirrorRenderer")),"_sceneReady") is true:!NGame.Instance.Transition.InTransition};
    }
    static void OpeningTask(OpeningTrace? __state,ref Task __result) {if(__state!=null)__result=FinishOpeningTrace(__state,__result);}
    static async Task FinishOpeningTrace(OpeningTrace trace,Task task)
    {try{await task;}finally{trace.End=Time.GetTicksMsec();File.AppendAllText(OpeningTracePath(Role,Renderer),JsonSerializer.Serialize(trace)+"\n");}}
    static List<OpeningTrace> OpeningRows(string role,bool replica,string tag)
    {var path=OpeningTracePath(role,replica);if(!File.Exists(path))return new();return File.ReadAllLines(path).Where(l=>l.Length>0).Select(l=>JsonSerializer.Deserialize<OpeningTrace>(l)!).Where(r=>r.Tag==tag).OrderBy(r=>r.Start).ToList();}
    static void OpeningPhase(string tag)=>Write(Role+"-opening-phase.json",new{tag});
    static void PrepareOpeningFixture(RunState state,bool upgraded)
    {
        SaveManager.Instance.PrefsSave.FastMode=FastModeType.Normal;
        foreach(var player in state.Players)
        {
            foreach(var relic in player.Relics.ToArray())player.RemoveRelicInternal(relic,silent:true);
            foreach(var potion in player.Potions.ToArray())player.DiscardPotionInternal(potion,silent:true);
            player.Deck.Clear(true);var card=state.CreateCard<Catastrophe>(player);
            if(upgraded){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
            card.EnchantInternal(ModelDb.Enchantment<Imbued>().ToMutable(),1);
            player.Deck.AddInternal(card,silent:true);
            for(int i=0;i<12;i++)player.Deck.AddInternal(state.CreateCard<DefendIronclad>(player),silent:true);
            player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
        }
    }
    static async Task OpeningFinished(string tag,int count)
    {await Wait(()=>!CombatManager.Instance.IsStarting&&!RunManager.Instance.ActionExecutor.IsRunning&&LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())?.PlayerCombatState?.Phase==PlayerTurnPhase.Play&&OpeningRows(Role,false,tag).Count==count,"native Imbued Catastrophe opening completes");}
    static void VerifyOpeningRows(List<OpeningTrace> rows,int children,string label,bool visible)
    {
        Check(rows.Count==children+1&&rows.Count(r=>r.Card==nameof(Catastrophe))==1&&rows.Count(r=>r.Card==nameof(DefendIronclad))==children,label+" plays Catastrophe and exactly "+children+" draw-pile cards");
        Check(rows.All(r=>!r.Fast),label+" uses original normal presentation throughout automatic effects");
        Check(rows.Where(r=>r.Card==nameof(DefendIronclad)).All(r=>r.End-r.Start>=200),label+" preserves readable native card animation waits");
        if(visible)Check(rows.All(r=>r.Visible),label+" opens native room before automatic cards begin");
    }
    static void CompareOpening(List<OpeningTrace> baseline,List<OpeningTrace> restored,string label)
    {
        ulong normal=baseline.Single(r=>r.Card==nameof(Catastrophe)).End-baseline.Single(r=>r.Card==nameof(Catastrophe)).Start;
        ulong actual=restored.Single(r=>r.Card==nameof(Catastrophe)).End-restored.Single(r=>r.Card==nameof(Catastrophe)).Start;
        Check(actual>=normal*.65,label+" retains normal-speed opening instead of compressing its animation");
        Write(Role+"-"+label+"-timing.json",new{baselineMilliseconds=normal,restoredMilliseconds=actual});
    }
    static object SingleMirror=>Get(null,"_mirror",Rmp("LiveSharingController"))!;
    static object SingleProc=>Get(SingleMirror,"Process")!;
    static int SinglePid=>((System.Diagnostics.Process)Get(SingleProc,"Child")!).Id;
    static async Task TestOpeningSingle()
    {
        for(int round=0;round<2;round++)
        {
            bool upgraded=round==1;int children=upgraded?3:2;string tag="single-"+children;
            RunManager.Instance.CleanUp();var player=Player.CreateForNewRun(ModelDb.Character<Defect>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
            var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPPLAYBACK"+round);
            PrepareOpeningFixture(state,upgraded);SaveManager.Instance.Progress.GetOrCreateCharacterStats(player.Character.Id);
            RunManager.Instance.SetUpTest(state,new NetSingleplayerGameService(),disableCombatStateSync:false,shouldSave:true);
            RunManager.Instance.GenerateRooms();await PreloadManager.LoadRunAssets(new[]{player.Character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);await NGame.Instance.Transition.FadeIn(.03f);
            Static("LiveSharingController","Open",state);await Wait(()=>Get(SingleProc,"DisplayReady") is true,"singleplayer resident map window");int pid=SinglePid;
            OpeningPhase(tag+"-baseline");await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
            await OpeningFinished(tag+"-baseline",children+1);var baseline=OpeningRows(Role,false,tag+"-baseline");VerifyOpeningRows(baseline,children,tag+" baseline",false);
            if(SaveManager.Instance.CurrentRunSaveTask is {} saving)await saving;
            await Wait(()=>Get(SingleMirror,"Verified") is true,"baseline original and replica state agreement");
            OpeningPhase(tag+"-sl");var quickSl=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")).First(t=>t!=null)!;
            await (Task)quickSl.GetMethod("RunSingleplayerSlAsync",Any)!.Invoke(null,null)!;
            var restored=RunManager.Instance.DebugOnlyGetState()!;Check(!ReferenceEquals(state,restored),tag+" mod Quick SL creates native restored run");
            await Wait(()=>Get(SingleProc,"DisplayReady") is true,"singleplayer SL window ready");
            Check(SinglePid==pid,tag+" SL reuses exact replica process");
            await OpeningFinished(tag+"-sl",children+1);await Wait(()=>Get(SingleMirror,"Verified") is true,"singleplayer restored complete native state");
            var actual=OpeningRows(Role,false,tag+"-sl");VerifyOpeningRows(actual,children,tag+" source SL",true);CompareOpening(baseline,actual,tag+"-source");
            await Wait(()=>OpeningRows(Role,true,tag+"-sl").Count==children+1,"singleplayer replica normal opening trace");
            var replica=OpeningRows(Role,true,tag+"-sl");VerifyOpeningRows(replica,children,tag+" replica SL",true);CompareOpening(baseline,replica,tag+"-replica");
            Check(Get(null,"Source",Rmp("MirrorFastRestore")) is false&&SaveManager.Instance.PrefsSave.FastMode==FastModeType.Normal,tag+" keeps original user animation preference");
        }
        Write(Role+"-result.json",new{passed=true,checks,scope="singleplayer normal Imbued Catastrophe two/three automatic plays after actual mod SL"});
    }
    static async Task TestOpeningMulti(RunState state)
    {
        int children=System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.EndsWith("3")?3:2;
        OpeningPhase("multi-baseline");await Barrier("opening-baseline");await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
        await OpeningFinished("multi-baseline",children+1);var baseline=OpeningRows(Role,false,"multi-baseline");VerifyOpeningRows(baseline,children,"multiplayer baseline",false);await Sync(state,"opening-baseline-state");
        OpeningPhase("multi-sl");await Barrier("opening-sl");var restored=await RestoreVisible(state);
        await OpeningFinished("multi-sl",children+1);await Sync(restored,"opening-restored-state");
        var actual=OpeningRows(Role,false,"multi-sl");VerifyOpeningRows(actual,children,"multiplayer source SL",true);CompareOpening(baseline,actual,"multi-source");
        await Wait(()=>OpeningRows(Role,true,"multi-sl").Count==children+1,"multiplayer replica normal opening trace");
        var replica=OpeningRows(Role,true,"multi-sl");var observedBaseline=OpeningRows(SourceRole,false,"multi-baseline");VerifyOpeningRows(replica,children,"multiplayer replica SL",true);CompareOpening(observedBaseline,replica,"multi-replica");
        Check(SaveManager.Instance.PrefsSave.FastMode==FastModeType.Normal,"multiplayer source retains native animation preference");
        Check(Child().GetProperty("rendering").GetBoolean()&&!Child().GetProperty("fastReplica").GetBoolean(),"restored visible opening retains native drawing and ordinary playback");
        Write(Role+"-result.json",new{passed=true,checks,scope="three original players normal Imbued Catastrophe opening after mod SL"});await Barrier("done");
    }
}
