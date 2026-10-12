using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static NCrystalSphereScreen? DivinationScreen=>NOverlayStack.Instance==null?null:Descendants<NCrystalSphereScreen>(NOverlayStack.Instance).LastOrDefault();
    static CrystalSphereMinigame? Divination=>DivinationScreen==null?null:Get(DivinationScreen,"_entity") as CrystalSphereMinigame;
    static object CrystalSnapshot()=>new{count=Divination?.DivinationCount??-1,tool=Divination?.CrystalSphereTool.ToString()??"",small=DivinationScreen==null?"":Static("MirrorNativeUi","Key",Get(DivinationScreen,"_smallDivinationButton")!),big=DivinationScreen==null?"":Static("MirrorNativeUi","Key",Get(DivinationScreen,"_bigDivinationButton")!),fog=Divination==null?"":string.Join("",Divination.cells.Cast<CrystalSphereCell>().Select(c=>c.IsHidden?"1":"0")),board=Divination?.Items.Select(i=>new{type=i.GetType().Name,x=i.Position.X,y=i.Position.Y,w=i.Size.X,h=i.Size.Y}).ToArray(),cards=Divination?.Items.OfType<CrystalSphereCardReward>().Select(i=>new{x=i.Position.X,y=i.Position.Y}).ToArray(),rewards=NOverlayStack.Instance==null?Array.Empty<object>():Descendants<NRewardButton>(NOverlayStack.Instance).Where(b=>b.IsEnabled&&b.IsVisibleInTree()).Select(b=>(object)new{path=Static("MirrorNativeUi","Key",b),type=b.Reward?.GetType().Name}).ToArray()};
    static JsonElement ChildCrystal()=>ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-crystal.json"));
    static bool CrystalMatches(){var original=ReadPath(Path.Combine(Root,SourceRole+"-crystal.json"));var child=ChildCrystal();return original.ValueKind==JsonValueKind.Object&&child.ValueKind==JsonValueKind.Object&&new[]{"count","tool","fog","board"}.All(k=>original.GetProperty(k).GetRawText()==child.GetProperty(k).GetRawText());}
    static async Task TestSpecialEvents(RunState state)
    {
        int pid=Pid;
        string eventCase=System.Environment.GetEnvironmentVariable("RMP_VISIBLE_CASE")!;
        foreach(int count in eventCase=="eventsLantern"?Array.Empty<int>():eventCase=="eventsRemaining"?new[]{6}:new[]{3,6}){
            string tag="crystal-"+count;await Enter(RoomType.Event,tag,ModelDb.Event<CrystalSphere>());await Sync(state,tag+"-entry");await Grant(tag);
            await Stage("多人占卜专项 · 原版 "+count+" 次路线");await Remote("native","NEventOptionButton",count==3?0:1);
            await Wait(()=>Divination?.DivinationCount==count&&CrystalMatches(),"native three-peer divination board "+count);Check(CrystalMatches(),"selected-source original board and fog match "+count);
            await Remote("native","="+ChildCrystal().GetProperty("small").GetString());await Wait(()=>ChildCrystal().GetProperty("tool").GetString()=="Small"&&CrystalMatches(),"native small divination tool");Check(CrystalMatches(),"native small tool decision synchronized");
            await Remote("native","="+ChildCrystal().GetProperty("big").GetString());await Wait(()=>ChildCrystal().GetProperty("tool").GetString()=="Big"&&CrystalMatches(),"native big divination tool");
            for(int step=0;step<count;step++){
                var snapshot=ChildCrystal();var cards=snapshot.GetProperty("cards").EnumerateArray().ToArray();var card=cards[step%cards.Length];int x=card.GetProperty("x").GetInt32(),y=card.GetProperty("y").GetInt32();
                var hidden=Divination!.cells.Cast<CrystalSphereCell>().Where(c=>c.IsHidden).ToArray();
                // Use the selected source's board for targeting, never viewer's board.
                int slot=x*11+y;string fog=snapshot.GetProperty("fog").GetString()!;if(fog[slot]!='1')slot=fog.IndexOf('1');
                await Stage("多人占卜专项 · "+count+" 次路线第 "+(step+1)+" 次揭格");await Remote("native","=NCrystalSphereCell#"+slot);
                await Wait(()=>ChildCrystal().GetProperty("count").GetInt32()==count-step-1&&CrystalMatches(),"exactly one native divination and fog convergence "+step);Check(CrystalMatches(),"one click consumes one divination and matches source fog "+step);await Sync(state,tag+"-cell-"+step);
            }
            int picked=0;
            for(int attempt=0;attempt<24;attempt++){
                await Frames(15);var actions=Authority().GetProperty("Actions").EnumerateArray().ToArray();
                if(ChoiceKind()=="Reward"){Check(ChoiceMatches(),"divination native card choices match selected source");await Remote("select");picked++;await Wait(()=>ChoiceKind()!="Reward"&&ChildCrystal().GetProperty("rewards").GetRawText()==ReadPath(Path.Combine(Root,SourceRole+"-crystal.json")).GetProperty("rewards").GetRawText(),"claimed native reward row removed and reflowed");Check(ChildCrystal().GetProperty("rewards").GetRawText()==ReadPath(Path.Combine(Root,SourceRole+"-crystal.json")).GetProperty("rewards").GetRawText(),"claimed divination reward rows match source after native removal");continue;}
                var cardReward=ChildCrystal().GetProperty("rewards").EnumerateArray().FirstOrDefault(r=>r.GetProperty("type").GetString()=="CardReward");
                if(cardReward.ValueKind==JsonValueKind.Object){await Remote("native","="+cardReward.GetProperty("path").GetString());await Wait(()=>ChoiceKind()=="Reward"&&ChoiceMatches(),"actual divination card reward chooser");continue;}
                var otherReward=ChildCrystal().GetProperty("rewards").EnumerateArray().FirstOrDefault(r=>r.GetProperty("type").GetString()!="PotionReward");
                if(otherReward.ValueKind==JsonValueKind.Object){await Remote("native","="+otherReward.GetProperty("path").GetString());continue;}
                if(actions.Any(a=>a.GetProperty("NativePath").GetString()!.Contains("NProceedButton"))){await Remote("native","NProceedButton");await Wait(()=>Child().GetProperty("overlay").GetString()!="NRewardsScreen","native divination rewards close to finished event");break;}
                await Pace(500);
            }
            await Sync(state,tag+"-rewards");Check(picked>0,"real divination card reward was selected "+count);Check(RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,"original divination event completes "+count);
            await Remote("native","NProceedButton");await Wait(()=>Child().GetProperty("map").GetBoolean(),"finished divination screen native Continue opens map");
        }
        await Enter(RoomType.Event,"lantern-return",ModelDb.Event<TheLanternKey>());await Sync(state,"lantern-return-entry");await Grant("lantern-return");int gold=LocalContext.GetMe(state)!.Gold;
        await Stage("多人灯火钥匙专项 · 原版全队投票归还钥匙");if(Id==1)await Remote("native","NEventOptionButton",0);await Barrier("lantern-one-vote");await Frames(15);Check(!RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,"one Lantern Key vote waits for full party");
        if(Id==2)await Remote("native","NEventOptionButton",0);await Barrier("lantern-two-votes");await Frames(15);Check(!RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,"two Lantern Key votes wait for third player");if(Id==3)await Remote("native","NEventOptionButton",0);
        await Wait(()=>RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,"native shared return outcome");await Sync(state,"lantern-returned");Check(LocalContext.GetMe(state)!.Gold==gold+100,"original return rewards each player exactly 100 gold");
        await Enter(RoomType.Event,"lantern-keep",ModelDb.Event<TheLanternKey>());await Sync(state,"lantern-keep-entry");await Grant("lantern-keep");await Remote("native","NEventOptionButton",1);await Sync(state,"lantern-keep-choice");await Remote("native","NEventOptionButton",0);
        await Wait(()=>CombatManager.Instance.IsInProgress&&(bool)Get(Proc,"DisplayReady")!&&Child().GetProperty("room").GetString()=="Monster","native Lantern Key encounter");await Barrier("lantern-combat");Check(state.Players.Count==3&&Child().GetProperty("players").GetArrayLength()==3,"cooperative event combat preserves whole party");
        await Sync(state,"lantern-combat-state");await GrantBattle("lantern-combat-control");await RemoteBattle("play","DefendIronclad");await Sync(state,"lantern-combat-defend");
        long combatGeneration=(long)Get(Mirror,"LocalGeneration")!;((Button)Get(View!,"_refresh")!).EmitSignal(Button.SignalName.Pressed);await Wait(()=>(long)Get(Mirror,"LocalGeneration")!>combatGeneration&&(bool)Get(Proc,"DisplayReady")!&&(bool)Get(Mirror,"Verified")!,"nested event combat clean replay");await Sync(state,"lantern-combat-reset");Check(Pid==pid,"nested event fight and clean reset preserve resident native process");
        await CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState().Enemies.ToArray(),true);await CombatManager.Instance.CheckWinCondition();await Barrier("lantern-combat-ended");await Wait(()=>!CombatManager.Instance.IsInProgress&&NOverlayStack.Instance.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen&&Child().GetProperty("overlay").GetString()=="NRewardsScreen"&&(bool)Get(Mirror,"Verified")!,"native event rewards checkpoint");await Sync(state,"lantern-rewards-ready");await Grant("lantern-key-reward");
        await Wait(()=>ChildCrystal().GetProperty("rewards").EnumerateArray().Any(r=>r.GetProperty("type").GetString()=="SpecialCardReward"),"native Lantern Key reward button");
        await Remote("native","="+ChildCrystal().GetProperty("rewards").EnumerateArray().First(r=>r.GetProperty("type").GetString()=="SpecialCardReward").GetProperty("path").GetString());
        await Wait(()=>LocalContext.GetMe(state)!.Deck.Cards.Count(c=>c.GetType().Name=="LanternKey")==1,"one original Lantern Key card per player");await Sync(state,"lantern-key-obtained");await Remote("native","NProceedButton");await Wait(()=>Child().GetProperty("map").GetBoolean(),"native cooperative rewards proceed");
        Check(state.Players.All(p=>p.Deck.Cards.Count(c=>c.GetType().Name=="LanternKey")==1),"all three players receive exactly one original Lantern Key");Check(Pid==pid,"both custom event branches retain exact native replica PID");Write(Role+"-result.json",new{passed=true,checks,visible=System.Environment.GetEnvironmentVariable("RMP_VISIBLE_SILENT")!="1",actualRendering=true,scope="independent three-player Crystal Sphere and cooperative Lantern Key branches"});await Barrier("special-events-done");
    }
}
