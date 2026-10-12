using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static void PrepareReturnFixture(RunState state)
    {
        foreach(var p in state.Players){p.Deck.Clear(true);for(int i=0;i<14;i++)p.Deck.AddInternal(state.CreateCard<Shiv>(p),silent:true);for(int i=0;i<10;i++)p.Deck.AddInternal(state.CreateCard<DefendIronclad>(p),silent:true);}
    }
    static async Task TestReturnExtras(RunState state)
    {
        int pid=Pid;await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
        var me=LocalContext.GetMe(state)!;await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState!.Hand.Cards.OfType<Shiv>().Any()&&me.PlayerCombatState.Hand.Cards.OfType<DefendIronclad>().Any(),"native pile fixture ready");await Barrier("return-pile-before-play");
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState!.Hand.Cards.OfType<Shiv>().First(),CombatManager.Instance.DebugOnlyGetState().Enemies.OrderByDescending(c=>c.CurrentHp).First()));
        await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState.ExhaustPile.Cards.Count>0,"native exhaust pile populated");await Barrier("return-pile-exhaust");
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState.Hand.Cards.OfType<DefendIronclad>().First(),null));await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState.DiscardPile.Cards.Count>0,"native discard pile populated");await Sync(state,"return-piles-ready");
        await Stage("后台返回专项 · 抽牌/弃牌/消耗牌堆、卡牌详情与设置各页");
        foreach(string kind in new[]{"drawPile","discardPile","exhaustPile"}){
            var o=await WindowClick(kind);Check(o.GetProperty("capstone").GetString()=="NCardPileScreen","native "+kind+" opens actual pile screen");
            o=await WindowClick("back");Check(o.GetProperty("capstone").ValueKind==JsonValueKind.Null,"native "+kind+" return closes pile");
        }
        await WindowClick("deck");var inspected=await WindowClick("inspectCard");Check(inspected.GetProperty("inspectCard").GetBoolean(),"native deck card click opens inspection");inspected=await WindowClick("inspectCardBack");Check(!inspected.GetProperty("inspectCard").GetBoolean(),"native card inspection return closes");await WindowClick("back");
        for(int tab=0;tab<4;tab++){
            await WindowClick("pause");await WindowClick("settings");var o=await WindowClick("settingsTab",(ulong)tab);
            Check(o.GetProperty("selectedTab").ValueKind==JsonValueKind.Number&&o.GetProperty("selectedTab").GetInt32()==tab,"native settings tab "+tab+" opens");
            o=await WindowClick("settingsBack");Check(!o.GetProperty("settings").GetBoolean(),"settings tab "+tab+" return reaches pause");o=await WindowClick("resume");Check(o.GetProperty("capstone").ValueKind==JsonValueKind.Null,"settings tab "+tab+" resume reaches fight");
        }
        await Sync(state,"extra-returns-readonly");Check(Pid==pid&&!Child().GetProperty("control").GetBoolean(),"extra returns preserve readonly source state and resident PID");Write(Role+"-result.json",new{passed=true,checks,visible=false,actualRendering=true,scope="independent native three populated battle pile returns/card inspector and four settings tab returns"});await Barrier("extra-returns-done");
    }
}
