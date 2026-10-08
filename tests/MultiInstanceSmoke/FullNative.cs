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
            for(int i=0;i<3;i++)
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
            object? Node(object a) => Type("MirrorNativeUi").GetMethod("Resolve",Any)!.Invoke(null,new[]{a.GetType().GetProperty("NativePath")!.GetValue(a)});
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
            async Task Drag(CardModel card,bool arrow)
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
                Send(0x202,end.X,end.Y); await Frames(30);
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
            await Drag(player.PlayerCombatState!.Hand.Cards.OfType<SculptingStrike>().First(),true);
            await Until(()=>Verified && NPlayerHand.Instance?.CurrentMode==NPlayerHand.Mode.SimpleSelect,"sculpting strike opens original pending hand selection in both processes");
            var selection=player.PlayerCombatState!.Hand.Cards.First();
            await Click(a=>Kind(a)=="select","sculpting strike native hand selection");
            if(NPlayerHand.Instance?.CurrentMode==NPlayerHand.Mode.SimpleSelect) await Click(a=>Kind(a)=="choice","sculpting strike confirm selection");
            await Until(()=>Verified && NPlayerHand.Instance?.CurrentMode==NPlayerHand.Mode.Play,"sculpting strike selection completes and combat resumes");
            Check(player.PlayerCombatState!.Hand.Cards.Any(c=>c.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal)),"selected native card receives ethereal");
            await Click(a=>Node(a)?.GetType().Name=="NPotionHolder" && Node(a)?.GetType().GetProperty("Potion")?.GetValue(Node(a))!=null,"open original potion popup");
            await Click(a=>Node(a) is Node n && n.Name.ToString()=="UseButton","use original targeted potion");
            await Click(a=>Kind(a)=="target","choose original potion target");
            await Until(()=>Verified && !player.Potions.Any(),"native targeted potion resolves in both processes");
            for(int round=0;round<8 && CombatManager.Instance.IsInProgress;round++)
            {
                await Until(()=>Verified,"full combat round consistent");
                var whirlwind=player.PlayerCombatState!.Hand.Cards.OfType<Whirlwind>().FirstOrDefault();
                if(whirlwind!=null && player.PlayerCombatState.Energy>0) { await Drag(whirlwind,false); await Until(()=>Verified,"all enemies card resolves without single target arrow"); }
                if(CombatManager.Instance.IsInProgress) await Click(a=>Kind(a)=="endTurn","full combat end turn");
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
