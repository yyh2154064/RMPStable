using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment = System.Environment;

[ModInitializer(nameof(Initialize))]
public static class CrystalFogProbe
{
    private const BindingFlags Any = BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static Type Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing."+name)).First(t=>t!=null)!;
    private static object? Field(object target,string name) => target.GetType().GetField(name,Any)?.GetValue(target) ?? target.GetType().GetProperty(name,Any)?.GetValue(target);
    private static object? Static(string name) => Rmp("LiveSharingController").GetField(name,Any)!.GetValue(null);
    private static object? Call(object target,string name,params object?[] args) => target.GetType().GetMethod(name,Any)!.Invoke(target,args);
    private static bool Renderer => Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")=="renderer";
    private static object Mirror => Static("_mirror")!;
    private static object Process => Field(Mirror,"Process")!;
    private static bool Verified => Static("_mirror") is { } mirror && (bool)mirror.GetType().GetProperty("Verified",Any)!.GetValue(mirror)!;
    private static string Output => Renderer ? Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")! : Environment.GetEnvironmentVariable("RMP_FOG_EVIDENCE")!;
    private static string _stage="启动占卜专项测试";
    private static Label? _caption;
    private static int _failures;
    private static readonly List<string> Findings=new();
    public static void Initialize()
    {
        if (!Renderer && Environment.GetEnvironmentVariable("RMP_CRYSTAL_FOG_TEST")!="1" || Renderer && Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")!="1") return;
        // Fixture only: make the next question-mark room this specific event in
        // both processes. The original room roll still consumes its normal RNG.
        var harmony=new Harmony("RMP.CrystalFog.FocusedFixture");
        harmony.Patch(typeof(RunManager).GetMethod("RollRoomTypeFor",Any)!,postfix:new HarmonyMethod(typeof(CrystalFogProbe).GetMethod(nameof(QuestionRoom),Any)!));
        harmony.Patch(typeof(ActModel).GetMethod("PullNextEvent",Any)!,prefix:new HarmonyMethod(typeof(CrystalFogProbe).GetMethod(nameof(PickEvent),Any)!));
        TaskHelper.RunSafely(Observe());
        if(!Renderer) TaskHelper.RunSafely(Run());
    }
    private static void QuestionRoom(MapPointType pointType,ref RoomType __result) { if(pointType==MapPointType.Unknown) __result=RoomType.Event; }
    private static bool PickEvent(ref EventModel __result) { __result=ModelDb.Event<CrystalSphere>(); return false; }
    private static IEnumerable<T> Desc<T>(Node root) where T:Node { foreach(var child in root.GetChildren()) {if(child is T value) yield return value; foreach(var nested in Desc<T>(child)) yield return nested;} }
    private static NCrystalSphereScreen? Screen => NOverlayStack.Instance==null ? null : Desc<NCrystalSphereScreen>(NOverlayStack.Instance).LastOrDefault(s=>s.IsVisibleInTree());
    private static CrystalSphereMinigame? Entity => Screen==null ? null : Field(Screen,"_entity") as CrystalSphereMinigame;
    private static async Task Frames(int n) {var tree=(SceneTree)Engine.GetMainLoop();for(int i=0;i<n;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    private static async Task Pause(double seconds) {var end=Time.GetTicksMsec()+seconds*1000;while(Time.GetTicksMsec()<end)await Frames(1);}
    private static void Stage(string text) {_stage=text;if(_caption!=null)_caption.Text="占卜专项 · 灰雾 F4 · "+text;GD.Print("[CrystalFogProbe] STAGE "+text);}
    private static void Check(bool ok,string label) {GD.Print("[CrystalFogProbe] "+(ok?"PASS ":"FAIL ")+label);if(!ok){_failures++;Findings.Add(label);}}
    private static async Task<bool> Wait(Func<bool> predicate,string label,int seconds=45)
    {
        var end=Time.GetTicksMsec()+(ulong)seconds*1000;
        while(Time.GetTicksMsec()<end){if(predicate())return true;await Frames(1);}
        Check(false,label+" (timeout)");return false;
    }
    private static string Key(Node node) => (string)Rmp("MirrorNativeUi").GetMethod("Key",Any)!.Invoke(null,new[]{node})!;
    private static float[] Rect(Control control) {var r=control.GetGlobalRect();var size=NGame.Instance.GetViewport().GetVisibleRect().Size;return new[]{r.Position.X/size.X,r.Position.Y/size.Y,r.Size.X/size.X,r.Size.Y/size.Y};}
    private static object Snapshot()
    {
        var state=RunManager.Instance.DebugOnlyGetState();var player=state?.Players.FirstOrDefault();var entity=Entity;
        string fog=entity==null?"":string.Join("",Enumerable.Range(0,121).Select(i=>entity.cells[i/11,i%11].IsHidden?"1":"0"));
        string board=entity==null?"":string.Join(";",entity.Items.Select(i=>i.GetType().Name+":"+i.Position+":"+i.Size));
        Node? root=NOverlayStack.Instance?.Peek() as Node ?? NRun.Instance?.EventRoom;
        var buttons=root==null?Array.Empty<object>():Desc<NClickableControl>(root).Where(c=>c.IsVisibleInTree()).Select(c=>(object)new {key=Key(c),name=c.Name.ToString(),type=c.GetType().Name,enabled=c.IsEnabled,rect=Rect(c)}).ToArray();
        var cards=root==null?Array.Empty<object>():Desc<NCardHolder>(root).Where(c=>c.CardModel!=null&&c.IsVisibleInTree()).Select(c=>(object)new {key=Key(c),id=c.CardModel!.Id.ToString(),rect=Rect(c.Hitbox)}).ToArray();
        return new {screen=root?.GetType().Name??"",map=NMapScreen.Instance?.IsOpen==true,count=entity?.DivinationCount??-1,tool=entity?.CrystalSphereTool.ToString()??"",fog,board,
            gold=player?.Gold??-1,deck=player==null?"":string.Join(";",player.Deck.Cards.Select(c=>c.Id+":"+c.CurrentUpgradeLevel)),buttons,cards,stage=_stage,preview=FogEnabled,mask=MaskState(),slPopup=QuickPopup};
    }
    private static async Task Observe()
    {
        while(true){await Frames(20);if(NGame.Instance==null||!Directory.Exists(Output))continue;
            try{var file=Path.Combine(Output,"crystal-state.json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(Snapshot()));File.Move(file+".tmp",file,true);
                if(Renderer && File.Exists(Path.Combine(Output,"capture-request.txt"))) {var name=File.ReadAllText(Path.Combine(Output,"capture-request.txt"));File.Delete(Path.Combine(Output,"capture-request.txt"));await SaveImage(name);}}
            catch(Exception ex){GD.Print("[CrystalFogProbe] observer "+ex.GetType().Name);}
        }
    }
    private static string ChildRoot => (string)Field(Process,"DirectoryPath")!;
    private static JsonElement ChildState() {try{using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(ChildRoot,"crystal-state.json")));return doc.RootElement.Clone();}catch{return default;}}
    private static JsonElement SourceState() => JsonSerializer.SerializeToElement(Snapshot());
    private static bool Same(string property) {var child=ChildState();return child.ValueKind==JsonValueKind.Object && child.GetProperty(property).GetRawText()==SourceState().GetProperty(property).GetRawText();}
    private static async Task SaveImage(string name) {await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Output,name+".png"));}
    private static async Task Capture(string label) {await SaveImage("source-"+label);File.WriteAllText(Path.Combine(ChildRoot,"capture-request.txt"),"replica-"+label);File.WriteAllText(Path.Combine(Output,label+"-comparison.json"),JsonSerializer.Serialize(new{source=SourceState(),replica=ChildState(),verified=Verified,phase=Field(Process,"Phase"),mirrorError=Mirror.GetType().GetProperty("Error",Any)!.GetValue(Mirror)}));await Pause(1);}
    [StructLayout(LayoutKind.Sequential)] private struct WinRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd,out WinRect rect);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint hwnd,uint msg,nuint key,nint data,uint flags,uint timeout,out nuint result);
    private static void Send(uint msg,float x,float y) {var hwnd=(nint)(long)Field(Process,"Window")!;GetClientRect(hwnd,out var r);int px=(int)(x*r.Right),py=(int)(y*r.Bottom);if(SendMessageTimeoutW(hwnd,msg,msg==0x201?1u:0u,(nint)((py<<16)|(px&65535)),2,5000,out _)==0)throw new Exception("Owned replica message failed");}
    private static async Task ClickKey(string key)
    {
        if(!await Wait(()=>{var s=ChildState();return s.ValueKind==JsonValueKind.Object&&s.GetProperty("buttons").EnumerateArray().Concat(s.GetProperty("cards").EnumerateArray()).Any(c=>c.GetProperty("key").GetString()==key);},"replica control available: "+key,12))throw new Exception("Replica control absent: "+key);
        var state=ChildState();var control=state.GetProperty("buttons").EnumerateArray().Concat(state.GetProperty("cards").EnumerateArray()).Last(c=>c.GetProperty("key").GetString()==key);
        var r=control.GetProperty("rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray();float x=r[0]+r[2]/2,y=r[1]+r[3]/2;
        GD.Print("[CrystalFogProbe] CLICK "+key);Send(0x200,x,y);await Pause(.3);Send(0x201,x,y);await Frames(2);Send(0x202,x,y);await Pause(.7);
    }
    private static async Task<bool> Sync(string label) {bool ok=await Wait(()=>Verified&&Same("count")&&Same("fog")&&Same("tool")&&Same("gold")&&Same("deck"),label,15);if(ok)Check(true,label);return ok;}
    private static Type QuickType => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")).First(t=>t!=null)!;
    private static bool QuickPopup => (bool)QuickType.GetProperty("ConfirmationOpen",Any)!.GetValue(null)!;
    private static bool FogEnabled => (bool)Rmp("CrystalFog").GetProperty("Enabled",Any)!.GetValue(null)!;
    private static object MaskState()
    {
        var mask=Screen==null?null:Desc<NCrystalSphereMask>(Screen).FirstOrDefault();
        var material=mask?.Material as ShaderMaterial;
        return new {exists=mask!=null,preview=material?.GetShaderParameter("rmp_crystal_preview").AsBool()??false,mouse=mask?.MouseFilter.ToString(),cells=Screen==null?0:Desc<NCrystalSphereCell>(Screen).Count(c=>c.Entity.IsHidden&&c.MouseFilter==Control.MouseFilterEnum.Stop),code=material?.Shader.Code};
    }
    private static async Task F4(bool expected, bool child=false)
    {
        if(child){var hwnd=(nint)(long)Field(Process,"Window")!;SendMessageTimeoutW(hwnd,0x100,0x73,1,2,5000,out _);await Pause(.6);SendMessageTimeoutW(hwnd,0x101,0x73,unchecked((nint)(1L<<31|1L<<30|1)),2,5000,out _);}
        else{Input.ParseInputEvent(new InputEventKey{Keycode=Godot.Key.F4,Pressed=true});await Pause(.6);Input.ParseInputEvent(new InputEventKey{Keycode=Godot.Key.F4,Pressed=false});}
        if(!await Wait(()=>FogEnabled==expected&&ChildState().GetProperty("preview").GetBoolean()==expected,"F4 preference on both original windows",10))throw new Exception("F4 propagation failed");
        await Pause(1);Check(FogEnabled==expected,"holding F4 toggles once");
    }
    private static async Task SourceClick(Control node)
    {
        var viewport=NGame.Instance.GetViewport();var point=node.GetGlobalRect().GetCenter();
        viewport.PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);await Frames(3);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point},true);await Frames(3);
        viewport.PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point},true);await Pause(.7);
    }
    private static async Task TestFog()
    {
        string board=SourceState().GetProperty("board").GetRawText(),fog=SourceState().GetProperty("fog").GetRawText();int count=Entity!.DivinationCount;
        Stage("F4 灰雾预览 · 本体与实况窗口");
        if(FogEnabled)await F4(false);
        await F4(true);await Capture("fog-enabled");await Pause(4);
        Check(SourceState().GetProperty("mask").GetProperty("preview").GetBoolean()&&ChildState().GetProperty("mask").GetProperty("preview").GetBoolean(),"both native masks use translucent shader");
        await F4(false,true);await Capture("fog-disabled");await Pause(2);
        Check(!SourceState().GetProperty("mask").GetProperty("preview").GetBoolean()&&!ChildState().GetProperty("mask").GetProperty("preview").GetBoolean(),"F4 over replica restores original curtain on both windows");
        await F4(true);await Capture("fog-restored");
        Check(Entity.DivinationCount==count&&SourceState().GetProperty("board").GetRawText()==board&&SourceState().GetProperty("fog").GetRawText()==fog,"F4 leaves original board hidden cells and divination count unchanged");
        Check(SourceState().GetProperty("mask").GetProperty("cells").GetInt32()>0,"original hidden cell hitboxes remain enabled");
        var path=ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("rmp-spectator.json"));
        Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("CrystalFogPreview").GetBoolean(),"preference persisted outside run save");
    }
    private static async Task ReplicaKey(uint key)
    {
        var window=Mirror.GetType().GetProperty("Window",Any)!.GetValue(Mirror)!;
        Call(window,"RouteKeyboard",0x100u,(nuint)key,(nint)1,true);await Pause(.3);
        Call(window,"RouteKeyboard",0x100u,(nuint)key,unchecked((nint)(1L<<30|1)),true);await Pause(.15);
        Call(window,"RouteKeyboard",0x101u,(nuint)key,unchecked((nint)(1L<<31|1L<<30|1)),true);await Pause(.5);
    }
    private static async Task TestSl()
    {
        Stage("实况 F5 · 取消和确认快速 SL");
        var before=RunManager.Instance.DebugOnlyGetState();int pid=((System.Diagnostics.Process)Field(Process,"Child")!).Id;
        await ReplicaKey(0x74);if(!await Wait(()=>ChildState().GetProperty("slPopup").GetBoolean(),"F5 opens replica confirmation"))throw new Exception("F5 popup missing");
        Check(!QuickPopup,"replica F5 confirmation does not open an extra source popup");
        await ReplicaKey(27);await Wait(()=>!ChildState().GetProperty("slPopup").GetBoolean(),"Escape cancels F5");Check(ReferenceEquals(before,RunManager.Instance.DebugOnlyGetState()),"cancelled F5 does not reload source");
        if(SaveManager.Instance.CurrentRunSaveTask is {} save)await save;
        await ReplicaKey(0x74);await Wait(()=>ChildState().GetProperty("slPopup").GetBoolean(),"second F5 confirmation");await ReplicaKey(13);
        if(!await Wait(()=>!ReferenceEquals(before,RunManager.Instance.DebugOnlyGetState())&&RunManager.Instance.DebugOnlyGetState()!=null&&Verified,"F5 confirmed original native checkpoint restored",90))throw new Exception("F5 restore failed");
        Check(((System.Diagnostics.Process)Field(Process,"Child")!).Id==pid,"F5 native restore reuses same replica process");Check(FogEnabled&&ChildState().GetProperty("preview").GetBoolean(),"F4 preference survives real F5 SL");await Capture("f5-restored");
    }
    private static async Task Run()
    {
        try{
            if(!await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu ready",60))throw new Exception("No main menu");
            await Pause(2);Rmp("LiveSharingController").GetMethod("Close",Any)!.Invoke(null,null);SaveManager.Instance.SetFtuesEnabled(false);
            var canvas=new CanvasLayer{Layer=120};NGame.Instance.AddChild(canvas);_caption=new Label{Position=new Vector2(20,72),Size=new Vector2(1750,48),MouseFilter=Control.MouseFilterEnum.Ignore};_caption.AddThemeFontSizeOverride("font_size",26);_caption.AddThemeColorOverride("font_outline_color",Colors.Black);_caption.AddThemeConstantOverride("outline_size",6);canvas.AddChild(_caption);
            var character=ModelDb.Character<Ironclad>();var player=Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1);
            var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPFOG045");player.Gold=1000;
            RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(1);await RunManager.Instance.LoadIntoLatestMapCoord(null);
            Rmp("LiveSharingController").GetMethod("Open",Any)!.Invoke(null,new object[]{state});
            Check(FogEnabled == (Environment.GetEnvironmentVariable("RMP_FOG_RESTART")=="1"),"default off / persisted preference after process restart");
            var view=Static("_view")!;Call(view,"SetControlEnabled",true);var panel=(Control)Field(view,"_panel")!;panel.Size=new Vector2(830,600);panel.Position=new Vector2(1070,300);
            if(!await Wait(()=>Verified,"initial setup synchronized",80))throw new Exception("Initial synchronization failed");
            Stage("进入问号房：水晶球占卜");await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Unknown).coord);
            if(!await Wait(()=>Verified&&NRun.Instance.EventRoom!=null,"Crystal Sphere room synchronized",60))throw new Exception("Event setup failed");await Pause(1.5);
            Check(RunManager.Instance.EventSynchronizer.GetLocalEvent() is CrystalSphere,"original question room is Crystal Sphere");
            bool payment=Environment.GetEnvironmentVariable("RMP_CRYSTAL_BRANCH")=="payment";int expected=payment?6:3;
            Stage(payment?"选择赊账：六次占卜":"选择付金币：三次占卜");
            var option=Desc<NEventOptionButton>(NRun.Instance.EventRoom).Where(c=>c.IsEnabled).ElementAt(payment?1:0);await ClickKey(Key(option));
            if(!await Wait(()=>Entity!=null&&ChildState().ValueKind==JsonValueKind.Object&&ChildState().GetProperty("count").GetInt32()==expected,"both native minigames opened",45))throw new Exception("Minigame entry failed");await Pause(1.5);
            Check(Entity!.DivinationCount==expected,"original divination count "+expected);Check(Same("board")&&Same("fog"),"both initial boards and fog are identical");await Capture("initial");await TestFog();
            Stage("切换小占卜 → 大占卜");
            await ClickKey(Key(Desc<NDivinationButton>(Screen!).First(c=>c.Name.ToString().Contains("Small"))));if(!await Sync("small tool switching stays synchronized"))throw new Exception("Tool desync");
            await ClickKey(Key(Desc<NDivinationButton>(Screen!).First(c=>c.Name.ToString().Contains("Big"))));if(!await Sync("big tool switching stays synchronized"))throw new Exception("Tool desync");
            // Prioritize actual 2x2 card-reward items to exercise subsequent cards,
            // without changing the original board or reward generation.
            var targetItems=Entity.Items.OfType<CrystalSphereCardReward>().Take(payment?3:1).ToArray();
            var targets=targetItems.Select(i=>new Vector2I(i.Position.X,i.Position.Y)).ToList();
            while(targets.Count<expected)targets.Add(new Vector2I(3+(targets.Count%3)*2,3+(targets.Count/3)*2));
            for(int step=0;step<expected;step++){
                Stage("第 "+(step+1)+" / "+expected+" 次占卜揭格");
                var cells=Desc<NCrystalSphereCell>(Screen!).ToArray();var target=targets[step];var cell=cells.FirstOrDefault(c=>c.Entity.X==target.X&&c.Entity.Y==target.Y&&c.Entity.IsHidden)??cells.First(c=>c.Entity.IsHidden);
                if(step%2==0)await ClickKey(Key(cell));else await SourceClick(cell);await Pause(.8);
                if(!await Wait(()=>Entity?.DivinationCount==expected-step-1,"one click consumes exactly one divination: "+(step+1),3))throw new Exception("Unexpected extra input or repeated divination execution");
                Check(true,"one click consumes exactly one divination: "+(step+1));
                if(!await Sync("divination "+(step+1)+" fog/count/model agree")){await Capture("desync-step-"+(step+1));throw new Exception("Cell desync");}
                await Capture("step-"+(step+1));
            }
            Stage("占卜奖励及选牌");
            int rewardChoices=0;
            for(int attempt=0;attempt<15;attempt++){
                await Pause(2);var child=ChildState();string screen=child.GetProperty("screen").GetString()!;var cards=child.GetProperty("cards").EnumerateArray().ToArray();
                // Hover raises the native holder in sibling order. Compare semantic
                // identities (including duplicates), not the transient scene z-order.
                if(screen.Contains("CardReward")&&cards.Length>0){Check(SourceState().GetProperty("cards").EnumerateArray().Select(c=>c.GetProperty("id").GetString()).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(cards.Select(c=>c.GetProperty("id").GetString()).OrderBy(id=>id,StringComparer.Ordinal)),"native reward choices identical");await Capture("reward-choice-"+attempt);await ClickKey(cards[0].GetProperty("key").GetString()!);if(!await Sync("reward card selection converges"))throw new Exception("Reward selection desync");rewardChoices++;continue;}
                var buttons=child.GetProperty("buttons").EnumerateArray().Where(b=>b.GetProperty("enabled").GetBoolean()).ToArray();
                var reward=buttons.FirstOrDefault(b=>b.GetProperty("type").GetString()=="NRewardButton");if(reward.ValueKind!=JsonValueKind.Undefined){await ClickKey(reward.GetProperty("key").GetString()!);continue;}
                var proceed=buttons.LastOrDefault(b=>b.GetProperty("type").GetString()=="NProceedButton");if(proceed.ValueKind!=JsonValueKind.Undefined){await ClickKey(proceed.GetProperty("key").GetString()!);if(!await Wait(()=>Verified&&NMapScreen.Instance?.IsOpen==true&&Same("map"),"event exit synchronized",30))throw new Exception("Exit desync");Check(true,"both sides return to original map");break;}
            }
            Check(rewardChoices>=targetItems.Length,"all targeted native card rewards exercised: "+rewardChoices);
            Check(RunManager.Instance.EventSynchronizer.GetLocalEvent().IsFinished,"original event completed");await Capture("finished");await TestSl();Stage(_failures==0?"专项通过，画面保留 8 秒":"专项发现问题，画面保留 8 秒");
        }catch(Exception ex){Check(false,"probe halted: "+ex);Stage("专项中止，保留现场 30 秒（以日志诊断为准）");try{await Capture("failure");}catch{}}
        File.WriteAllText(Path.Combine(Output,"result.json"),JsonSerializer.Serialize(new{failures=_failures,findings=Findings}));GD.Print("[CrystalFogProbe] "+(_failures==0?"CRYSTAL PASSED":"CRYSTAL FAILED"));await Pause(8);Rmp("LiveSharingController").GetMethod("Suspend",Any)!.Invoke(null,null);((SceneTree)Engine.GetMainLoop()).Quit(_failures==0?0:1);
    }
}
