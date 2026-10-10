using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment=System.Environment;

[ModInitializer(nameof(Initialize))]
public static class RewardHoverProbe
{
    private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static Type Rmp(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing."+name)).First(t=>t!=null)!;
    private static object? Field(object target,string name)=>target.GetType().GetField(name,Any)!.GetValue(target);
    private static object? Static(string name)=>Rmp("LiveSharingController").GetField(name,Any)!.GetValue(null);
    private static object Mirror=>Static("_mirror")!;
    private static object Process=>Field(Mirror,"Process")!;
    private static bool Verified=>Static("_mirror") is {} m&&(bool)m.GetType().GetProperty("Verified",Any)!.GetValue(m)!;
    private static bool Renderer=>Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")=="renderer";
    private static string Output=>Renderer?Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!:Environment.GetEnvironmentVariable("RMP_HOVER_EVIDENCE")!;
    private static IEnumerable<T> Desc<T>(Node root) where T:Node {foreach(var c in root.GetChildren()){if(c is T item)yield return item;foreach(var nested in Desc<T>(c))yield return nested;}}
    private static NRewardButton[] Buttons=>NOverlayStack.Instance?.Peek() is NRewardsScreen screen?Desc<NRewardButton>(screen).Where(b=>b.IsVisibleInTree()&&!b.IsQueuedForDeletion()).OrderBy(b=>b.GlobalPosition.Y).ToArray():Array.Empty<NRewardButton>();
    private static readonly FieldInfo Hover=typeof(NClickableControl).GetField("_isHovered",Any)!;
    private static bool Focused(NClickableControl button)=>(bool)typeof(NClickableControl).GetProperty("IsFocused",Any)!.GetValue(button)!;
    private static async Task Frames(int count){var tree=(SceneTree)Engine.GetMainLoop();for(int i=0;i<count;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    private static async Task Pause(double seconds){var end=Time.GetTicksMsec()+seconds*1000;while(Time.GetTicksMsec()<end)await Frames(1);}
    private static async Task Wait(Func<bool> condition,string label,int seconds=60){var end=Time.GetTicksMsec()+(ulong)seconds*1000;while(Time.GetTicksMsec()<end){if(condition())return;await Frames(1);}throw new Exception("Timeout: "+label);}
    private static string Key(Node node)=>(string)Rmp("MirrorNativeUi").GetMethod("Key",Any)!.Invoke(null,new[]{node})!;
    private static float[] Rect(Control node){var r=node.GetGlobalRect();var size=NGame.Instance.GetViewport().GetVisibleRect().Size;return new[]{r.Position.X/size.X,r.Position.Y/size.Y,r.Size.X/size.X,r.Size.Y/size.Y};}
    private static object State()=>new{buttons=Buttons.Select(b=>new{kind=b.Reward!.GetType().Name,key=Key(b),hovered=(bool)Hover.GetValue(b)!,focused=Focused(b),rect=Rect(b),shader=((ShaderMaterial)((TextureRect)Field(b,"_background")!).Material).GetShaderParameter("v").AsSingle()}).ToArray()};
    private static async Task Fixture()
    {
        var state=RunManager.Instance.DebugOnlyGetState()!;var player=state.Players[0];
        var options=new CardCreationOptions(new[]{player.Character.CardPool},CardCreationSource.Other,CardRarityOddsType.Uniform);
        var card=new CardReward(options,3,player);
        await RewardsCmd.OfferCustom(player,new List<Reward>{new GoldReward(10,player),new PotionReward(ModelDb.Potion<FirePotion>().ToMutable(),player),card});
    }
    public static void Initialize(){if(!Renderer&&Environment.GetEnvironmentVariable("RMP_MULTI_HOVER_TEST")!="1"||Renderer&&Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")!="1")return;TaskHelper.RunSafely(Observe());if(!Renderer)TaskHelper.RunSafely(Run());}
    private static async Task Observe()
    {
        bool started=false;while(true){await Frames(12);if(NGame.Instance==null||!Directory.Exists(Output))continue;
            if(Renderer&&!started&&File.Exists(Path.Combine(Output,"hover-fixture.txt"))&&RunManager.Instance.DebugOnlyGetState()!=null){started=true;TaskHelper.RunSafely(Fixture());}
            try{string file=Path.Combine(Output,"hover-state.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(State()));File.Move(file+".tmp",file,true);
                var request=Path.Combine(Output,"hover-capture.txt");if(Renderer&&File.Exists(request)){string label=File.ReadAllText(request);File.Delete(request);await Image(label);}}
            catch(Exception e){GD.Print("[RewardHover] observer "+e.GetType().Name);}
        }
    }
    private static string ChildRoot=>(string)Field(Process,"DirectoryPath")!;
    private static JsonElement Child(){try{using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(ChildRoot,"hover-state.json")));return json.RootElement.Clone();}catch{return default;}}
    private static JsonElement[] ChildButtons()=>Child().ValueKind==JsonValueKind.Object?Child().GetProperty("buttons").EnumerateArray().ToArray():Array.Empty<JsonElement>();
    private static async Task Image(string name){await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Output,name+".png"));}
    private static async Task Capture(string label){await Image("source-"+label);File.WriteAllText(Path.Combine(ChildRoot,"hover-capture.txt"),"replica-"+label);File.WriteAllText(Path.Combine(Output,label+".json"),JsonSerializer.Serialize(new{source=State(),replica=Child(),verified=Verified}));await Pause(1);}
    [StructLayout(LayoutKind.Sequential)] private struct WinRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetClientRect(nint hwnd,out WinRect rect);
    [DllImport("user32.dll")]private static extern nint SendMessageTimeoutW(nint hwnd,uint msg,nuint key,nint data,uint flags,uint timeout,out nuint result);
    private static void Send(uint msg,float x,float y){nint hwnd=(nint)(long)Field(Process,"Window")!;GetClientRect(hwnd,out var r);int px=(int)(x*r.Right),py=(int)(y*r.Bottom);if(SendMessageTimeoutW(hwnd,msg,msg==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,5000,out _)==0)throw new Exception("Owned message failed");}
    private static void SourcePointer(Vector2 point){using var motion=new InputEventMouseMotion{Position=point,GlobalPosition=point,Relative=Vector2.Zero};NGame.Instance.GetViewport().PushInput(motion,true);}
    private static Label? _caption;
    private static void Stage(string text){GD.Print("[RewardHover] "+text);if(_caption!=null)_caption.Text="搜刮悬停专项 · "+text;}
    private static async Task Run()
    {
        int exit=0;try{
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Pause(2);Rmp("LiveSharingController").GetMethod("Close",Any)!.Invoke(null,null);SaveManager.Instance.SetFtuesEnabled(false);
            var canvas=new CanvasLayer{Layer=120};NGame.Instance.AddChild(canvas);_caption=new Label{Position=new Vector2(20,75),MouseFilter=Control.MouseFilterEnum.Ignore};_caption.AddThemeFontSizeOverride("font_size",26);_caption.AddThemeColorOverride("font_outline_color",Colors.Black);_caption.AddThemeConstantOverride("outline_size",5);canvas.AddChild(_caption);
            var character=ModelDb.Character<Ironclad>();var state=RunState.CreateForNewRun(new[]{Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1)},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPHOVER12");
            RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
            await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.RestSite).coord);
            Rmp("LiveSharingController").GetMethod("Open",Any)!.Invoke(null,new object[]{state});var view=Static("_view")!;view.GetType().GetMethod("SetControlEnabled",Any)!.Invoke(view,new object[]{true});((Control)Field(view,"_panel")!).Position=new Vector2(1130,160);
            await Wait(()=>Verified,"initial synchronization",90);File.WriteAllText(Path.Combine(ChildRoot,"hover-fixture.txt"),"1");TaskHelper.RunSafely(Fixture());await Wait(()=>Buttons.Length==3&&ChildButtons().Length==3&&Verified,"three original rewards",45);await Pause(3);
            var r=ChildButtons()[0].GetProperty("rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();float x=r[0]+r[2]/2,y=r[1]+r[3]/2;var sourceRect=Buttons[0].GetGlobalRect();var sourcePoint=sourceRect.GetCenter();
            SourcePointer(sourcePoint);Send(0x200,x,y);await Pause(2);await Capture("gold-hover");
            if(!ChildButtons()[0].GetProperty("hovered").GetBoolean())throw new Exception("Initial native hover missing");
            Stage("鼠标保持不动：领取金币 → 药水上移");Send(0x201,x,y);await Frames(2);Send(0x202,x,y);await Wait(()=>Buttons.Length==2&&ChildButtons().Length==2&&Verified,"gold claimed");await Pause(2);await Capture("potion-stationary");
            bool potionHover=ChildButtons()[0].GetProperty("hovered").GetBoolean()&&ChildButtons()[0].GetProperty("focused").GetBoolean();
            Stage("仍不移动鼠标：领取药水 → 卡牌上移");Send(0x201,x,y);await Frames(2);Send(0x202,x,y);await Wait(()=>Buttons.Length==1&&ChildButtons().Length==1&&Verified,"potion claimed");await Pause(2);await Capture("card-stationary");
            bool cardHover=ChildButtons()[0].GetProperty("hovered").GetBoolean()&&ChildButtons()[0].GetProperty("focused").GetBoolean();
            Stage("移动一次鼠标，比较是否恢复黄色选中框");Send(0x200,x+.003f,y);await Pause(2);await Capture("card-after-move");bool afterMove=ChildButtons()[0].GetProperty("hovered").GetBoolean()&&ChildButtons()[0].GetProperty("focused").GetBoolean();
            Send(0x200,.75f,.75f);await Pause(2);await Capture("pointer-outside");bool outsideCleared=!ChildButtons()[0].GetProperty("hovered").GetBoolean()&&!ChildButtons()[0].GetProperty("focused").GetBoolean();
            bool passed=potionHover&&cardHover&&afterMove&&outsideCleared&&Verified;
            File.WriteAllText(Path.Combine(Output,"result.json"),JsonSerializer.Serialize(new{potionHover,cardHover,afterMove,outsideCleared,verified=Verified,passed}));
            if(Environment.GetEnvironmentVariable("RMP_HOVER_EXPECT_FIXED")=="1"&&!passed)throw new Exception("Stationary hover assertions failed");
            Stage("RESULT potion="+potionHover+" card="+cardHover+" afterMove="+afterMove+"；保留画面 20 秒");
        }catch(Exception e){exit=1;Stage("FAIL "+e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
        await Pause(20);Rmp("LiveSharingController").GetMethod("Suspend",Any)!.Invoke(null,null);((SceneTree)Engine.GetMainLoop()).Quit(exit);
    }
}
