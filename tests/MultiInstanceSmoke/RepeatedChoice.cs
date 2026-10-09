using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public static partial class Smoke
{
    private static async Task RunRepeatedChoice()
    {
        try
        {
            for(int i=0;i<1800 && NGame.Instance?.MainMenu==null;i++) await Frames(1);
            await Frames(180); Controller=Type("LiveSharingController"); Controller.GetMethod("Close",Any)!.Invoke(null,null);
            SaveManager.Instance.SetFtuesEnabled(false);
            var character=ModelDb.Character<Necrobinder>();
            var player=Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
            var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPREPEAT10");
            player.Deck.Clear(true);
            foreach(var card in new CardModel[]{state.CreateCard<Cleanse>(player),state.CreateCard<Transfigure>(player)})
            { CardCmd.Enchant(ModelDb.Enchantment<RoyallyApproved>().ToMutable(),card,1); player.Deck.AddInternal(card,silent:true); }
            for(int i=0;i<90;i++) player.Deck.AddInternal(state.CreateCard<Wither>(player),silent:true);
            RunManager.Instance.SetUpNewSingleplayer(state,true); await PreloadManager.LoadRunAssets(new[]{character}); RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state)); await RunManager.Instance.SetActInternal(0);
            await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
            Controller.GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            await Until(()=>Verified,"repeated-choice initial combat agrees without source input");
            var process=Field(Static("_mirror")!,"Process")!; var view=Static("_view")!; Call(view,"SetControlEnabled",true);
            nint window=(nint)(long)Field(process,"Window")!; string profile=(string)Field(process,"DirectoryPath")!;
            System.Text.Json.JsonElement Status()
            { using var stream=new FileStream(Path.Combine(profile,"feedback-state.json"),FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var json=System.Text.Json.JsonDocument.Parse(stream); return json.RootElement.Clone(); }
            object[] Hits()=>((System.Collections.IEnumerable)Field(process,"LastHits")!).Cast<object>().ToArray();
            string Kind(object h)=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)!;
            void Send(uint message,float x,float y)
            { GetClientRect(window,out var rect); int px=(int)(x*rect.Right),py=(int)(y*rect.Bottom); Check(SendMessageTimeoutW(window,message,message==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,1000,out _)!=0,"repeated-choice native input delivered"); }
            async Task Click(Func<object,bool> predicate,string label)
            {
                await Until(()=>Verified && Hits().Any(predicate),label+" ready"); await Frames(30);
                var hit=Hits().Last(predicate); var r=(float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!;
                Send(0x201,r[0]+r[2]/2,r[1]+r[3]/2); await Frames(2); Send(0x202,r[0]+r[2]/2,r[1]+r[3]/2); await Frames(3);
            }
            async Task Play(CardModel card)
            {
                await Until(()=>Verified,"repeated-choice card ready"); await Frames(45);
                int index=player.PlayerCombatState!.Hand.Cards.ToList().IndexOf(card);
                var hit=Hits().First(h=>Kind(h)=="play" && (int)h.GetType().GetProperty("Index")!.GetValue(h)! == index);
                var r=(float[])hit.GetType().GetProperty("Rect")!.GetValue(hit)!;
                Send(0x200,r[0]+r[2]/2,r[1]+r[3]*.2f); Send(0x201,r[0]+r[2]/2,r[1]+r[3]*.2f); Send(0x200,.5f,.35f); await Frames(10); Send(0x202,.5f,.35f); await Frames(3);
            }
            await Frames(180);
            Check(Status().GetProperty("TransitionClear").GetBoolean(),"initial transition stays clear without source action");
            GetClientRect(window,out var initialClient);
            Check(Status().GetProperty("DisplayWidth").GetInt32()==initialClient.Right && Status().GetProperty("DisplayHeight").GetInt32()==initialClient.Bottom &&
                Status().GetProperty("PresentFrames").GetInt32()==0,"initial Godot display size matches owned window and all startup redraws finish before source input");
            await Play(player.PlayerCombatState!.Hand.Cards.OfType<Transfigure>().Single());
            await Until(()=>Verified && NPlayerHand.Instance?.CurrentMode==NPlayerHand.Mode.SimpleSelect,"transfigure selector ready");
            await Click(h=>Kind(h)=="select" && ((string)h.GetType().GetProperty("NodeKey")!.GetValue(h)!).Contains("CARD.CLEANSE"),"choose cleanse");
            await Click(h=>Kind(h)=="choice","confirm cleanse transfigure");
            await Until(()=>Verified && !RunManager.Instance.ActionExecutor.IsRunning && player.PlayerCombatState!.Hand.Cards.OfType<Cleanse>().Single().BaseReplayCount==1,"cleanse has one additional native replay");
            int exhaustBefore=player.PlayerCombatState!.ExhaustPile.Cards.Count;
            await Play(player.PlayerCombatState.Hand.Cards.OfType<Cleanse>().Single());
            ulong previous=0;
            for(int repeat=0;repeat<2;repeat++)
            {
                await Until(()=>Verified && NOverlayStack.Instance?.Peek() is NCombatPileCardSelectScreen s && s.GetInstanceId()!=previous &&
                    File.Exists(Path.Combine(profile,"feedback-state.json")) && Status().GetProperty("Screen").GetString()=="NCombatPileCardSelectScreen","cleanse choice "+repeat+" becomes usable without another source action");
                previous=((Node)NOverlayStack.Instance!.Peek()!).GetInstanceId(); await Frames(60);
                var status=Status(); GD.Print("[MultiInstanceSmoke] REPEAT before wheel "+status);
                if(repeat==1) { Call(view,"SetControlEnabled",false); await Frames(30); }
                var journal=(System.Collections.IList)Type("MirrorJournal").GetField("Operations",Any)!.GetValue(null)!;
                int eventsBefore=journal.Count;
                var sourceGrid=Descendants<NCardGrid>((Node)NOverlayStack.Instance.Peek()!).Single();
                float sourceOffset=(float)Field(sourceGrid,"_targetDrag")!;
                var sr=status.GetProperty("ScrollRect").EnumerateArray().Select(v=>v.GetSingle()).ToArray(); float before=status.GetProperty("Offset").GetSingle();
                GetClientRect(window,out var rect); var p=new NativePoint{X=(int)((sr[0]+sr[2]/2)*rect.Right),Y=(int)((sr[1]+sr[3]/2)*rect.Bottom)}; ClientToScreen(window,ref p);
                for(int tick=0;tick<(repeat==0?6:20);tick++) SendMessageTimeoutW(window,0x20A,unchecked((nuint)((-120&65535)<<16)),(nint)((p.Y<<16)|(p.X&65535)),2,1000,out _);
                await Until(()=>Status().GetProperty("Offset").GetSingle()<before-150 && Verified,"cleanse choice "+repeat+" wheel works without source fallback");
                Check(journal.Count==eventsBefore && (float)Field(sourceGrid,"_targetDrag")! == sourceOffset,"selector wheel stays local and creates no authority wait or replay operations");
                if(repeat==1) { Call(view,"SetControlEnabled",true); await Frames(30); }
                await Frames(90);
                await Click(h=>Kind(h)=="select" && h.GetType().GetProperty("Rect")!.GetValue(h) is float[] r && r[1]>.12f && r[1]+r[3]<.95f && r[3]>.15f,"cleanse choice "+repeat+" native selection");
            }
            await Until(()=>Verified && !RunManager.Instance.ActionExecutor.IsRunning && NOverlayStack.Instance?.Peek()==null && player.PlayerCombatState!.ExhaustPile.Cards.Count==exhaustBefore+2,"repeated cleanse resolves both exhaust choices and resumes combat");
            var journalBefore=(System.Collections.IList)Type("MirrorJournal").GetField("Operations",Any)!.GetValue(null)!;
            int eventCount=journalBefore.Count;
            string hash=(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!;
            var oldChild=(System.Diagnostics.Process)process.GetType().GetProperty("Child",Any)!.GetValue(process)!;
            int oldPid=oldChild.Id;
            var refresh=(Button)Field(view,"_refresh")!;
            Check(refresh.IsVisibleInTree(),"titlebar resync is available even without a reported sync error");
            refresh.EmitSignal(Button.SignalName.Pressed);
            await Until(()=>Verified,"manual titlebar resync replays both cleanse choices without source fallback");
            var newProcess=Field(Static("_mirror")!,"Process")!;
            var newChild=(System.Diagnostics.Process)newProcess.GetType().GetProperty("Child",Any)!.GetValue(newProcess)!;
            Check(newChild.Id!=oldPid && journalBefore.Count==eventCount && hash==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{state})!,"manual resync replaces the replica and preserves source state, RNG and journal");
            Controller.GetMethod("Suspend",Any)!.Invoke(null,null);
            GD.Print("[MultiInstanceSmoke] REPLAY PASSED"); ((SceneTree)Engine.GetMainLoop()).Quit();
        }
        catch(Exception e) { GD.PrintErr("[MultiInstanceSmoke] FAIL REPLAY "+e); Controller?.GetMethod("Suspend",Any)?.Invoke(null,null); ((SceneTree)Engine.GetMainLoop()).Quit(1); }
    }
}
