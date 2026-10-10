using System;
using System.IO;
using System.Linq;
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
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment = System.Environment;

public static partial class Smoke
{
    private static async Task RendererFeedback()
    {
        bool mapCircleSaved=false;
        while (true)
        {
            await ((SceneTree)Engine.GetMainLoop()).ToSignal(Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);
            if (NGame.Instance == null || NRun.Instance == null) continue;
            var settings = Type("LiveSharingController").Assembly.GetType("RemoveMultiplayerPlayerLimit.Infrastructure.SceneMonitor")!.GetMethod("FindSettingsScreen",Any)!.Invoke(null,null) as Control;
            var root = settings?.IsVisibleInTree()==true ? settings : NCapstoneContainer.Instance?.CurrentCapstoneScreen as Node ?? NOverlayStack.Instance?.Peek() as Node;
            var scroll = root == null ? null : Descendants<Control>(root).LastOrDefault(n => n is NCardGrid or NScrollableContainer && n.IsVisibleInTree());
            float offset = scroll == null ? 0 : (float)Field(scroll, scroll is NCardGrid ? "_targetDrag" : "_targetDragPosY")!;
            var renderer = Type("MirrorRenderer").GetField("Active", Any)!.GetValue(null);
            var notice = renderer == null ? null : Field(renderer, "_notice") as Label;
            var feedbackPath=Path.Combine(Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!, "feedback-state.json");
            File.WriteAllText(feedbackPath+".tmp",
                System.Text.Json.JsonSerializer.Serialize(new { Screen = root != null && Descendants<Node>(root).Any(n=>n.GetType().Name=="NPauseMenu" && n is Control c && c.IsVisibleInTree()) && settings?.IsVisibleInTree()!=true ? "NPauseMenu" : root?.GetType().Name ?? "", Instance = root?.GetInstanceId() ?? 0, Offset = offset,
                    QuickSlOpen = (bool)Type("LiveSharingController").Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")!.GetProperty("ConfirmationOpen",Any)!.GetValue(null)!,
                    VolumeMaster = SaveManager.Instance.SettingsSave.VolumeMaster,
                    ShowRunTimer = SaveManager.Instance.PrefsSave.ShowRunTimer,
                    CardInputReady = renderer != null && (bool)renderer.GetType().GetProperty("CanBufferCard",Any)!.GetValue(renderer)! && (bool)renderer.GetType().GetProperty("NativeIdle",Any)!.GetValue(renderer)!,
                    CardKey1 = NInputManager.Instance.GetMKbHotkey("mega_select_card_1").ToString(),
                    SettingsControls = settings?.IsVisibleInTree()==true ? Descendants<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>(settings).Where(c=>c.IsVisibleInTree() && c.IsEnabled).Select(c=> new { Name=c.Name.ToString(), Type=c.GetType().Name, Rect=Normalized(c) }).ToArray() : null,
                    Waiting = renderer != null && (bool)Field(renderer,"_waitingForAuthority")!, Busy = renderer != null && (bool)Field(renderer,"_busy")!,
                    AuthorityReady = renderer != null && (bool)Field(renderer,"_authorityReady")!, Events = renderer == null ? 0 : (int)Field(renderer,"_events")!,
                    PresentFrames = renderer == null ? -1 : (int)Field(renderer,"_presentFrames")!,
                    DisplayWidth = DisplayServer.WindowGetSize().X, DisplayHeight = DisplayServer.WindowGetSize().Y,
                    ScrollRect = scroll == null ? new float[4] : Normalized(scroll),
                    Hand = NPlayerHand.Instance?.ActiveHolders.Where(h => h.CardModel != null && h.IsVisibleInTree() && renderer != null && !(bool)Call(renderer,"IsCardReserved",h.CardModel)!).OrderBy(h => h.ZIndex)
                        .Select(h => new { Key = h.CardModel!.GetHashCode(), Id = h.CardModel.Id.ToString(), Exhaust = h.CardModel.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Exhaust), Rect = Normalized(h.Hitbox) }).ToArray(),
                    NoticeY = notice?.Position.Y ?? 0, NoticeHeight = notice?.Size.Y ?? 0,
                    ViewHeight = NGame.Instance.GetViewport().GetVisibleRect().Size.Y,
                    TransitionClear = !NGame.Instance.Transition.InTransition &&
                        NGame.Instance.Transition.GetNode<Control>("SimpleTransition").Modulate.A < .01f &&
                        NGame.Instance.Transition.GetNode<Control>("GradientTransition").Modulate.A < .01f &&
                        (NGame.Instance.Transition.Material is not ShaderMaterial transition || transition.GetShaderParameter("threshold").AsSingle() < .01f),
                    MapY = NMapScreen.Instance == null ? 0 : ((Vector2)Field(NMapScreen.Instance,"_targetDragPos")!).Y,
                    MapCanScroll = NMapScreen.Instance != null && (bool)Call(NMapScreen.Instance,"CanScroll")! }));
            // Windows can transiently deny replacing a delete-pending file
            // even when readers share deletion. Keep the observer alive.
            for(int attempt=0;;attempt++)
            {
                try { File.Move(feedbackPath+".tmp",feedbackPath,true); break; }
                catch(Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt<30) { await Frames(1); }
            }
            if (!mapCircleSaved && NMapScreen.Instance?.IsOpen==true && Descendants<MegaCrit.Sts2.Core.Nodes.Vfx.NMapCircleVfx>(NRun.Instance)
                .Any(v=>v.IsVisibleInTree() && v.GetParent() is NMapPoint { State: MapPointState.Travelable }))
            {
                await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var pixels=NGame.Instance.GetViewport().GetTexture().GetImage();
                pixels.SavePng(Path.Combine(Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!,"visual-map-circle.png"));
                mapCircleSaved=true; GD.Print("[MultiInstanceSmoke] VISUAL native animated map circle");
            }
            for (int i=0; i<5; i++) await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var node in root.GetChildren())
        {
            if (node is T typed) yield return typed;
            foreach (var child in Descendants<T>(node)) yield return child;
        }
    }
    private static float[] Normalized(Control control)
    {
        var viewport=control.GetViewport();
        var rect=(viewport.GetFinalTransform()*control.GetGlobalTransformWithCanvas())*new Rect2(Vector2.Zero,control.Size);
        var window=(nint)Type("MirrorWin32").GetProperty("OwnWindow",Any)!.GetValue(null)!;
        GetClientRect(window,out var size);
        return new[]{rect.Position.X/size.Right,rect.Position.Y/size.Bottom,rect.Size.X/size.Right,rect.Size.Y/size.Bottom};
    }
    private static async Task RunFeedback()
    {
        try
        {
            for(int i=0;i<1800 && NGame.Instance?.MainMenu==null;i++) await Frames(1);
            await Frames(180); Controller=Type("LiveSharingController"); Controller.GetMethod("Close",Any)!.Invoke(null,null);
            SaveManager.Instance.SetFtuesEnabled(false);
            var character=ModelDb.Character<Ironclad>();
            var player=Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
            var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPFEEDBACK7");
            player.AddRelicInternal(ModelDb.Relic<Toolbox>().ToMutable(),-1);
            player.Deck.Clear(true);
            player.Deck.AddInternal(state.CreateCard<BootSequence>(player),silent:true);
            for(int i=0;i<35;i++) player.Deck.AddInternal(state.CreateCard<DefendIronclad>(player),silent:true);
            RunManager.Instance.SetUpNewSingleplayer(state,true); await PreloadManager.LoadRunAssets(new[]{character}); RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state)); await RunManager.Instance.SetActInternal(0);
            state.Act.SetBossEncounter(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.AeonglassBoss>());
            await RunManager.Instance.EnterMapCoord(state.Map.BossMapPoint.coord);
            Controller.GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            await Until(()=>NOverlayStack.Instance?.Peek() is NChooseACardSelectionScreen && Verified,"Toolbox choice during boss initialization reaches matching usable native screens");
            var mirror=Static("_mirror")!; var process=Field(mirror,"Process")!;
            var view=Static("_view")!; Call(view,"SetControlEnabled",true);
            nint window=(nint)(long)Field(process,"Window")!;
            string profile=(string)Field(process,"DirectoryPath")!;
            System.Text.Json.JsonElement ChildStatus()
            {
                using var stream=new FileStream(Path.Combine(profile,"feedback-state.json"),FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                using var json=System.Text.Json.JsonDocument.Parse(stream); return json.RootElement.Clone();
            }
            await Until(()=>File.Exists(Path.Combine(profile,"feedback-state.json")) && ChildStatus().GetProperty("TransitionClear").GetBoolean(),"initial replica has every native transition cleared before first source choice");
            System.Collections.IEnumerable Hits() => (System.Collections.IEnumerable)Field(process,"LastHits")!;
            void Send(uint message,float x,float y)
            {
                GetClientRect(window,out var size); int px=(int)(x*size.Right),py=(int)(y*size.Bottom);
                Check(SendMessageTimeoutW(window,message,message==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,1000,out _)!=0,"feedback native message dispatched");
            }
            void Wheel(float x,float y,int ticks,bool owner = false)
            {
                GetClientRect(window,out var size); var point=new NativePoint{X=(int)(x*size.Right),Y=(int)(y*size.Bottom)}; ClientToScreen(window,ref point);
                var recipient=owner ? (nint)Type("MirrorWin32").GetProperty("OwnWindow",Any)!.GetValue(null)! : window;
                Check(SendMessageTimeoutW(recipient,0x20A,(nuint)((ticks*120&65535)<<16),(nint)((point.Y<<16)|(point.X&65535)),2,1000,out _)!=0,"feedback wheel dispatched in OS screen coordinates");
            }
            async Task Click(string kind)
            {
                await Until(()=>Hits().Cast<object>().Any(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == kind),kind+" native hit available");
                var h=Hits().Cast<object>().Last(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == kind);
                var r=(float[])h.GetType().GetProperty("Rect")!.GetValue(h)!;
                Send(0x200,r[0]+r[2]/2,r[1]+r[3]/2); Send(0x201,r[0]+r[2]/2,r[1]+r[3]/2); await Frames(2); Send(0x202,r[0]+r[2]/2,r[1]+r[3]/2);
            }
            async Task KeyPress(uint key, bool owner=true)
            {
                var host=mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror)!;
                if(owner) Check((bool)Call(host,"RouteKeyboard",0x100u,(nuint)key,(nint)1,true)!,"key over replica routes to child");
                else Check(SendMessageTimeoutW(window,0x100,(nuint)key,1,2,1000,out _)!=0,"child key down dispatched");
                await Frames(2);
                if(owner) Check((bool)Call(host,"RouteKeyboard",0x101u,(nuint)key,unchecked((nint)0xC0000001),false)!,"replica key release stays with child after pointer leaves");
                else Check(SendMessageTimeoutW(window,0x101,(nuint)key,unchecked((nint)0xC0000001),2,1000,out _)!=0,"child key up dispatched");
                await Frames(15);
            }
            uint Shortcut(string action)
            {
                var key=NInputManager.Instance.GetMKbHotkey(action);
                Check(key!=Key.None,"native shortcut is registered: "+action);
                return key>=Key.F1 && key<=Key.F35 ? (uint)(0x70+(key-Key.F1)) : key==Key.Enter?13u : (uint)key;
            }
            var refresh=(Button)Field(view,"_refresh")!;
            var keyboardHost=mirror.GetType().GetProperty("Window",Any)!.GetValue(mirror)!;
            Check(!(bool)Call(keyboardHost,"RouteKeyboard",0x100u,(nuint)90,(nint)1,false)!,"source-area key stays in source");
            Check(!(bool)Call(keyboardHost,"RouteKeyboard",0x100u,(nuint)90,(nint)(1L<<30|1),true)!,"source-held key repeat keeps original owner across pointer boundary");
            Check(!(bool)Call(keyboardHost,"RouteKeyboard",0x101u,(nuint)90,unchecked((nint)0xC0000001),true)!,"source-held key release reaches source after pointer enters replica");
            var toggle=(Button)Field(view,"_controlToggle")!;
            Check(Math.Abs(refresh.Position.Y+refresh.Size.Y*refresh.Scale.Y/2-toggle.Position.Y-toggle.Size.Y*toggle.Scale.Y/2)<1,"rebuild control shares the watch/control vertical center");
            refresh.EmitSignal(Control.SignalName.MouseEntered); await Frames(2);
            var hint=refresh.GetParent().GetNode<Label>("RebuildMirrorHint");
            var content=(Control)Field(view,"_content")!;
            Check(hint.Visible && hint.Position.Y+hint.Size.Y<=content.Position.Y,"rebuild hint remains above native game content");
            await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using(var pixels=NGame.Instance.GetViewport().GetTexture().GetImage()) pixels.SavePng(Path.Combine(profile,"visual-rebuild-header.png"));
            refresh.EmitSignal(Control.SignalName.MouseExited);
            await Frames(120); await Click("select");
            await Until(()=>NOverlayStack.Instance?.Peek()==null && player.PlayerCombatState?.Hand.Cards.Count>0 && Verified,"Toolbox native selection resolves and boss combat continues");
            await Frames(45);
            var sl=Controller.Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")!;
            await KeyPress(Shortcut("rmpQuickSl"));
            await Until(()=>ChildStatus().GetProperty("QuickSlOpen").GetBoolean(),"F5 over replica opens confirmation in replica");
            Check(!(bool)sl.GetProperty("ConfirmationOpen",Any)!.GetValue(null)! && NModalContainer.Instance?.OpenModal==null,"replica F5 leaves source confirmation closed");
            await KeyPress(27);
            await Until(()=>!ChildStatus().GetProperty("QuickSlOpen").GetBoolean() && Verified,"Escape cancels replica F5 without source action");
            await KeyPress(27);
            await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="NPauseMenu","Escape opens replica pause menu");
            await Click("ui.settings");
            await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="NSettingsScreen","replica native settings open");
            await Frames(60);
            bool sourceTimer=SaveManager.Instance.PrefsSave.ShowRunTimer;
            var timer=ChildStatus().GetProperty("SettingsControls").EnumerateArray().Single(c=>c.GetProperty("Type").GetString()=="NRunTimerTickbox");
            var timerRect=timer.GetProperty("Rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            bool childTimer=ChildStatus().GetProperty("ShowRunTimer").GetBoolean();
            Send(0x200,timerRect[0]+timerRect[2]/2,timerRect[1]+timerRect[3]/2); Send(0x201,timerRect[0]+timerRect[2]/2,timerRect[1]+timerRect[3]/2); await Frames(2); Send(0x202,timerRect[0]+timerRect[2]/2,timerRect[1]+timerRect[3]/2);
            await Until(()=>ChildStatus().GetProperty("ShowRunTimer").GetBoolean()!=childTimer,"replica settings checkbox changes replica preference");
            Check(SaveManager.Instance.PrefsSave.ShowRunTimer==sourceTimer,"changing replica preference leaves source preference unchanged");
            var settingsStatus=ChildStatus(); var settingsRect=settingsStatus.GetProperty("ScrollRect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            float settingsOffset=settingsStatus.GetProperty("Offset").GetSingle();
            for(int i=0;i<3;i++) Wheel(settingsRect[0]+settingsRect[2]/2,settingsRect[1]+settingsRect[3]/2,-1,true);
            await Until(()=>ChildStatus().GetProperty("Offset").GetSingle()<settingsOffset-80,"owner-directed settings wheel scrolls original settings container");
            Check(!ReplicaSettingsVisible() && SaveManager.Instance.SettingsSave.VolumeMaster==0,"replica settings scroll leaves source settings closed and unchanged");
            await KeyPress(27);
            await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="NPauseMenu","settings Escape returns to replica pause");
            await KeyPress(27);
            await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="" && Verified,"keyboard returns to combat without source action");
            await Until(()=>ChildStatus().GetProperty("CardInputReady").GetBoolean() && Verified,"native card input is ready for keyboard fixture");
            int keyboardHand=player.PlayerCombatState!.Hand.Cards.Count;
            int bootIndex=NPlayerHand.Instance!.ActiveHolders.ToList().FindIndex(h=>h.CardModel is BootSequence);
            await KeyPress(Shortcut("mega_select_card_"+(bootIndex+1))); await Frames(30); await KeyPress(13);
            await Until(()=>Verified && player.PlayerCombatState!.Hand.Cards.Count==keyboardHand-1 && player.PlayerCombatState.ExhaustPile.Cards.Any(c=>c is BootSequence),"numeric zero-cost card shortcut and Enter execute once on authority and converge");
            await Frames(90);
            await Frames(50);
            Check(player.PlayerCombatState!.Hand.Cards.Count(c=>c is DefendIronclad)>=3,"isolated fixture has three distinct defend cards for rapid input");
            int handBefore=player.PlayerCombatState.Hand.Cards.Count;
            var clicked=new System.Collections.Generic.HashSet<int>();
            int blocksBefore=player.Creature.Block;
            for(int rapid=0;rapid<3;rapid++)
            {
                // Re-read animated geometry, then release the next card while the
                // preceding card can still be awaiting authority or native VFX.
                System.Text.Json.JsonElement hit=default;
                await Until(()=>ChildStatus().GetProperty("Hand").EnumerateArray().Any(h=>h.GetProperty("Id").GetString()=="CARD.DEFEND_IRONCLAD" && !clicked.Contains(h.GetProperty("Key").GetInt32())),"next rapid card identity visible even while replay is busy");
                hit=ChildStatus().GetProperty("Hand").EnumerateArray().Where(h=>h.GetProperty("Id").GetString()=="CARD.DEFEND_IRONCLAD" && !clicked.Contains(h.GetProperty("Key").GetInt32()))
                    .OrderBy(h=>h.GetProperty("Exhaust").GetBoolean()).Last();
                clicked.Add(hit.GetProperty("Key").GetInt32());
                var r=hit.GetProperty("Rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                Send(0x200,r[0]+r[2]/2,r[1]+r[3]*.2f); Send(0x201,r[0]+r[2]/2,r[1]+r[3]*.2f);
                Send(0x200,.5f,.35f); await Frames(2); Send(0x202,.5f,.35f); await Frames(3);
            }
            await Until(()=>Verified && player.PlayerCombatState!.Hand.Cards.Count==handBefore-3,"three rapid native card releases all reach authority and converge");
            Check(player.Creature.Block>=blocksBefore+15,"rapid cards produce all three original block results");
            await Until(()=>Verified && player.PlayerCombatState!.ExhaustPile.Cards.Count>0 && player.PlayerCombatState.DiscardPile.Cards.Count>=3,"original zero-cost innate boot sequence prepares nonempty exhaust and discard piles");
            await Click("ui.deck"); await Frames(100);
            await Until(()=>File.Exists(Path.Combine(profile,"feedback-state.json")) && ChildStatus().GetProperty("Screen").GetString()=="NDeckViewScreen","replica deck opens locally");
            var status=ChildStatus(); float before=status.GetProperty("Offset").GetSingle();
            var sr=status.GetProperty("ScrollRect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            Call(view,"SetControlEnabled",false); await Frames(15);
            for(int i=0;i<6;i++) Wheel(sr[0]+sr[2]/2,sr[1]+sr[3]/2,-1);
            await Until(()=>ChildStatus().GetProperty("Offset").GetSingle()<before-150,"watch mode deck wheel retains all six ticks without authority input");
            status=ChildStatus(); Check(status.GetProperty("NoticeY").GetSingle()>=status.GetProperty("ViewHeight").GetSingle()*.75f,"loading notice lies at bottom center");
            Call(view,"SetControlEnabled",true); await Frames(20);
            await Click("ui.return");
            await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="" && Verified,"deck return preserves native combat state");
            var journal=Type("MirrorJournal");
            var operations=(System.Collections.IList)journal.GetField("Operations",Any)!.GetValue(null)!;
            int browsingBefore=operations.Count;
            foreach(var pile in new[]{"draw","discard","exhaust"})
            {
                foreach(var control in new[]{false,true})
                {
                    Call(view,"SetControlEnabled",control); await Frames(15);
                    await Click("ui."+pile);
                    await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="NCardPileScreen","replica "+pile+" pile opens locally in "+(control?"control":"watch")+" mode");
                    Check(NCapstoneContainer.Instance?.CurrentCapstoneScreen==null && operations.Count==browsingBefore,"replica pile browse leaves source screen and journal unchanged");
                    await Frames(45); await Click("ui.return");
                    await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="" && Verified,"replica pile return is ready without source card play");
                }
            }
            await Click("ui.deck"); await Frames(90);
            status=ChildStatus(); before=status.GetProperty("Offset").GetSingle();
            sr=status.GetProperty("ScrollRect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            for(int i=0;i<6;i++) Wheel(sr[0]+sr[2]/2,sr[1]+sr[3]/2,-1,true);
            await Until(()=>ChildStatus().GetProperty("Offset").GetSingle()<before-300,"owner-directed wheel forwards every tick with reduced-window distance compensation");
            Check(NCapstoneContainer.Instance?.CurrentCapstoneScreen==null && operations.Count==browsingBefore,"owner-directed deck wheel cannot scroll or open source UI");
            await Click("ui.return");
            await Until(()=>ChildStatus().GetProperty("Screen").GetString()=="" && Verified,"deck return after owner wheel needs no source action");
            Descendants<NDrawPileButton>(NCombatRoom.Instance).Single().ForceClick();
            await Until(()=>NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen,"source draw pile opens without replica presentation");
            await Frames(40);
            var sourcePile=(Node)NCapstoneContainer.Instance.CurrentCapstoneScreen!;
            var sourceGrid=Descendants<NCardGrid>(sourcePile).First();
            Type("LocalSpectatorSource").GetMethod("ScrollNative",Any)!.Invoke(null,new object[]{sourceGrid,"down:3"});
            Descendants<NBackButton>(sourcePile).Single().ForceClick();
            await Until(()=>NCapstoneContainer.Instance.CurrentCapstoneScreen==null && Verified,"source draw pile back cannot stall an absent replica screen");
            NRun.Instance.GlobalUi.TopBar.Deck.ForceClick(); await Frames(45);
            var sourceDeck=(Node)NCapstoneContainer.Instance.CurrentCapstoneScreen!;
            Descendants<NBackButton>(sourceDeck).Single().ForceClick(); await Frames(40);
            Check(operations.Count==browsingBefore,"source pile scrolling/back and deck browsing add no gameplay operations");
            int turnBefore=player.PlayerCombatState!.TurnNumber;
            await Click("endTurn");
            await Until(()=>Verified && player.PlayerCombatState!.TurnNumber>turnBefore,"window end turn after pile return works without any intervening source action");
            await Until(()=>Verified && ChildStatus().GetProperty("CardInputReady").GetBoolean(),"native card input is ready after turn transition");
            for(int repeat=0;repeat<2;repeat++)
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                if(repeat==0)
                {
                    var previous=RunManager.Instance.DebugOnlyGetState();
                    await KeyPress(Shortcut("rmpQuickSl"));
                    await Until(()=>ChildStatus().GetProperty("QuickSlOpen").GetBoolean(),"replica F5 offers local confirmation before SL");
                    await KeyPress(13);
                    await Until(()=>!ReferenceEquals(previous,RunManager.Instance.DebugOnlyGetState()) && !(bool)sl.GetField("_operationRunning",Any)!.GetValue(null)!,"replica confirmation performs one authority SL without a source popup");
                    Check(!(bool)sl.GetProperty("ConfirmationOpen",Any)!.GetValue(null)!,"confirmed replica SL never opens duplicate source confirmation");
                }
                else
                {
                    var task=(Task)sl.GetMethod("RunSingleplayerSlAsync",Any)!.Invoke(null,null)!;
                    await Until(()=>task.IsCompleted,"boss SL returns while Toolbox choice awaits user"); await task;
                }
                state=RunManager.Instance.DebugOnlyGetState()!; player=state.Players[0];
                Check(!NGame.Instance.Transition.InTransition && NGame.Instance.Transition.MouseFilter==Control.MouseFilterEnum.Ignore,"boss SL removes blocking transition");
                Check(NGame.Instance.Transition.GetNode<Control>("SimpleTransition").Modulate.A<.01f,"boss SL leaves simple transition transparent");
                Check(NGame.Instance.Transition.GetNode<Control>("GradientTransition").Modulate.A<.01f,"boss SL leaves gradient transition transparent");
                GD.Print("[MultiInstanceSmoke] FEEDBACK boss SL "+repeat+" source="+clock.Elapsed.TotalMilliseconds+"ms");
                await Until(()=>Static("_view")!=null && Verified && NOverlayStack.Instance?.Peek() is NChooseACardSelectionScreen,"boss SL replica choice reconverges");
                view=Static("_view")!; Call(view,"SetControlEnabled",true); await Frames(90); await Click("select");
                await Until(()=>NOverlayStack.Instance?.Peek()==null && Verified,"post-SL Toolbox choice remains interactive");
            }
            await Frames(45);
            mirror=Static("_mirror")!; process=Field(mirror,"Process")!; view=Static("_view")!;
            var checkpoint=journal.GetProperty("Checkpoint",Any)!.GetValue(null);
            int recoveryEvents=operations.Count;
            var recoveryState=RunManager.Instance.DebugOnlyGetState()!;
            var sourceHash=(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{recoveryState})!;
            var child=(System.Diagnostics.Process)process.GetType().GetProperty("Child",Any)!.GetValue(process)!;
            int failedPid=child.Id; child.Kill();
            for(int frame=0;frame<180 && (string)mirror.GetType().GetProperty("Error",Any)!.GetValue(mirror)! == "";frame++) await Frames(1);
            Check((string)mirror.GetType().GetProperty("Error",Any)!.GetValue(mirror)! != "","owned replica exit suspends control with a reported error");
            await Frames(12);
            var retry=(Button)Field(view,"_retry")!;
            Check(retry.Visible,"sync failure exposes resync button");
            retry.EmitSignal(Button.SignalName.Pressed);
            await Until(()=>Verified,"resync button reconstructs failed replica from unchanged source checkpoint");
            process=Field(Static("_mirror")!,"Process")!;
            child=(System.Diagnostics.Process)process.GetType().GetProperty("Child",Any)!.GetValue(process)!;
            Check(child.Id!=failedPid,"resync replaces only the failed owned child process");
            Check(ReferenceEquals(checkpoint,journal.GetProperty("Checkpoint",Any)!.GetValue(null)) && recoveryEvents==operations.Count &&
                sourceHash==(string)Type("MirrorState").GetMethod("Hash",Any)!.Invoke(null,new object[]{recoveryState})!,"resync preserves source state, RNG, checkpoint and operation journal");
            Controller.GetMethod("Suspend",Any)!.Invoke(null,null);
            GD.Print("[MultiInstanceSmoke] FEEDBACK PASSED"); ((SceneTree)Engine.GetMainLoop()).Quit();
        }
        catch(Exception e) { GD.PrintErr("[MultiInstanceSmoke] FEEDBACK FAIL "+e); Controller?.GetMethod("Suspend",Any)?.Invoke(null,null); ((SceneTree)Engine.GetMainLoop()).Quit(1); }
    }
}
