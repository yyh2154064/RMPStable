using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment = System.Environment;

public static partial class Smoke
{
    private static async Task RunFullNative()
    {
        try
        {
            for(int i=0;i<1800 && NGame.Instance?.MainMenu==null;i++) await Frames(1);
            await Frames(180); Controller=Type("LiveSharingController"); Controller.GetMethod("Close",Any)!.Invoke(null,null);
            SaveManager.Instance.SetFtuesEnabled(false);
            var character=ModelDb.Character<Ironclad>();
            var player=Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
            var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPFULLNATIVE");
            player.Gold=1000; // Isolated fixture: exercise purchases without farming.
            player.AddPotionInternal(ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.FirePotion>().ToMutable(),0,true);
            player.Deck.Clear(true);
            player.Deck.AddInternal(state.CreateCard<SculptingStrike>(player),silent:true);
            player.Deck.AddInternal(state.CreateCard<DefendIronclad>(player),silent:true);
            player.Deck.AddInternal(state.CreateCard<Transfigure>(player),silent:true);
            for(int i=0;i<2;i++)
            { var card=state.CreateCard<Whirlwind>(player); card.UpgradeInternal(); card.FinalizeUpgradeInternal(); player.Deck.AddInternal(card,silent:true); }
            RunManager.Instance.SetUpNewSingleplayer(state,true); await PreloadManager.LoadRunAssets(new[]{character}); RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state)); await RunManager.Instance.SetActInternal(0);
            await RunManager.Instance.LoadIntoLatestMapCoord(null);
            Controller.GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            await Until(()=>Verified,"full native initial map room matches authority");
            var mirror=Static("_mirror")!; var process=Field(mirror,"Process")!;
            var view=Static("_view")!; Call(view,"SetControlEnabled",true);
            var source=Static("_source")!;
            object[] Actions()
            {
                var snapshot=Call(source,"CaptureCommands",state)!;
                var control=snapshot.GetType().GetProperty("Control")!.GetValue(snapshot)!;
                return ((System.Collections.IEnumerable)control.GetType().GetProperty("Actions")!.GetValue(control)!).Cast<object>().ToArray();
            }
            object? Node(object a) => Type("MirrorNativeUi").GetMethod("Resolve",Any)!.Invoke(null,new object?[]{a.GetType().GetProperty("NativePath")!.GetValue(a),false});
            bool Enabled(object a) => (bool)a.GetType().GetProperty("Enabled")!.GetValue(a)!;
            string Kind(object a) => (string)a.GetType().GetProperty("Kind")!.GetValue(a)!;
            string Key(object a) => (string)a.GetType().GetProperty("NativePath")!.GetValue(a)!;
            System.Collections.IEnumerable Hits() => (System.Collections.IEnumerable)Field(process,"LastHits")!;
            nint Window() => (nint)(long)Field(process,"Window")!;
            void Send(uint message,float x,float y)
            {
                GetClientRect(Window(),out var rect); int px=(int)(x*rect.Right),py=(int)(y*rect.Bottom);
                Check(SendMessageTimeoutW(Window(),message,message==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,5000,out _)!=0,"full native owned window message dispatched");
            }
            (float X,float Y) Center(object hit)
            { var r=(float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!; return (r[0]+r[2]/2,r[1]+r[3]/2); }
            async Task Click(Func<object,bool> predicate,string label)
            {
                object? action=null,hit=null;
                await Until(()=>Verified && (bool)Field(process,"LastControlEnabled")! && (action=Actions().FirstOrDefault(a=>Enabled(a)&&predicate(a)))!=null &&
                    (hit=Hits().Cast<object>().LastOrDefault(h=>(string)h.GetType().GetProperty("NodeKey")!.GetValue(h)! ==Key(action)))!=null,label+" original control ready");
                await Frames(45); // Original opening tweens can temporarily overlap cards.
                action=Actions().First(a=>Enabled(a)&&predicate(a));
                hit=Hits().Cast<object>().Last(h=>(string)h.GetType().GetProperty("NodeKey")!.GetValue(h)! ==Key(action));
                int beforeEvents=(int)Field(process,"LastEvents")!;
                long beforeGeneration=(long)Field(Field(process,"Wire")!,"Identity")!.GetType().GetField("Generation",Any)!.GetValue(Field(Field(process,"Wire")!,"Identity"))!;
                var p=Center(hit!); GD.Print("[MultiInstanceSmoke] FULL CLICK "+Node(action!)?.GetType().Name+" "+Key(action!)+" point="+p);
                Send(0x200,p.X,p.Y); Send(0x201,p.X,p.Y); await Frames(2); Send(0x202,p.X,p.Y);
                await Frames(30);
                await Until(()=>Verified && ((int)Field(process,"LastEvents")!>beforeEvents || (long)Field(Field(process,"Wire")!,"Identity")!.GetType().GetField("Generation",Any)!.GetValue(Field(Field(process,"Wire")!,"Identity"))!>beforeGeneration),label+" executes and authority and replica agree");
            }
            async Task Drag(CardModel card,bool arrow,bool settle = true)
            {
                await Until(()=>Verified,"card drag native state ready");
                await Frames(30);
                int index=player.PlayerCombatState!.Hand.Cards.ToList().IndexOf(card);
                var h=Hits().Cast<object>().First(a=>(string)a.GetType().GetProperty("Kind")!.GetValue(a)! =="play" && (int)a.GetType().GetProperty("Index")!.GetValue(a)! ==index);
                var r=(float[])h.GetType().GetProperty("Rect")!.GetValue(h)!;
                var p=(X:r[0]+r[2]/2,Y:r[1]+r[3]*.2f); GD.Print("[MultiInstanceSmoke] FULL DRAG "+card.Id+" index="+index+" point="+p);
                int enemyIndex=player.Creature.CombatState!.Creatures.ToList().FindIndex(c=>c.IsEnemy && c.IsAlive);
                var target=Hits().Cast<object>().FirstOrDefault(a=>(string)a.GetType().GetProperty("Kind")!.GetValue(a)! =="target" && (int)a.GetType().GetProperty("Index")!.GetValue(a)! ==enemyIndex);
                var end=arrow?Center(target!):(X:.5f,Y:.35f);
                if (arrow)
                {
                    var targetRect=(float[])target!.GetType().GetProperty("Rect")!.GetValue(target)!;
                    end=(targetRect[0]+targetRect[2]/2,targetRect[1]+targetRect[3]*.2f);
                    GD.Print("[MultiInstanceSmoke] FULL TARGET point="+end+" rect="+string.Join(",",targetRect));
                }
                Send(0x200,p.X,p.Y); Send(0x201,p.X,p.Y); Send(0x200,end.X,end.Y); await Frames(30);
                Check((bool)Field(process,"TargetArrowVisible")! == arrow,card.Id+" uses original target type arrow rule");
                Send(0x202,end.X,end.Y); await Frames(settle ? 30 : 2);
            }
            async Task Keyboard(uint key)
            {
                var host=mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror)!;
                Check((bool)Call(host,"RouteKeyboard",0x100u,(nuint)key,(nint)1,true)!,"full keyboard input routes to replica");
                await Frames(2);
                Check((bool)Call(host,"RouteKeyboard",0x101u,(nuint)key,unchecked((nint)0xC0000001),false)!,"full keyboard release retains replica owner");
                await Frames(20);
            }
            if(Environment.GetEnvironmentVariable("RMP_MULTI_TREASURE_TEST") != "1")
            {
            await Frames(150);
            int drawingEvents=(int)Field(process,"LastEvents")!;
            Send(0x200,.35f,.55f); Send(0x204,.35f,.55f); await Frames(10);
            for(int stroke=1;stroke<=12;stroke++) { Send(0x200,.35f+stroke*.003f,.55f+stroke*.002f); await Frames(1); }
            Send(0x200,.4f,.6f); await Frames(10); Send(0x205,.4f,.6f);
            await Until(()=>Verified && (int)Field(process,"LastEvents")!>drawingEvents &&
                (string)Field(process,"LastDrawingHash")! == (string)Type("MirrorState").GetMethod("DrawingHash",Any)!.Invoke(null,null)!,"native map drawing matches authority");
            drawingEvents=(int)Field(process,"LastEvents")!;
            Send(0x204,.55f,.55f); await Frames(10); Send(0x205,-.02f,1.02f);
            await Until(()=>Verified && (int)Field(process,"LastEvents")!>drawingEvents &&
                (string)Field(process,"LastDrawingHash")! == (string)Type("MirrorState").GetMethod("DrawingHash",Any)!.Invoke(null,null)!,"native map stroke released outside window terminates consistently");
            Check(!NMapScreen.Instance!.Drawings.IsLocalDrawing() && NMapScreen.Instance.Drawings.GetLocalDrawingMode()==DrawingMode.None,"right-button release leaves no source drawing or override mode");
            var map=NMapScreen.Instance!;
            Call(source,"RouteNativeMapInput",true);
            map._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true,Position=new Vector2(640,360)});
            Check(Field(map,"_drawingInput")==null,"source GUI cannot start another held pen while pointer is over replica");
            Call(source,"RouteNativeMapInput",false);
            var held=NMapDrawingInput.Create(map.Drawings,DrawingMode.Drawing,true);
            held.Connect(NMapDrawingInput.SignalName.Finished,Callable.From(()=>map.GetType().GetField("_drawingInput",Any)!.SetValue(map,null)));
            map.GetType().GetField("_drawingInput",Any)!.SetValue(map,held); map.AddChild(held);
            Call(source,"RouteNativeMapInput",true); Call(source,"RouteNativeMapInput",false);
            Check(held.IsQueuedForDeletion() && !map.Drawings.IsLocalDrawing() && map.Drawings.GetLocalDrawingMode()==DrawingMode.None,"return to source retires held pen whose button release occurred over popup");
            await Until(()=>Verified && (string)Field(process,"LastDrawingHash")! == (string)Type("MirrorState").GetMethod("DrawingHash",Any)!.Invoke(null,null)!,"drawing cleanup remains synchronized with replica");
            float mapBefore=((Vector2)Field(map,"_targetDragPos")!).Y;
            GD.Print("[MultiInstanceSmoke] MAP WHEEL before="+mapBefore+" canScroll="+Call(map,"CanScroll")+" sourceEvents="+Field(process,"LastEvents"));
            var wheelPoint=new NativePoint{X=170,Y=110}; ClientToScreen(Window(),ref wheelPoint);
            for(int wheel=0;wheel<6;wheel++)
                Check(SendMessageTimeoutW(Window(),0x20A,(nuint)((120&65535)<<16),(nint)((wheelPoint.Y<<16)|(wheelPoint.X&65535)),2,1000,out _)!=0,"queued map wheel dispatched");
            await Frames(120);
            GD.Print("[MultiInstanceSmoke] MAP WHEEL after="+((Vector2)Field(map,"_targetDragPos")!).Y+" canScroll="+Call(map,"CanScroll")+" sourceEvents="+Field(process,"LastEvents"));
            await Until(()=>Verified && ((Vector2)Field(map,"_targetDragPos")!).Y>mapBefore+500,"six rapid map wheel ticks move at least 500 native pixels without losses");
            Type("LocalSpectatorSource").GetMethod("ScrollNative",Any)!.Invoke(null,new object[]{map,"down:24"}); await Frames(90);
            await Until(()=>Verified,"map returns to initial scroll range before travel regression");
            if(Environment.GetEnvironmentVariable("RMP_MULTI_ROOMS_TEST") != "1")
            {
            if(NMapScreen.Instance?.IsOpen!=true) await Click(a=>Node(a)?.GetType().Name=="NOpenMapButton","open original map");
            await Frames(150);
            await Click(a=>Node(a) is NMapPoint,"travel original map point");
            GD.Print("[MultiInstanceSmoke] FULL ROOM after map="+state.CurrentRoom?.GetType().Name+" coords="+state.VisitedMapCoords.Count);
            if(NCombatRoom.Instance==null)
            {
                var monster=state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster);
                await RunManager.Instance.EnterMapCoord(monster.coord);
            }
            await Until(()=>NCombatRoom.Instance!=null && Verified,"map travel builds native combat");
            await Drag(player.PlayerCombatState!.Hand.Cards.OfType<DefendIronclad>().First(),false);
            await Until(()=>Verified && player.Creature.Block>0,"self target defense resolves without red arrow");
            async Task SourceMouseChoice(string label, bool reselect)
            {
                // Real source mouse signal, without waiting for replica replay.
                // This was absent from the old window-only selection fixture.
                await Until(()=>NPlayerHand.Instance?.CurrentMode==NPlayerHand.Mode.SimpleSelect,label+" source awaits hand choice");
                var hand=NPlayerHand.Instance!;
                void Mouse(NHandCardHolder holder) => holder.GetType().GetMethod("OnMousePressed",Any)!.Invoke(holder,new object[]{new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true}});
                var chosen=hand.ActiveHolders.First(h=>h.CardModel is Whirlwind);
                var card=chosen.CardModel!;
                var journal=(System.Collections.IList)Type("MirrorJournal").GetField("Operations",Any)!.GetValue(null)!;
                int before=journal.Count;
                Mouse(chosen);
                if(reselect)
                {
                    var selected=Descendants<NSelectedHandCardHolder>(hand).Single(h=>h.CardModel==card);
                    selected.EmitSignal(NCardHolder.SignalName.Pressed,selected);
                    Mouse(hand.ActiveHolders.Single(h=>h.CardModel==card));
                }
                var confirm=(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl)Field(hand,"_selectModeConfirmButton")!;
                Check(confirm.IsEnabled,label+" original confirm enabled after mouse choice");
                confirm.ForceClick();
                Check(journal.Count>=before+(reselect?4:2),label+" records mouse select/deselect/reselect and confirm");
                await Until(()=>Verified && hand.CurrentMode==NPlayerHand.Mode.Play && !RunManager.Instance.ActionExecutor.IsRunning &&
                    (int)Field(process,"LastEvents")! == journal.Count,label+" rapid source choice and native replica converge");
            }
            var sculpting=player.PlayerCombatState!.Hand.Cards.OfType<SculptingStrike>().First();
            int sculptingIndex=NPlayerHand.Instance!.ActiveHolders.ToList().FindIndex(h=>h.CardModel==sculpting);
            var sculptingKey=MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager.Instance.GetMKbHotkey("mega_select_card_"+(sculptingIndex+1));
            Check(sculptingKey!=Godot.Key.None,"targeted card has original numeric shortcut");
            await Keyboard((uint)sculptingKey);
            await Until(()=>(bool)Field(process,"TargetArrowVisible")!,"numeric targeted-card shortcut shows original arrow");
            await Keyboard(39); await Keyboard(13);
            await SourceMouseChoice("sculpting strike",true);
            await Until(()=>Verified && NPlayerHand.Instance?.CurrentMode==NPlayerHand.Mode.Play,"sculpting strike selection completes and combat resumes");
            Check(player.PlayerCombatState!.Hand.Cards.Any(c=>c.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal)),"selected native card receives ethereal");
            await Drag(player.PlayerCombatState!.Hand.Cards.OfType<Transfigure>().First(),false,false);
            await SourceMouseChoice("transfigure",false);
            Check(player.PlayerCombatState!.Hand.Cards.OfType<Whirlwind>().Any(c=>c.BaseReplayCount==1),"transfigure original repeat modification reaches selected native card");
            await Click(a=>Node(a)?.GetType().Name=="NPotionHolder" && Node(a)?.GetType().GetProperty("Potion")?.GetValue(Node(a))!=null,"open original potion popup");
            await Click(a=>Node(a) is Node n && n.Name.ToString()=="UseButton","use original targeted potion");
            var pileHit=Hits().Cast<object>().First(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! =="ui.discard");
            var pilePoint=Center(pileHit); Send(0x201,pilePoint.X,pilePoint.Y); await Frames(2); Send(0x202,pilePoint.X,pilePoint.Y); await Frames(30);
            Check(Verified && NTargetManager.Instance?.IsInSelection==true,"local pile click cannot cancel authoritative potion targeting");
            await Click(a=>Kind(a)=="target","choose original potion target");
            await Until(()=>Verified && !player.Potions.Any(),"native targeted potion resolves in both processes");
            for(int round=0;round<8 && CombatManager.Instance.IsInProgress;round++)
            {
                await Until(()=>Verified,"full combat round consistent");
                var whirlwind=player.PlayerCombatState!.Hand.Cards.OfType<Whirlwind>().FirstOrDefault();
                if(whirlwind!=null && player.PlayerCombatState.Energy>0) { await Drag(whirlwind,false); await Until(()=>Verified,"all enemies card resolves without single target arrow"); }
                if(CombatManager.Instance.IsInProgress)
                {
                    if(round==0)
                    {
                        int turn=player.PlayerCombatState!.TurnNumber;
                        await Keyboard((uint)MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager.Instance.GetMKbHotkey("ui_end_turn"));
                        await Until(()=>Verified && (!CombatManager.Instance.IsInProgress || player.PlayerCombatState!.TurnNumber>turn),"native end-turn keyboard binding executes on authority");
                    }
                    else await Click(a=>Kind(a)=="endTurn","full combat end turn");
                }
            }
            await Until(()=>!CombatManager.Instance.IsInProgress && Verified,"combat finishes into identical original rewards");
            await Until(()=>Verified && Actions().Any(a=>Enabled(a)&&Node(a)?.GetType().Name=="NRewardButton"),"original rewards are interactive");
            string RewardKind(object a) => Node(a)?.GetType().GetProperty("Reward")?.GetValue(Node(a))?.GetType().Name ?? "";
            int gold=player.Gold;
            await Click(a=>RewardKind(a)=="GoldReward","claim original gold reward");
            Check(player.Gold>gold,"gold reward changes authoritative gold");
            int deckCount=player.Deck.Cards.Count;
            await Click(a=>RewardKind(a)=="CardReward","open original card reward selection");
            await Click(a=>Kind(a)=="select","choose original card reward");
            await Until(()=>Verified && player.Deck.Cards.Count>deckCount,"card reward reaches native deck in both processes");
            await Click(a=>Node(a)?.GetType().Name=="NProceedButton","leave original rewards");
            }
            }
            async Task Enter(MapPointType type,string label)
            {
                var point=state.Map.GetAllMapPoints().First(p=>p.PointType==type);
                await RunManager.Instance.EnterMapCoord(point.coord);
                await Until(()=>Verified,label+" native room agrees");
                await Frames(60);
            }
            if(Environment.GetEnvironmentVariable("RMP_MULTI_TREASURE_TEST") != "1")
            {
            await Enter(MapPointType.RestSite,"campfire");
            int upgradeCount=player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
            await Click(a=>Node(a)?.GetType().GetProperty("Option")?.GetValue(Node(a))?.GetType().Name=="SmithRestSiteOption","campfire smith");
            await Click(a=>Kind(a)=="select","campfire upgrade card selection");
            await Click(a=>Node(a)?.GetType().Name=="NConfirmButton","campfire upgrade confirmation");
            await Until(()=>Verified && player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)>upgradeCount,"campfire upgrades native card");
            await Click(a=>Node(a)?.GetType().Name=="NProceedButton","leave original campfire");
            await Enter(MapPointType.Shop,"merchant");
            await Click(a=>Node(a)?.GetType().Name=="NMerchantButton","open original merchant");
            int shopGold=player.Gold, shopDeckCount=player.Deck.Cards.Count;
            await Click(a=>Kind(a)=="buy" && Node(a)?.GetType().Name=="NMerchantCard","purchase original merchant card");
            await Until(()=>Verified && player.Gold<shopGold && player.Deck.Cards.Count>shopDeckCount,"merchant purchase preserves gold and deck");
            int shopRelics=player.Relics.Count;
            await Click(a=>Kind(a)=="buy" && Node(a)?.GetType().Name=="NMerchantRelic","purchase original merchant relic");
            await Until(()=>Verified && player.Relics.Count>shopRelics,"merchant relic enters native inventory");
            int shopPotions=player.Potions.Count();
            await Click(a=>Kind(a)=="buy" && Node(a)?.GetType().Name=="NMerchantPotion","purchase original merchant potion");
            await Until(()=>Verified && player.Potions.Count()>shopPotions,"merchant potion enters native inventory");
            int removalDeck=player.Deck.Cards.Count,removalGold=player.Gold;
            await Click(a=>Kind(a)=="buy" && Node(a)?.GetType().Name=="NMerchantCardRemoval","open original merchant removal selection");
            await Click(a=>Kind(a)=="select","select original card for removal");
            await Click(a=>Node(a)?.GetType().Name=="NConfirmButton","confirm original merchant removal");
            await Until(()=>Verified && player.Deck.Cards.Count==removalDeck-1 && player.Gold<removalGold,"merchant removal updates native deck and gold");
            await Click(a=>Node(a)?.GetType().Name=="NBackButton","close original merchant inventory");
            await Click(a=>Node(a)?.GetType().Name=="NProceedButton","leave original merchant");
            }
            await Enter(MapPointType.Treasure,"treasure");
            int relicCount=player.Relics.Count;
            await Click(a=>Node(a) is Node n && n.Name.ToString()=="Chest","open original treasure chest");
            await Click(a=>Node(a)?.GetType().Name=="NTreasureRoomRelicHolder","claim original chest relic");
            await Until(()=>Verified && player.Relics.Count>relicCount,"treasure relic reaches native inventory");
            await Click(a=>Node(a)?.GetType().Name=="NProceedButton","leave original treasure");
            await Enter(MapPointType.Unknown,"question mark");
            GD.Print("[MultiInstanceSmoke] FULL QUESTION room="+state.CurrentRoom?.GetType().Name);
            if(NRun.Instance.EventRoom!=null)
            {
                await Click(a=>Node(a)?.GetType().Name=="NEventOptionButton","original event option");
                await Until(()=>Verified,"event choice agrees");
            }
            else throw new Exception("Fixture question mark did not roll an event");
            int actIndex=state.CurrentActIndex;
            await RunManager.Instance.EnterNextAct();
            await Until(()=>Verified && state.CurrentActIndex==actIndex+1,"next act rebuilds original map and matches authority");
            Controller.GetMethod("Suspend",Any)!.Invoke(null,null);
            GD.Print("[MultiInstanceSmoke] FULL PASSED"); ((SceneTree)Engine.GetMainLoop()).Quit();
        }
        catch(Exception e) { GD.PrintErr("[MultiInstanceSmoke] FAIL FULL "+e); Controller?.GetMethod("Suspend",Any)?.Invoke(null,null); ((SceneTree)Engine.GetMainLoop()).Quit(1); }
    }
}
