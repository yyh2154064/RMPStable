using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Env = System.Environment;

// Only v0.4.4 selection scenarios run here. Startup/input/evidence helpers are shared.
public static partial class MultiplayerVisibleProbe
{
    static void InstallSelectionFixture()
    {
        // Force one original ancient reward for reproducible scenarios on every
        // native peer/replica. Keep the original option delegate and all effects.
        new Harmony("RMP.Selection044.Fixture").Patch(typeof(AncientEventModel).GetMethod("GenerateInitialOptionsWrapper",Any)!,
            postfix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(AncientFixture),Any)!));
    }
    static void AncientFixture(AncientEventModel __instance,ref IReadOnlyList<EventOption> __result)
    {
        var desired=ReadPath(Path.Combine(Shared,"044-ancient.json"));
        if(desired.ValueKind!=JsonValueKind.Object)return;
        string relic=desired.GetProperty("relic").GetString()!;
        var option=__instance.AllPossibleOptions.FirstOrDefault(o=>o.Relic?.GetType().Name==relic);
        if(option==null)return;
        var options=new List<EventOption>{option};
        typeof(AncientEventModel).GetField("_generatedOptions",Any)!.SetValue(__instance,options);__result=options;
    }
    static void PrepareSelections(RunState state,Player[] players)
    {
        foreach(var p in players)
        {
            p.Deck.Clear(true);
            foreach(var card in new CardModel[]{state.CreateCard<Wish>(p),state.CreateCard<Wish>(p),state.CreateCard<Begone>(p),state.CreateCard<Charge>(p),state.CreateCard<Hologram>(p),state.CreateCard<Prepared>(p),state.CreateCard<DefendIronclad>(p)})
            {CardCmd.Enchant<MegaCrit.Sts2.Core.Models.Enchantments.RoyallyApproved>(card,1);p.Deck.AddInternal(card,silent:true);}
            for(int i=0;i<10;i++) foreach(var card in new CardModel[]{state.CreateCard<StrikeIronclad>(p),state.CreateCard<DefendIronclad>(p),state.CreateCard<DemonForm>(p),state.CreateCard<Offering>(p),state.CreateCard<Wish>(p)})p.Deck.AddInternal(card,silent:true);
            for(int i=0;i<10;i++)p.AddRelicInternal(ModelDb.Relic<Lantern>().ToMutable(),silent:true);
            for(int i=0;i<2;i++)p.AddRelicInternal(ModelDb.Relic<BagOfPreparation>().ToMutable(),silent:true);
        }
    }
    static async Task ShellClick044()
    {
        var button=(Button)Get(View!,"_controlToggle")!;var point=button.GetGlobalTransformWithCanvas()*(button.Size/2);var viewport=button.GetViewport();
        viewport.PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point},true);await Frames(8);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point},true);await Frames(8);
    }
    static async Task SetMode044(bool control)
    {
        if((bool)Get(Get(null,"Current",Rmp("SpectatorPreferences")),"MultiplayerControlMode")! !=control)await ShellClick044();
        await Wait(()=>Child().GetProperty("control").GetBoolean()==control,"044 requested mode reaches source-validated replica");
    }
    static async Task DockModes044()
    {
        int pid=Pid;var pin=(Button)Get(View!,"_pin")!;
        var pinnedField=View!.GetType().GetField("_pinned",Any)!;
        foreach(bool mode in new[]{true,false,true})
        {
            // No OS cursor is moved: hold the shell expanded during its own
            // synthetic button click, then exercise the real dock geometry.
            pinnedField.SetValue(View,true);Call(View!,"SlidePanel",false,true);
            await SetMode044(mode);pinnedField.SetValue(View,false);Call(View!,"SlidePanel",true,true);
            ulong until=Time.GetTicksMsec()+(mode?18000UL:1000UL);
            while(Time.GetTicksMsec()<until)await Frames(); // Wall time, independent of rendering FPS.
            Check(Get(View!,"_dockHidden") is true,"044 panel reaches genuinely collapsed state");
            pinnedField.SetValue(View,true);Call(View!,"SlidePanel",false,true);await Frames(2);
            Check((bool)Get(Get(null,"Current",Rmp("SpectatorPreferences")),"MultiplayerControlMode")! ==mode,"044 player mode preference survives collapse/expand");
            Check(Child().GetProperty("control").GetBoolean()==mode&&Pid==pid,"044 collapse/expand preserves native permission and resident process");
            Check(((Control)Get(View!,"_modeThumb")!).Position.X==(mode?58:2),"044 visible mode thumb retains player selection");
        }
        pin.SetPressedNoSignal(true);View.GetType().GetField("_pinned",Any)!.SetValue(View,true);Call(View,"SlidePanel",false,true);
    }
    static async Task Selector044(string kind,string tag,bool cancel=false)
    {
        await Wait(()=>ChoiceKind()==kind&&ChoiceMatches(),tag+" native selector appears with exact source contract");
        Check(Personal().GetProperty("Cards").GetArrayLength()>0&&ChoiceMatches(),tag+" complete candidates/filter/count match source");
        await SetMode044(false);await ContractReadonly(kind,tag+"-readonly");
        Check(Personal().GetProperty("Selected").GetArrayLength()==0&&!Personal().GetProperty("Preview").GetBoolean(),tag+" watch input cannot mutate selection or preview");
        await SetMode044(true);
        int count=Personal().GetProperty("Min").GetInt32();
        if(kind=="Bundle")
        {
            Check((await InteractionInput("nativeEyeHidden")).GetProperty("success").GetBoolean(),tag+" original noncombat bundle eye remains disabled and hidden");
            await Remote("native","NCardBundle#0");await Wait(()=>Personal().GetProperty("Preview").GetBoolean(),tag+" bundle preview");
            await Remote("native","NBackButton");await Wait(()=>!Personal().GetProperty("Preview").GetBoolean(),tag+" bundle cancel");
            await Remote("native","NCardBundle#1");await Wait(()=>Personal().GetProperty("Preview").GetBoolean(),tag+" second bundle preview");
            Check(Personal().GetProperty("Selected")[0].GetInt32()==1,tag+" second original bundle is selected");
            await Remote("native","NConfirmButton");await Wait(()=>ChoiceKind()=="",tag+" bundle result completes");return;
        }
        if(count==0)count=Math.Min(2,Personal().GetProperty("Max").GetInt32());
        for(int round=0;round<(cancel?2:1);round++)
        {
            for(int i=0;i<count;i++)Check((await InteractionInput("selectNext")).GetProperty("success").GetBoolean(),tag+" authoritative native selection "+round+"/"+i);
            await Wait(()=>Personal().ValueKind==JsonValueKind.Object&&Personal().GetProperty("Selected").GetArrayLength()==count,tag+" source selection metadata applied");
            if(!Personal().GetProperty("Preview").GetBoolean())await Remote("native","Confirm");
            await Wait(()=>Personal().ValueKind==JsonValueKind.Object&&Personal().GetProperty("Preview").GetBoolean(),tag+" source preview mirrored");
            Check(Personal().GetProperty("Preview").GetBoolean(),tag+" original preview visible");
            if(round==0&&cancel)
            {
                await Remote("native","NBackButton");await Wait(()=>Personal().ValueKind==JsonValueKind.Object&&!Personal().GetProperty("Preview").GetBoolean()&&Personal().GetProperty("Selected").GetArrayLength()==0,tag+" cancel clears preview and original selection");
                Check(ChoiceMatches(),tag+" cancel keeps live selection task and candidates");
            }
        }
        await Remote("native","Confirm");await Wait(()=>ChoiceKind()=="",tag+" original result finishes selector");
    }
    static async Task Event044(RunState state,EventModel model,string tag,int option,string kind="DeckEnchant",bool cancel=false,string expected="",int delta=0)
    {
        await Enter(RoomType.Event,tag,model);await Sync(state,tag+"-entry");await SetMode044(true);int before=state.GetPlayer(SourceId).Deck.Cards.Count;
        await Remote("native","=NEventOptionButton#"+option);
        await Selector044(kind,tag,cancel);
        await Wait(()=>RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,tag+" original event finishes");await Sync(state,tag+"-finished");
        Check(state.GetPlayer(SourceId).Deck.Cards.Count==before+delta,tag+" exact original deck count change");
        if(expected.Length>0)Check(state.GetPlayer(SourceId).Deck.Cards.Any(c=>c.GetType().Name==expected||c.Enchantment?.GetType().Name==expected),tag+" requested original card/enchantment exists");
        Check((await InteractionInput("captureIdle")).GetProperty("success").GetBoolean(),tag+" no stale command captures remain");
    }
    static async Task Combat044(RunState state)
    {
        await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
        await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning,"044 combat fixture ready");await Sync(state,"044-combat");await SetMode044(true);
        await DockModes044();
        await RemoteBattle("play","Begone");await Wait(()=>ChoiceKind()=="Hand"&&ChoiceMatches(),"Begone hand transform selector");Check(ChoiceMatches(),"Begone original hand selection contract");await RemoteBattle("select","DEFEND_IRONCLAD");await RemoteBattle("choice");await Wait(()=>ChoiceKind()==""&&!RunManager.Instance.ActionExecutor.IsRunning,"Begone completed");await Sync(state,"044-begone");Check(state.GetPlayer(SourceId).PlayerCombatState!.Hand.Cards.Any(c=>c is MinionStrike),"Begone produces original MinionStrike");
        await RemoteBattle("play","Charge");await Wait(()=>ChoiceKind()=="Pile"&&ChoiceMatches(),"Charge draw-pile transform selector");Check(ChoiceMatches(),"Charge original two-card contract");for(int i=0;i<2;i++)Check((await InteractionInput("selectNext")).GetProperty("success").GetBoolean(),"Charge selects distinct draw card "+i);await Wait(()=>ChoiceKind()==""&&!RunManager.Instance.ActionExecutor.IsRunning,"Charge completed");await Sync(state,"044-charge");Check(state.GetPlayer(SourceId).PlayerCombatState!.DrawPile.Cards.Count(c=>c is MinionDiveBomb)==2,"Charge transforms exactly two original draw cards");
        await RemoteBattle("play","Wish");await Wait(()=>ChoiceKind()=="Pile"&&ChoiceMatches(),"Wish original draw selection");Check((await InteractionInput("selectScrolled")).GetProperty("success").GetBoolean(),"Wish scrolls independently and selects deep draw card");await Wait(()=>ChoiceKind()==""&&!RunManager.Instance.ActionExecutor.IsRunning,"Wish completed");await Sync(state,"044-wish");
        await RemoteBattle("play","Prepared");await Wait(()=>ChoiceKind()=="Hand"&&ChoiceMatches(),"Prepared draw/discard selector");Check(ChoiceMatches(),"Prepared original draw/discard contract");await RemoteBattle("select","MINION_STRIKE");await RemoteBattle("choice");await Wait(()=>ChoiceKind()==""&&!RunManager.Instance.ActionExecutor.IsRunning,"Prepared completed");await Sync(state,"044-prepared");
        // End this fixture through vanilla cleanup before entering event cases.
        await CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState().Enemies.ToArray(),true);await CombatManager.Instance.CheckWinCondition();await Barrier("044-combat-cleanup");await Wait(()=>!CombatManager.Instance.IsInProgress,"044 fixture combat ended");
    }
    static async Task TestSelections(RunState state)
    {
        var supported=new HashSet<string>{"FromChooseACardScreen","FromSimpleGridForRewards","FromSimpleGrid","FromCombatPile","FromDeckForUpgrade","FromDeckForTransformation","FromDeckForEnchantment","FromDeckForRemoval","FromDeckGeneric","FromHand","FromHandForDiscard","FromHandForUpgrade","FromChooseABundleScreen"};
        Check(typeof(CardSelectCmd).GetMethods(Any).Where(m=>m.Name.StartsWith("From",StringComparison.Ordinal)).All(m=>supported.Contains(m.Name)),"044 original selector API inventory has no unreviewed entry points");
        string part=Env.GetEnvironmentVariable("RMP_044_PART")??"all";
        if(part is "all" or "combat")await Combat044(state);
        if(part is "all" or "events")
        {
            await Event044(state,ModelDb.Event<GraveOfTheForgotten>(),"044-grave",0,cancel:true,expected:"SoulsPower",delta:1);
            await Event044(state,ModelDb.Event<FieldOfManSizedHoles>(),"044-holes",1,expected:"PerfectFit");
            await Event044(state,ModelDb.Event<SapphireSeed>(),"044-seed",1,expected:"Sown");
            for(int i=0;i<3;i++)await Event044(state,ModelDb.Event<SelfHelpBook>(),"044-book-"+i,i,expected:new[]{"Sharp","Nimble","Swift"}[i]);
            await Event044(state,ModelDb.Event<SpiralingWhirlpool>(),"044-spiral",0,expected:"Spiral");
            await Event044(state,ModelDb.Event<StoneOfAllTime>(),"044-stone",1,expected:"Vigorous");
            await Event044(state,ModelDb.Event<Symbiote>(),"044-symbiote",0,expected:"Corrupted");
            for(int i=1;i<=2;i++)await Event044(state,ModelDb.Event<WaterloggedScriptorium>(),"044-scriptorium-"+i,i,cancel:i==2,expected:"Steady");
            await Event044(state,ModelDb.Event<WoodCarvings>(),"044-wood",1,expected:"Slither");
        }
        if(part is "all" or "events" or "extras")
        {
            await Event044(state,ModelDb.Event<Symbiote>(),"044-transform",1,"DeckTransform",true);
            await Event044(state,ModelDb.Event<Amalgamator>(),"044-fuse-strikes",0,"Deck",true,"UltimateStrike",-1);
            await Event044(state,ModelDb.Event<Amalgamator>(),"044-fuse-defends",1,"Deck",true,"UltimateDefend",-1);
        }
        if(part is "all" or "ancients" or "extras" or "cardpack")await Ancients044(state);
        Write(Role+"-result.json",new{passed=true,checks,scope="v0.4.4 selection and remembered mode",part});await Barrier("044-done");
    }
    static async Task ObserveReadonly044(object renderer,RunState state)
    {
        var request=ReadPath(Path.Combine(Root,"visible-readonly.json"));
        if(request.ValueKind==JsonValueKind.Object&&request.GetProperty("seq").GetInt32()!=readonlyHandled&&NOverlayStack.Instance.Peek() is NChooseABundleSelectionScreen bundle)
        {
            if(Get(bundle,"_bundleRow") is Node row&&row.GetChildren().OfType<NCardBundle>().FirstOrDefault() is { } first)first.Hitbox.ForceClick();
            if(Get(bundle,"_previewConfirmButton") is NClickableControl confirm)confirm.ForceClick();
        }
        await ObserveReadonly(renderer,state);
    }
    static async Task Ancients044(RunState state)
    {
        var scenarios=new (AncientEventModel Ancient,string Relic,string Kind,string Expected)[]{
            (ModelDb.AncientEvent<Nonupeipe>(),"BeautifulBracelet","AutoEnchant","Swift"),
            (ModelDb.AncientEvent<Orobas>(),"ElectricShrymp","DeckEnchant","Imbued"),
            (ModelDb.AncientEvent<Pael>(),"PaelsGrowth","DeckEnchant","Clone"),
            (ModelDb.AncientEvent<Tanx>(),"TriBoomerang","DeckEnchant","Instinct"),
            (ModelDb.AncientEvent<Tanx>(),"Claws","DeckTransform","Maul"),
            (ModelDb.AncientEvent<Darv>(),"PandorasBox","","Pandora"),
            (ModelDb.AncientEvent<Neow>(),"ScrollBoxes","Bundle","")};
        foreach(var spec in scenarios)
        {
            if(Env.GetEnvironmentVariable("RMP_044_PART")=="cardpack"&&spec.Relic!="ScrollBoxes")continue;
            string tag="044-ancient-"+spec.Relic;
            if(Host)Write("044-ancient.json",new{relic=spec.Relic});await Barrier(tag+"-fixture");
            await Enter(RoomType.Event,tag,spec.Ancient);await Sync(state,tag+"-entry");await SetMode044(true);
            Check(RunManager.Instance.EventSynchronizer.GetLocalEvent().CurrentOptions.Any(o=>o.Relic?.GetType().Name==spec.Relic),tag+" original ancient reward available");
            int previousEnchanted=state.GetPlayer(SourceId).Deck.Cards.Count(c=>c.Enchantment?.GetType().Name==spec.Expected);
            if(spec.Kind=="AutoEnchant"||spec.Relic=="PandorasBox")
            {
                // These vanilla effects consume shared RunRng.Niche. Exercise
                // one original choice at a time so private-event callback
                // scheduling does not reorder that RNG across native peers.
                foreach(ulong actor in new[]{1UL,2UL,3UL})
                {
                    if(Id==actor)await Remote("native","NEventOptionButton",0);
                    await Barrier(tag+"-actor-"+actor);
                    ulong owner=actor%3+1;
                    await Wait(()=>RunManager.Instance.EventSynchronizer.Events.Any(e=>e.Owner?.NetId==owner&&e.IsFinished),tag+" original actor effect complete "+actor);
                    await Barrier(tag+"-actor-finished-"+actor);
                }
            }
            else await Remote("native","NEventOptionButton",0);
            if(spec.Kind=="AutoEnchant")
            {
                await Wait(()=>RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,tag+" original automatic enchantment completes");
                var relic=state.GetPlayer(SourceId).Relics.First(r=>r.GetType().Name==spec.Relic);
                int amount=relic.DynamicVars["Swift"].IntValue;
                Check(state.GetPlayer(SourceId).Deck.Cards.Count(c=>c.Enchantment?.GetType().Name==spec.Expected)==previousEnchanted+relic.DynamicVars.Cards.IntValue,tag+" original automatic enchant count matches current game version");
                Check(state.GetPlayer(SourceId).Deck.Cards.Where(c=>c.Enchantment?.GetType().Name==spec.Expected).All(c=>c.Enchantment!.Amount==amount),tag+" original automatic enchant amount matches current game version");
                Check(ChoiceKind()=="",tag+" original automatic effect needs no selection UI");
            }
            else if(spec.Kind.Length>0)await Selector044(spec.Kind,tag,true);
            else
            {
                await Wait(()=>NCapstoneContainer.Instance.CurrentCapstoneScreen?.GetType().Name=="NSimpleCardsViewScreen"&&Child().GetProperty("capstone").GetString()=="NSimpleCardsViewScreen",tag+" actual original result cards view");
                Check(Child().GetProperty("capstone").GetString()=="NSimpleCardsViewScreen",tag+" native result view is visible");
                Check((await InteractionInput("resultClose")).GetProperty("success").GetBoolean(),tag+" native result confirmation closes source and replica");
                await Wait(()=>NCapstoneContainer.Instance.CurrentCapstoneScreen==null,tag+" source result closes");
                Check(state.GetPlayer(SourceId).Deck.Cards.All(c=>!c.IsBasicStrikeOrDefend||!c.IsRemovable),tag+" all eligible starter cards actually transformed");
            }
            await Wait(()=>RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,tag+" native ancient completes");await Sync(state,tag+"-result");
            Check(state.GetPlayer(SourceId).Relics.Any(r=>r.GetType().Name==spec.Relic),tag+" original relic obtained");
            if(spec.Expected.Length>0&&spec.Expected!="Pandora")Check(state.GetPlayer(SourceId).Deck.Cards.Any(c=>c.GetType().Name==spec.Expected||c.Enchantment?.GetType().Name==spec.Expected),tag+" expected original transformation/enchantment");
        }
    }
}
