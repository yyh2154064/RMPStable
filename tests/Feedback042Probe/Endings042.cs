using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class MultiplayerVisibleProbe
{
    static object ArchitectSnapshot()
    {
        var model=RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is EventRoom?RunManager.Instance.EventSynchronizer?.GetLocalEvent():null;
        var dialogue=model is TheArchitect?Get(model,"_dialogue") as MegaCrit.Sts2.Core.Entities.Ancients.AncientDialogue:null;
        return new{model=model?.Id.ToString(),line=model is TheArchitect?Get(model,"_currentLineIndex"):null,dialogue=dialogue?.Lines.Select(l=>l.LineText?.GetFormattedText()).ToArray(),
            options=NRun.Instance==null?Array.Empty<string>():Descendants<NEventOptionButton>(NRun.Instance).Where(b=>b.IsVisibleInTree()&&b.IsEnabled).Select(b=>b.Option.TextKey).ToArray()};
    }
    static async Task TestFeedbackEnding(RunState state,bool victory)
    {
        if(victory)
        {
            // A real progressed source profile selects later Architect lines;
            // the isolated replica initially has a fresh profile.
            foreach(var player in state.Players)SaveManager.Instance.Progress.GetOrCreateCharacterStats(player.Character.Id).TotalWins=2;
            await RunManager.Instance.SetActInternal(state.Acts.Count-1);await RunManager.Instance.LoadIntoLatestMapCoord(null);await Barrier("042-final-act");
            foreach(var point in new[]{state.Map.BossMapPoint,state.Map.SecondBossMapPoint}.Where(p=>p!=null))
            {
                await Barrier("042-before-boss-"+point.coord);await RunManager.Instance.EnterMapCoord(point.coord);
                await Wait(()=>CombatManager.Instance.IsInProgress&&!CombatManager.Instance.IsStarting&&!RunManager.Instance.ActionExecutor.IsRunning,"042 boss initialized");await Barrier("042-boss-ready-"+point.coord);
                for(int phase=0;phase<4&&CombatManager.Instance.IsInProgress;phase++)
                {
                    await CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState().Enemies.Where(c=>c.IsAlive).ToArray(),true);
                    foreach(var subject in CombatManager.Instance.DebugOnlyGetState().Enemies.Where(c=>c.IsDead&&c.Monster is MegaCrit.Sts2.Core.Models.Monsters.TestSubject&&c.HasPower<MegaCrit.Sts2.Core.Models.Powers.AdaptablePower>()).ToArray())
                        await (Task)Call(subject.Monster,"RespawnMove",state.Players.Select(p=>p.Creature).ToList())!;
                    await CombatManager.Instance.CheckWinCondition();
                }
                await Wait(()=>NOverlayStack.Instance.Peek() is NRewardsScreen,"042 original boss rewards");await Sync(state,"042-boss-ended-"+point.coord);
                await Wait(()=>Get(NOverlayStack.Instance.Peek(),"_proceedButton") is NClickableControl b&&b.IsEnabled,"042 boss proceed ready");
                ((NClickableControl)Get(NOverlayStack.Instance.Peek(),"_proceedButton")!).ForceClick();await Barrier("042-boss-continued-"+point.coord);await Frames(30);
            }
            // The real final boss rewards button above sends the native act
            // vote. Do not send a second vote while its transition is running.
            await Wait(()=>state.CurrentRoom?.IsVictoryRoom==true&&Get(Proc,"DisplayReady") is true&&Get(Mirror,"Verified") is true,"042 Architect after final boss",120);
            await Barrier("042-architect-entry");
            bool LocalReady() => ((System.Collections.IEnumerable)Get(RunManager.Instance.ActChangeSynchronizer,"_readyPlayers")!).Cast<bool>().ElementAt(state.GetPlayerSlotIndex(LocalContext.GetMe(state)!));
            for(int line=0;line<8;line++)
            {
                await Wait(()=>NOverlayStack.Instance.Peek() is NGameOverScreen||LocalReady()||Descendants<NEventOptionButton>(NRun.Instance).Any(b=>b.IsEnabled&&b.IsVisibleInTree()),"042 original Architect option or ready vote",120);
                Write(Role+"-architect-"+line+".json",ArchitectSnapshot());
                Write(Role+"-architect-round-"+line+".json",new{ended=NOverlayStack.Instance.Peek() is NGameOverScreen,ready=LocalReady()});
                await Barrier("042-dialogue-"+line);await Frames(15);
                if(Roles.All(r=>ReadPath(Path.Combine(Root,r+"-architect-round-"+line+".json")).GetProperty("ended").GetBoolean()))break;
                var expected=ReadPath(Path.Combine(Root,SourceRole+"-architect-"+line+".json"));
                var sourceRound=ReadPath(Path.Combine(Root,SourceRole+"-architect-round-"+line+".json"));
                if(!sourceRound.GetProperty("ended").GetBoolean()&&!sourceRound.GetProperty("ready").GetBoolean()&&expected.GetProperty("options").GetArrayLength()>0)
                {
                    await Wait(()=>FeedbackState().GetProperty("architect").GetProperty("line").GetRawText()==expected.GetProperty("line").GetRawText(),"042 source dialogue reaches replica");
                    Check(FeedbackState().GetProperty("architect").GetProperty("line").GetRawText()==expected.GetProperty("line").GetRawText(),"Architect source dialogue line follows progressed profile "+line);
                    Check(FeedbackState().GetProperty("architect").GetProperty("dialogue").GetRawText()==expected.GetProperty("dialogue").GetRawText(),"Architect native dialogue text matches source progress "+line);
                    if(!Child().GetProperty("control").GetBoolean())Static("MultiplayerMirrorSession","RequestControl",true);
                    await Wait(()=>Child().GetProperty("control").GetBoolean()&&Child().GetProperty("inputReady").GetBoolean(),"042 direct Architect control");
                    await Remote("native","NEventOptionButton");
                }
                await Barrier("042-dialogue-click-"+line);await Frames(70);
            }
        }
        else
        {
            foreach(var player in state.Players)player.Creature.SetCurrentHpInternal(1);
            await Barrier("042-lethal-hp");await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
            await Wait(()=>CombatManager.Instance.IsInProgress&&!CombatManager.Instance.IsStarting&&!RunManager.Instance.ActionExecutor.IsRunning&&Get(Mirror,"Verified") is true,"042 lethal monster battle");await Barrier("042-lethal-ready");
            for(int turn=0;turn<12&&NOverlayStack.Instance.Peek() is not NGameOverScreen;turn++)
            {
                await Barrier("042-enemy-turn-"+turn);
                var me=LocalContext.GetMe(state)!;
                if(me.Creature.IsAlive)RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me,me.PlayerCombatState!.TurnNumber));
                await Wait(()=>NOverlayStack.Instance.Peek() is NGameOverScreen||!CombatManager.Instance.IsEnemyTurnStarted&&!CombatManager.Instance.EndingPlayerTurnPhaseOne&&!CombatManager.Instance.EndingPlayerTurnPhaseTwo&&state.Players.Where(p=>p.Creature.IsAlive).All(p=>p.PlayerCombatState!.TurnNumber>turn+1),"042 real monster turn resolves",120);
            }
        }
        await Wait(()=>NOverlayStack.Instance.Peek() is NGameOverScreen,"042 original final screen",120);await Barrier("042-source-ended");
        await Wait(()=>FeedbackState().ValueKind==System.Text.Json.JsonValueKind.Object&&FeedbackState().GetProperty("ending").GetProperty("screen").GetString()=="NGameOverScreen"&&Get(Mirror,"Verified") is true,"042 mirrored final screen",120);
        Write(Role+"-042-ending-source.json",EndingSnapshot());await Barrier("042-final-snapshots");
        var source=ReadPath(Path.Combine(Root,SourceRole+"-042-ending-source.json"));var actual=FeedbackState().GetProperty("ending");
        foreach(string field in new[]{"local","character","score","win"})Check(actual.GetProperty(field).GetRawText()==source.GetProperty(field).GetRawText(),"042 native ending "+field+" equals selected source");
        Check(actual.GetProperty("win").GetBoolean()==victory,"042 correct "+(victory?"victory":"monster defeat")+" outcome");
        await Wait(()=>FeedbackState().GetProperty("ending").GetProperty("continueReady").GetBoolean(),"042 result continue ready");await Grant("042-ending");await Remote("ending");
        await Wait(()=>FeedbackState().GetProperty("ending").GetProperty("summary").GetBoolean(),"042 native score summary follows");
        Write(Role+"-result.json",new{passed=true,checks,scope="v0.4.2 "+(victory?"final bosses, progressed Architect dialogue and native victory":"real monster lethal turn and native defeat")+" summary"});await Barrier("042-ending-done");
    }
}
