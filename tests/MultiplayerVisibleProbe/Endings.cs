using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Helpers;

public static partial class MultiplayerVisibleProbe
{
    static int endingReadonlyHandled;
    static object EndingSnapshot()
    {
        var screen=NOverlayStack.Instance?.Peek() as NGameOverScreen;
        var player=screen==null?null:Get(screen,"_localPlayer") as Player;
        return new{screen=screen?.GetType().Name,local=player?.NetId,character=player?.Character.GetType().Name,
            win=screen==null?null:Get(Get(screen,"_history"),"Win"),score=screen==null?null:Get(screen,"_score"),
            continueReady=screen!=null&&Get(screen,"_continueButton") is NClickableControl c&&c.IsEnabled&&c.IsVisibleInTree(),
            mainReady=screen!=null&&Get(screen,"_mainMenuButton") is NClickableControl m&&m.IsEnabled&&m.IsVisibleInTree(),
            summary=screen!=null&&Get(screen,"_isAnimatingSummary") is true&&Get(screen,"_summaryContainer") is Control s&&s.IsVisibleInTree()};
    }
    static async Task ObserveEnding()
    {
        if(System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")?.StartsWith("ending")!=true)return;
        var request=ReadPath(Path.Combine(Root,"visible-ending-readonly.json"));
        if(request.ValueKind==System.Text.Json.JsonValueKind.Object&&request.GetProperty("seq").GetInt32()!=endingReadonlyHandled&&NOverlayStack.Instance?.Peek() is NGameOverScreen screen){
            var renderer=Get(null,"Active",Rmp("MirrorRenderer"))!;long before=(long)Get(renderer,"_request")!;await NativeClick(renderer,NativePoint((Control)Get(screen,"_continueButton")!));endingReadonlyHandled=request.GetProperty("seq").GetInt32();
            WritePath(Path.Combine(Root,"visible-ending-readonly-output.json"),new{seq=endingReadonlyHandled,unchanged=(long)Get(renderer,"_request")! ==before&&Get(screen,"_isAnimatingSummary") is false});
        }
        WritePath(Path.Combine(Root,"visible-ending.json"),EndingSnapshot());
    }
    static System.Text.Json.JsonElement ChildEnding()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-ending.json"));
    static bool ChildEndingReady(ulong actor)
    {
        var e=ChildEnding();return e.ValueKind==System.Text.Json.JsonValueKind.Object&&e.GetProperty("local").ValueKind==System.Text.Json.JsonValueKind.Number&&e.GetProperty("local").GetUInt64()==actor&&e.GetProperty("mainReady").GetBoolean();
    }
    static async Task TestEnding(RunState state,bool victory)
    {
        int originalPid=Pid;
        if(victory){await Enter(RoomType.Event,"native-victory-architect",ModelDb.Event<TheArchitect>());await Sync(state,"architect-entry");}
        else{await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning&&(bool)Get(Mirror,"Verified")!,"defeat fixture ready");await Barrier("defeat-fixture-ready");}
        await Stage(victory?"完整结算专项 · 原版胜利流程":"完整结算专项 · 原版全队死亡");await Barrier("before-native-game-over");
        if(victory)await (Task)Call(RunManager.Instance,"WinRun")!;
        else{await CreatureCmd.Kill(state.Players.Select(p=>p.Creature).ToArray(),true);await CombatManager.Instance.CheckWinCondition();}
        await Wait(()=>NOverlayStack.Instance?.Peek() is NGameOverScreen,"original game-over screen");await Barrier("native-game-over");
        await Wait(()=>ChildEnding().ValueKind==System.Text.Json.JsonValueKind.Object&&ChildEnding().GetProperty("screen").GetString()=="NGameOverScreen"&&(bool)Get(Mirror,"Verified")!,"native replica full ending");
        Write(Role+"-ending.json",EndingSnapshot());await Barrier("ending-snapshots");var expected=ReadPath(Path.Combine(Root,SourceRole+"-ending.json"));var child=ChildEnding();
        Check(child.GetProperty("local").GetUInt64()==SourceId,"ending is the selected player's native personal result");
        Check(child.GetProperty("win").GetBoolean()==victory&&child.GetProperty("score").GetInt32()==expected.GetProperty("score").GetInt32(),"native victory/defeat outcome and score match source");
        Check(!Child().GetProperty("control").GetBoolean(),"game over revokes previous room permission");await Sync(state,"native-ending-state");
        await Wait(()=>Get(NOverlayStack.Instance.Peek(),"_continueButton") is NClickableControl c&&c.IsEnabled&&c.IsVisibleInTree()&&ChildEnding().GetProperty("continueReady").GetBoolean(),"native final Continue animation ready");
        await GrantBattle("ending-denied",deny:true);int readonlySeq=++sequence;string directory=(string)Get(Proc,"DirectoryPath")!;WritePath(Path.Combine(directory,"visible-ending-readonly.json"),new{seq=readonlySeq});await Wait(()=>ReadPath(Path.Combine(directory,"visible-ending-readonly-output.json")).ValueKind==System.Text.Json.JsonValueKind.Object,"readonly native ending click");Check(ReadPath(Path.Combine(directory,"visible-ending-readonly-output.json")).GetProperty("unchanged").GetBoolean()&&!ChildEnding().GetProperty("summary").GetBoolean(),"denied native window click cannot advance ending summary");
        await GrantBattle("ending-allowed");await RemoteBattle("ending");await Wait(()=>ChildEnding().GetProperty("summary").GetBoolean()&&Get(NOverlayStack.Instance.Peek(),"_summaryContainer") is Control s&&s.IsVisibleInTree(),"authorized native Continue opens source and replica summary");await Sync(state,"ending-summary");
        Check(Pid==originalPid,"native ending and summary preserve resident replica");
        await Wait(()=>Get(NOverlayStack.Instance.Peek(),"_mainMenuButton") is NClickableControl c&&c.IsEnabled&&c.IsVisibleInTree()&&ChildEnding().GetProperty("mainReady").GetBoolean(),"full native score/discovery animation completed",120);
        await Stage("完整结算专项 · 经好友授权继续结束整局");
        allowExpectedEndingOffline=true;
        // Keep the host's own run open to assist both friends in succession.
        // A peer that has returned to its menu no longer has a run UI lease.
        foreach(ulong actor in new ulong[]{2,3,1}){
            await Barrier("before-ending-menu-"+actor);
            if(actor!=2){
                if(Host){Static("LiveSharingController","SelectMultiplayerSource",actor.ToString());await Wait(()=>(bool)Get(Proc,"DisplayReady")!&&ChildEndingReady(actor),"switch to remaining native ending source");Static("MultiplayerMirrorSession","RequestControl",true);}
                if(Id==actor&&actor!=1){await Wait(()=>Session("_pendingViewer") is ulong viewer&&viewer==1,"host requests final friend permission");((Button)Session("_allowButton")!).EmitSignal(Button.SignalName.Pressed);}
                if(Host)await Wait(()=>Child().GetProperty("control").GetBoolean()&&Child().GetProperty("inputReady").GetBoolean(),"remaining native ending source grant");
                await Barrier("ending-menu-granted-"+actor);
            }
            if(Host)await RemoteBattle("ending");
            if(Id==actor){await Wait(()=>RunManager.Instance.DebugOnlyGetState()==null&&NRun.Instance==null,"source original ending completes whole run");Write(Role+"-finished-ending.json",new{finished=true,pid=System.Environment.ProcessId});}
            await Barrier("after-ending-menu-"+actor);
        }
        var resident=Get(null,"Host",Rmp("MirrorResident"))!;Check(((System.Diagnostics.Process)Get(Get(resident,"Process"),"Child")!).Id==originalPid&&!((System.Diagnostics.Process)Get(Get(resident,"Process"),"Child")!).HasExited,"all peers exit their run without destroying resident replica");
        await Stage("完整结算专项 · 同一主进程开始新局");await StartAfterEnding(originalPid);
        Write(Role+"-result.json",new{passed=true,checks,visible=System.Environment.GetEnvironmentVariable("RMP_VISIBLE_SILENT")!="1",actualRendering=true,scope="independent full native "+(victory?"victory":"defeat")+" ending, source approval, Continue/summary, whole run finish and same-process next run"});await Barrier("ending-done");
    }
    static async Task StartAfterEnding(int pid)
    {
        networkPump=true;INetGameService service;await Barrier("new-run-network-start");
        if(Host){var host=new NetHostGameService(PeerVersionInfo.LocalDefault());service=host;if(host.StartENetHost(33799,3).HasValue)throw new Exception("New run ENet host failed");_ = TaskHelper.RunSafely(PumpNetwork(host));Write("next-host-listening.json",new{ready=true});await Wait(()=>host.ConnectedPeers.Count==2,"new run peers connected");host.SetPeerReadyForBroadcasting(2);host.SetPeerReadyForBroadcasting(3);}
        else{await Wait(()=>File.Exists(Path.Combine(Root,"next-host-listening.json")),"new run host listening");var client=new NetClientGameService(PeerVersionInfo.LocalDefault());service=client;var transport=new ENetClient(client);client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);if((await transport.ConnectToHost(Id,"127.0.0.1",33799)).HasValue)throw new Exception("New run ENet client failed");_ = TaskHelper.RunSafely(PumpNetwork(client));}
        var players=new[]{Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1),Player.CreateForNewRun(ModelDb.Character<Silent>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),2),Player.CreateForNewRun(ModelDb.Character<Defect>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),3)};
        var state=RunState.CreateForNewRun(players,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPVISIBLEAFTERENDING");
        RunManager.Instance.SetUpTest(state,service,disableCombatStateSync:false,shouldSave:false);RunManager.Instance.GenerateRooms();await PreloadManager.LoadRunAssets(players.Select(p=>p.Character));RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);networkPump=false;await Barrier("next-run-loaded");
        Static("LiveSharingController","SelectMultiplayerSource",DefaultSource.ToString());await Wait(()=>(bool)Get(Proc,"DisplayReady")!&&Child().GetProperty("local").GetUInt64()==DefaultSource,"same-process next run native replica");await Sync(state,"next-run-map");Check(Pid==pid,"starting a new multiplayer run reuses the exact resident PID");
    }
}
