using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

// New feedback only: real pile choices, local eye/settings and expanded mode toggle.
public static partial class MultiplayerVisibleProbe
{
    static void PrepareInteractions(RunState state,Player[] players)
    {
        foreach(var p in players)
        {
            p.Deck.Clear(true);
            foreach(var starter in new CardModel[]{state.CreateCard<Wish>(p),state.CreateCard<Wish>(p),state.CreateCard<Wish>(p),state.CreateCard<DefendIronclad>(p),state.CreateCard<DefendIronclad>(p),state.CreateCard<SecretTechnique>(p),state.CreateCard<SecretWeapon>(p),state.CreateCard<Charge>(p),state.CreateCard<Hologram>(p),state.CreateCard<Dredge>(p)})
            {
                MegaCrit.Sts2.Core.Commands.CardCmd.Enchant<MegaCrit.Sts2.Core.Models.Enchantments.RoyallyApproved>(starter,1);
                p.Deck.AddInternal(starter,silent:true);
            }
            for(int i=0;i<30;i++)p.Deck.AddInternal(state.CreateCard<Wish>(p),silent:true);
            for(int i=0;i<30;i++)p.Deck.AddInternal(state.CreateCard<DefendIronclad>(p),silent:true);
            foreach(var card in new CardModel[]{state.CreateCard<Hologram>(p),state.CreateCard<Seance>(p),state.CreateCard<NeowsFury>(p),state.CreateCard<Headbutt>(p),state.CreateCard<Graveblast>(p),state.CreateCard<CosmicIndifference>(p)})p.Deck.AddInternal(card,silent:true);
            // Fixture-only energy supply; all original hooks run normally.
            for(int i=0;i<14;i++)p.AddRelicInternal(ModelDb.Relic<Lantern>().ToMutable(),silent:true);
            for(int i=0;i<3;i++)p.AddRelicInternal(ModelDb.Relic<BagOfPreparation>().ToMutable(),silent:true);
            p.AddPotionInternal(ModelDb.Potion<LiquidMemories>().ToMutable(),silent:true);
            p.AddPotionInternal(ModelDb.Potion<DropletOfPrecognition>().ToMutable(),silent:true);
        }
    }
    static int interactionHandled;
    static async Task ObserveInteractions(object renderer,RunState state)
    {
        var overlay=NOverlayStack.Instance?.Peek() as Node;
        var grid=overlay==null?null:Get(overlay,"_grid") as NCardGrid;
        WritePath(Path.Combine(Root,"interaction-state.json"),new{choice=Static("MultiplayerPersonalUi","Capture"),
            overlay=overlay?.GetType().Name,
            holders=overlay==null?null:Descendants<NCardHolder>(overlay).Select(h=>new{path=Static("MirrorNativeUi","Key",h),card=h.CardModel?.Id.ToString(),visible=h.IsVisibleInTree(),enabled=h.Hitbox.IsEnabled,clickable=Get(h,"_isClickable"),rect=h.Hitbox.GetGlobalRect().ToString()}),
            allCards=grid==null?null:((System.Collections.IEnumerable)Get(grid,"_cards")!).Cast<CardModel>().Select(c=>c.Id.ToString()),
            visibleCards=grid?.CurrentlyDisplayedCards.Select(c=>c.Id.ToString()),settings=SettingsNode()!=null});
        var input=ReadPath(Path.Combine(Root,"interaction-input.json"));
        if(input.ValueKind!=JsonValueKind.Object||input.GetProperty("seq").GetInt32()==interactionHandled)return;
        string kind=input.GetProperty("kind").GetString()!;
        if(kind is "select" or "selectNext" or "deselect")
        {
            // A native selector can open while later replay events/personal
            // authority are still being applied. Match the product input gate.
            if(Get(renderer,"MultiplayerInputReady") is not true||Get(renderer,"_waitingForAuthority") is true||
                !ReferenceEquals(Get(renderer,"_battleAuthority"),Get(Get(renderer,"_personalMessage"),"Authority")))return;
        }
        if(kind is not "select" and not "selectNext" and not "deselect")interactionHandled=input.GetProperty("seq").GetInt32();
        bool success=false;object? detail=null;
        if(kind=="captureIdle")
        {
            int pending=((System.Collections.IDictionary)Get(null,"Pending",Rmp("MultiplayerPersonalUi"))!).Count;
            int reserved=((System.Collections.IDictionary)Get(null,"Reserved",Rmp("MultiplayerPersonalUi"))!).Count;
            success=pending==0&&reserved==0&&overlay is not NCombatPileCardSelectScreen;
            detail=new{pending,reserved};
        }
        else if(kind=="eye")
        {
            var eye=overlay==null?null:Get(overlay,"_peekButton") as NPeekButton;
            if(eye!=null){bool before=eye.IsPeeking;await NativeClick(renderer,NativePoint(eye));await Frames(8);success=eye.IsPeeking!=before;detail=new{before,after=eye.IsPeeking,hit=Call(renderer,"LocalUiAt",NativePoint(eye))?.GetType().Name};}
        }
        else if(kind=="selectScrolled")
        {
            if(grid!=null)
            {
                float bottom=(float)Get(grid,"ScrollLimitBottom")!;
                grid.SetScrollPosition(bottom);Call(grid,"AllocateCardHolders");await Frames(12);
                var holder=grid.CurrentlyDisplayedCardHolders.LastOrDefault(h=>h.CardModel is Wish&&h.IsVisibleInTree()&&NativePoint(h.Hitbox).Y is >.1f and <.85f);
                long before=(long)Get(renderer,"_request")!;string path=holder==null?"":(string)Static("MirrorNativeUi","Key",holder)!;
                if(holder!=null){await NativeClick(renderer,NativePoint(holder.Hitbox));await Frames(40);success=(long)Get(renderer,"_request")!>before;}
                detail=new{before,after=Get(renderer,"_request"),path,bottom,authority=Get(renderer,"_battleAuthority")};
            }
        }
        else if(kind=="scrollContract")
        {
            if(grid!=null)
            {
                string before=JsonSerializer.Serialize(Static("MultiplayerPersonalUi","Capture"));
                Static("LocalSpectatorSource","ScrollNative",grid,"down:64");await Frames(15);
                string after=JsonSerializer.Serialize(Static("MultiplayerPersonalUi","Capture"));
                success=before==after;detail=new{before,after};
            }
        }
        else if(kind=="select"||kind=="selectNext"||kind=="deselect")
        {
            var selected=JsonSerializer.SerializeToElement(Get(Get(renderer,"_personalMessage"),"Choice")).GetProperty("Selected").EnumerateArray().Select(i=>i.GetInt32()).ToHashSet();
            string model=input.TryGetProperty("model",out var m)?m.GetString()!:"";
            var action=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority")).GetProperty("Actions").EnumerateArray().FirstOrDefault(a=>a.GetProperty("Kind").GetString()=="select"&&a.GetProperty("Enabled").GetBoolean()&&a.GetProperty("CardId").GetString()!.Contains(model,StringComparison.OrdinalIgnoreCase)&&selected.Contains(int.Parse(a.GetProperty("NativePath").GetString()!.Split(":grid:").Last()))==(kind=="deselect"));
            if(action.ValueKind==JsonValueKind.Object&&Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,true) is NCardHolder holder)
            {
                if(grid!=null)
                {
                    int index=int.Parse(action.GetProperty("NativePath").GetString()!.Split(":grid:").Last());
                    float top=(float)Get(grid,"ScrollLimitTop")!,bottom=(float)Get(grid,"ScrollLimitBottom")!;
                    var size=(Vector2)Get(grid,"_cardSize")!;float padding=(float)Get(grid,"CardPadding")!;int columns=(int)Get(grid,"Columns")!;
                    grid.SetScrollPosition(Math.Clamp(top-(index/columns)*(size.Y+padding),Math.Min(top,bottom),Math.Max(top,bottom)));Call(grid,"AllocateCardHolders");await Frames(3);
                    holder=(NCardHolder)Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,false)!;
                }
                if(holder==null||!GodotObject.IsInstanceValid(holder)||Static("LocalSpectatorSource","NativeReplayReady",holder,"") is not true)return;
                interactionHandled=input.GetProperty("seq").GetInt32();
                long before=(long)Get(renderer,"_request")!;await NativeClick(renderer,NativePoint(holder.Hitbox));
                for(int i=0;i<150&&((long)Get(renderer,"_request")!<=before||(long)Get(renderer,"LastMultiplayerResult")!<(long)Get(renderer,"_request")!);i++)await Frames();
                success=(long)Get(renderer,"_request")! > before && (long)Get(renderer,"LastMultiplayerResult")! == (long)Get(renderer,"_request")! && Get(renderer,"LastMultiplayerAccepted") is true;
                await Frames(12);
                if(grid!=null&&GodotObject.IsInstanceValid(grid)&&ReferenceEquals(NOverlayStack.Instance?.Peek(),overlay))
                {
                    var highlights=((System.Collections.Generic.IEnumerable<CardModel>)Get(grid,"_highlightedCards")!).ToArray();
                    var localSelected=((System.Collections.Generic.IEnumerable<CardModel>)Get(overlay,"_selectedCards")!).ToArray();
                    success=success&&highlights.Length==localSelected.Length&&highlights.ToHashSet().SetEquals(localSelected);
                }
                detail=new{before,after=Get(renderer,"_request"),result=Get(renderer,"LastMultiplayerResult"),accepted=Get(renderer,"LastMultiplayerAccepted"),path=action.GetProperty("NativePath").GetString()};
            }
            else return; // Native grid allocation/overlay entrance is still in progress.
        }
        WritePath(Path.Combine(Root,"interaction-output.json"),new{seq=interactionHandled,kind,success,detail});
    }
    static async Task<JsonElement> InteractionInput(string kind,string model="")
    {
        int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;WritePath(Path.Combine(dir,"interaction-input.json"),new{seq,kind,model});
        await Wait(()=>ReadPath(Path.Combine(dir,"interaction-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"interaction-output.json")).GetProperty("seq").GetInt32()==seq,"043 input "+kind);
        var result=ReadPath(Path.Combine(dir,"interaction-output.json"));Write(Role+"-043-"+seq+".json",result);return result;
    }
    static async Task TestInteractions(RunState state)
    {
        await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster).coord);
        var me=LocalContext.GetMe(state)!;
        await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning&&me.PlayerCombatState!.Hand.Cards.OfType<Wish>().Any(),"043 Wish fixture");
        await Sync(state,"043-battle");await Grant("043-battle");
        if(System.Environment.GetEnvironmentVariable("RMP_043_BASELINE")!="1")
        {
            Check(state.GetPlayer(SourceId).PlayerCombatState!.DiscardPile.Cards.Count==0,"Hologram starts with an actually empty discard pile");
            await RemoteBattle("play","Hologram");
            await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&ChoiceKind()=="","empty discard Hologram finishes without selection UI");
            await Sync(state,"043-empty-discard");
            Check((await InteractionInput("captureIdle")).GetProperty("success").GetBoolean(),"empty discard leaves no pending or reserved selector capture");
        }
        await RemoteBattle("play","Wish");await Wait(()=>ChoiceKind()=="Pile","043 real Wish opens pile");await Frames(40);await Barrier("043-wish-open");
        if(System.Environment.GetEnvironmentVariable("RMP_043_BASELINE")!="1")
        {
            int sent=(int)Session("SentFrames")!;await Frames(15);
            Check((int)Session("SentFrames")!-sent<=8,"stable full-pile metadata does not queue unchanged frames ahead of input");
        }
        var snapshot=ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"interaction-state.json"));Write(Role+"-043-wish-diagnostic.json",snapshot);
        var click=await InteractionInput("selectScrolled");
        Check(click.GetProperty("success").GetBoolean(),"Wish native pile click issues an authoritative request");
        await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&ChoiceKind()=="","Wish finishes and resumes original action queue");await Sync(state,"043-wish-result");
        if(System.Environment.GetEnvironmentVariable("RMP_043_BASELINE")=="1")return;
        await TestRemainingInteractions(state);
        Write(Role+"-result.json",new{passed=true,checks,scope="v0.4.3 reported interactions"});await Barrier("043-done");
    }
}
