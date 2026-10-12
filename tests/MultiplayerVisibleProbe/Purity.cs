using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

public static partial class MultiplayerVisibleProbe
{
    static void PreparePurityFixture(RunState state)
    {
        foreach(var p in state.Players){p.Deck.Clear(true);for(int i=0;i<2;i++)p.Deck.AddInternal(state.CreateCard<Purity>(p),silent:true);for(int i=0;i<8;i++)p.Deck.AddInternal(state.CreateCard<DefendIronclad>(p),silent:true);}
    }
    static async Task TestPurity(RunState state)
    {
        int pid=Pid;await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
        var me=LocalContext.GetMe(state)!;await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState!.Hand.Cards.OfType<Purity>().Count()==2,"native Purity fixture ready");await Sync(state,"purity-ready");await GrantBattle("purity-first");
        int initial=me.PlayerCombatState!.Hand.Cards.Count;await RemoteBattle("play","Purity");await Wait(()=>ChoiceKind()=="Hand"&&ChoiceMatches(),"native Purity hand selection");Check(Personal().GetProperty("Min").GetInt32()==0&&Personal().GetProperty("Max").GetInt32()==3,"Purity original zero to three card contract");
        await Stage("后台洁净专项 · 等待选牌时重置、消耗三张与零张分支");long generation=(long)Get(Mirror,"LocalGeneration")!;((Button)Get(View!,"_refresh")!).EmitSignal(Button.SignalName.Pressed);
        await Wait(()=>(long)Get(Mirror,"LocalGeneration")!>generation&&(bool)Get(Proc,"DisplayReady")!&&ChoiceKind()=="Hand"&&ChoiceMatches(),"native Purity waiting choice restored");Check(Pid==pid&&me.PlayerCombatState.ExhaustPile.Cards.Count==0,"Purity reset preserves PID and leaves original choice uncompleted");await Barrier("purity-waiting-reset");await ContractReadonly("Hand","purity-readonly");await GrantBattle("purity-restored");
        for(int i=0;i<3;i++)await RemoteBattle("select","DEFEND");
        if(ChoiceKind()=="Hand")await RemoteBattle("choice");await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState.ExhaustPile.Cards.Count==4,"original Purity exhaust completes");await Sync(state,"purity-three-exhaust");Check(me.PlayerCombatState.ExhaustPile.Cards.OfType<DefendIronclad>().Count()==3&&me.PlayerCombatState.ExhaustPile.Cards.OfType<Purity>().Count()==1&&me.PlayerCombatState.Hand.Cards.Count==initial-4,"Purity exhausts exactly three selected cards and itself once");
        generation=(long)Get(Mirror,"LocalGeneration")!;((Button)Get(View!,"_refresh")!).EmitSignal(Button.SignalName.Pressed);await Wait(()=>(long)Get(Mirror,"LocalGeneration")!>generation&&(bool)Get(Proc,"DisplayReady")!&&(bool)Get(Mirror,"Verified")!,"native completed Purity replay restored");await Sync(state,"purity-completed-reset");Check(Pid==pid&&me.PlayerCombatState.ExhaustPile.Cards.Count==4,"completed Purity replay keeps exact source state and resident PID");await GrantBattle("purity-zero");
        int hand=me.PlayerCombatState.Hand.Cards.Count;await RemoteBattle("play","Purity");await Wait(()=>ChoiceKind()=="Hand"&&ChoiceMatches(),"second native Purity optional selection");await RemoteBattle("choice");await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState.ExhaustPile.Cards.Count==5,"zero card Purity completes");await Sync(state,"purity-zero-result");Check(me.PlayerCombatState.ExhaustPile.Cards.OfType<DefendIronclad>().Count()==3&&me.PlayerCombatState.ExhaustPile.Cards.OfType<Purity>().Count()==2&&me.PlayerCombatState.Hand.Cards.Count==hand-1,"zero card Purity exhausts only itself without repeating prior selections");Check(Pid==pid,"both Purity choices and resets retain one original native process");
        Write(Role+"-result.json",new{passed=true,checks,visible=false,actualRendering=true,scope="independent three-player original Purity zero/three-card exhaust and clean replay while waiting/after completion"});await Barrier("purity-done");
    }
}
