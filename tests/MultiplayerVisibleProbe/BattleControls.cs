using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Runs;
public static partial class MultiplayerVisibleProbe {
static async Task BattleControls(RunState state,Player[] players,Player me) {
        var old=Authority().Clone();
int accepted=(int)Session("AcceptedControlRequests")!,beforeReject=(int)Session("RejectedControlRequests")!;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState!.Hand.Cards.OfType<DefendIronclad>().First(),null));await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning,"own native input");await Barrier("own-input");
            var stale=JsonSerializer.SerializeToElement(new{Epoch=old.GetProperty("Epoch").GetInt64(),Request=999,Generation=Get(Mirror,"LocalGeneration"),Events=0,NodeKey=old.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()=="play").GetProperty("Id").GetString(),Value=old.GetProperty("Context").GetString()});Forward(stale);await Barrier("stale-input");await Wait(()=>(int)Session("RejectedControlRequests")!>beforeReject,"stale private context rejected");Check((int)Session("AcceptedControlRequests")! ==accepted,"simultaneous own input invalidates stale controller request");
            await Wait(()=>Authority().GetProperty("Context").GetString()!=old.GetProperty("Context").GetString(),"fresh own state metadata");var fresh=Authority();beforeReject=(int)Session("RejectedControlRequests")!;
            await Barrier("future-baseline");
            var future=JsonSerializer.SerializeToElement(new{Epoch=fresh.GetProperty("Epoch").GetInt64(),Request=1000,Generation=Get(Mirror,"LocalGeneration"),Events=int.MaxValue,NodeKey=fresh.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()=="play").GetProperty("Id").GetString(),Value=fresh.GetProperty("Context").GetString()});Forward(future);await Barrier("future-events");await Wait(()=>(int)Session("RejectedControlRequests")!>beforeReject,"future event prefix rejected");Check((int)Session("AcceptedControlRequests")! ==accepted,"future event prefix cannot execute a current otherwise legal card");
            Static("MultiplayerMirrorSession","RevokeControl");await Barrier("revoke");await Frames(20);Check(!Child().GetProperty("control").GetBoolean(),"source revocation disables renderer authority");Forward(stale);await Barrier("old-epoch");await Frames(10);Check((int)Session("AcceptedControlRequests")! ==accepted,"revoked epoch cannot execute delayed request");
            await GrantBattle("regrant");await RemoteBattle("potion","FocusPotion");await Sync(state,"self-potion");Check(!me.Potions.OfType<FocusPotion>().Any(),"untargeted potion executed for selected source");
            await TestPotionTargets(state);
            var last=await RemoteBattle("potion","FirePotion",target:-2);await Sync(state,"target-potion");Check(!me.Potions.OfType<FirePotion>().Any(),"targeted potion original action consumes correct source slot");accepted=(int)Session("AcceptedControlRequests")!;Forward(last);Forward(last);await Barrier("duplicates");await Frames(20);Check((int)Session("AcceptedControlRequests")! ==accepted,"duplicate reliable request returns cached result without repeating action");
            await RemoteBattle("play","Prepared");await SelectVisible("Hand",2,"DEFEND","prepared-select");await Sync(state,"prepared-result");
            await RemoteBattle("play","TrueGrit");await SelectVisible("Hand",1,"DEFEND","grit-select");await Sync(state,"grit-result");
            await RemoteBattle("play","Armaments");await SelectVisible("Upgrade",1,"","upgrade-select");await Sync(state,"upgrade-result");
            await RemoteBattle("play","Headbutt",target:-2);await SelectVisible("Pile",1,"","pile-select");await Sync(state,"pile-result");
            await RemoteBattle("play","Discovery");await SelectVisible("Choose",1,"","choose-select");await Sync(state,"choose-result");
            await RemoteBattle("endTurn");await Wait(()=>players.All(p=>p.PlayerCombatState!.TurnNumber>=2),"all three controllers ended original turn");await Sync(state,"end-turn-result");Check(me.PlayerCombatState!.TurnNumber==2,"remote end-turn follows original all-player advancement");

}}
