using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
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
            var root = NCapstoneContainer.Instance?.CurrentCapstoneScreen as Node;
            var scroll = root == null ? null : Descendants<Control>(root).FirstOrDefault(n => n is NCardGrid or NScrollableContainer);
            float offset = scroll == null ? 0 : (float)Field(scroll, scroll is NCardGrid ? "_targetDrag" : "_targetDragPosY")!;
            var renderer = Type("MirrorRenderer").GetField("Active", Any)!.GetValue(null);
            var notice = renderer == null ? null : Field(renderer, "_notice") as Label;
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!, "feedback-state.json"),
                System.Text.Json.JsonSerializer.Serialize(new { Screen = root?.GetType().Name ?? "", Offset = offset,
                    ScrollRect = scroll == null ? new float[4] : Normalized(scroll),
                    Hand = NPlayerHand.Instance?.ActiveHolders.Where(h => h.CardModel != null && h.IsVisibleInTree() && renderer != null && !(bool)Call(renderer,"IsCardReserved",h.CardModel)!).OrderBy(h => h.ZIndex)
                        .Select(h => new { Key = h.CardModel!.GetHashCode(), Id = h.CardModel.Id.ToString(), Rect = Normalized(h.Hitbox) }).ToArray(),
                    NoticeY = notice?.Position.Y ?? 0, NoticeHeight = notice?.Size.Y ?? 0,
                    ViewHeight = NGame.Instance.GetViewport().GetVisibleRect().Size.Y,
                    MapY = NMapScreen.Instance == null ? 0 : ((Vector2)Field(NMapScreen.Instance,"_targetDragPos")!).Y,
                    MapCanScroll = NMapScreen.Instance != null && (bool)Call(NMapScreen.Instance,"CanScroll")! }));
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
        var size=DisplayServer.WindowGetSize();
        return new[]{rect.Position.X/size.X,rect.Position.Y/size.Y,rect.Size.X/size.X,rect.Size.Y/size.Y};
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
            System.Collections.IEnumerable Hits() => (System.Collections.IEnumerable)Field(process,"LastHits")!;
            void Send(uint message,float x,float y)
            {
                GetClientRect(window,out var size); int px=(int)(x*size.Right),py=(int)(y*size.Bottom);
                Check(SendMessageTimeoutW(window,message,message==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,1000,out _)!=0,"feedback native message dispatched");
            }
            void Wheel(float x,float y,int ticks)
            {
                GetClientRect(window,out var size); var point=new NativePoint{X=(int)(x*size.Right),Y=(int)(y*size.Bottom)}; ClientToScreen(window,ref point);
                Check(SendMessageTimeoutW(window,0x20A,(nuint)((ticks*120&65535)<<16),(nint)((point.Y<<16)|(point.X&65535)),2,1000,out _)!=0,"feedback wheel dispatched in OS screen coordinates");
            }
            async Task Click(string kind)
            {
                await Until(()=>Hits().Cast<object>().Any(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == kind),kind+" native hit available");
                var h=Hits().Cast<object>().Last(h=>(string)h.GetType().GetProperty("Kind")!.GetValue(h)! == kind);
                var r=(float[])h.GetType().GetProperty("Rect")!.GetValue(h)!;
                Send(0x200,r[0]+r[2]/2,r[1]+r[3]/2); Send(0x201,r[0]+r[2]/2,r[1]+r[3]/2); await Frames(2); Send(0x202,r[0]+r[2]/2,r[1]+r[3]/2);
            }
            await Frames(120); await Click("select");
            await Until(()=>NOverlayStack.Instance?.Peek()==null && player.PlayerCombatState?.Hand.Cards.Count>0 && Verified,"Toolbox native selection resolves and boss combat continues");
            await Frames(90);
            await Click("ui.deck"); await Frames(100);
            System.Text.Json.JsonElement ChildStatus()
            {
                using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(profile,"feedback-state.json"))); return json.RootElement.Clone();
            }
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
                hit=ChildStatus().GetProperty("Hand").EnumerateArray().Last(h=>h.GetProperty("Id").GetString()=="CARD.DEFEND_IRONCLAD" && !clicked.Contains(h.GetProperty("Key").GetInt32()));
                clicked.Add(hit.GetProperty("Key").GetInt32());
                var r=hit.GetProperty("Rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                Send(0x200,r[0]+r[2]/2,r[1]+r[3]*.2f); Send(0x201,r[0]+r[2]/2,r[1]+r[3]*.2f);
                Send(0x200,.5f,.35f); await Frames(2); Send(0x202,.5f,.35f); await Frames(3);
            }
            await Until(()=>Verified && player.PlayerCombatState!.Hand.Cards.Count==handBefore-3,"three rapid native card releases all reach authority and converge");
            Check(player.Creature.Block>=blocksBefore+15,"rapid cards produce all three original block results");
            var sl=Controller.Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")!;
            for(int repeat=0;repeat<2;repeat++)
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var task=(Task)sl.GetMethod("RunSingleplayerSlAsync",Any)!.Invoke(null,null)!;
                await Until(()=>task.IsCompleted,"boss SL returns while Toolbox choice awaits user"); await task;
                state=RunManager.Instance.DebugOnlyGetState()!; player=state.Players[0];
                Check(!NGame.Instance.Transition.InTransition && NGame.Instance.Transition.MouseFilter==Control.MouseFilterEnum.Ignore,"boss SL removes blocking transition");
                Check(NGame.Instance.Transition.GetNode<Control>("SimpleTransition").Modulate.A<.01f,"boss SL leaves simple transition transparent");
                Check(NGame.Instance.Transition.GetNode<Control>("GradientTransition").Modulate.A<.01f,"boss SL leaves gradient transition transparent");
                GD.Print("[MultiInstanceSmoke] FEEDBACK boss SL "+repeat+" source="+clock.Elapsed.TotalMilliseconds+"ms");
                await Until(()=>Static("_view")!=null && Verified && NOverlayStack.Instance?.Peek() is NChooseACardSelectionScreen,"boss SL replica choice reconverges");
                view=Static("_view")!; Call(view,"SetControlEnabled",true); await Frames(90); await Click("select");
                await Until(()=>NOverlayStack.Instance?.Peek()==null && Verified,"post-SL Toolbox choice remains interactive");
            }
            Controller.GetMethod("Suspend",Any)!.Invoke(null,null);
            GD.Print("[MultiInstanceSmoke] FEEDBACK PASSED"); ((SceneTree)Engine.GetMainLoop()).Quit();
        }
        catch(Exception e) { GD.PrintErr("[MultiInstanceSmoke] FEEDBACK FAIL "+e); Controller?.GetMethod("Suspend",Any)?.Invoke(null,null); ((SceneTree)Engine.GetMainLoop()).Quit(1); }
    }
}
