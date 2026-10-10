using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment=System.Environment;

[ModInitializer(nameof(Initialize))]
public static class Ui14RefinementProbe
{
    private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private const string SteamName="山岚中文昵称测试";
    private const ulong SteamAccount=76561198012345678;
    private static ulong _nameAccount;
    private static int _checks,_failedAttempts,_dialogueAttempts;
    private static string _lastFailure="";
    private static bool Renderer=>Environment.GetEnvironmentVariable("RMP_MULTI_ROLE")=="renderer";
    private static string Output=>Renderer?Environment.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!:Environment.GetEnvironmentVariable("RMP_REFINEMENT_EVIDENCE")!;
    private static Type Rmp(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing."+name)).First(t=>t!=null)!;
    private static object? Static(string name)=>Rmp("LiveSharingController").GetField(name,Any)!.GetValue(null);
    private static object Field(object target,string name)=>target.GetType().GetField(name,Any)?.GetValue(target) ?? target.GetType().GetProperty(name,Any)?.GetValue(target) ?? throw new MissingMemberException(target.GetType().Name,name);
    private static object? Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Any)!.Invoke(target,args);
    private static void Controller(string name)=>Rmp("LiveSharingController").GetMethod(name,Any)!.Invoke(null,null);
    private static object Mirror=>Static("_mirror")!;
    private static object Process=>Field(Mirror,"Process");
    private static bool Verified=>Static("_mirror") is {} m&&(bool)m.GetType().GetProperty("Verified",Any)!.GetValue(m)!;
    private static IEnumerable<T> Desc<T>(Node node) where T:Node {foreach(var child in node.GetChildren()){if(child is T value)yield return value;foreach(var nested in Desc<T>(child))yield return nested;}}
    private static async Task Frames(int n){var tree=(SceneTree)Engine.GetMainLoop();for(int i=0;i<n;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    private static async Task Pause(double seconds){ulong end=Time.GetTicksMsec()+(ulong)(seconds*1000);while(Time.GetTicksMsec()<end)await Frames(1);}
    private static async Task Wait(Func<bool> condition,string name,int seconds=60){ulong end=Time.GetTicksMsec()+(ulong)seconds*1000;while(Time.GetTicksMsec()<end){if(condition())return;await Frames(1);}throw new Exception("Timeout: "+name);}
    private static void Check(bool ok,string name){File.AppendAllText(Path.Combine(Output,"checks.jsonl"),JsonSerializer.Serialize(new{name,passed=ok})+"\n");if(!ok)throw new Exception(name);_checks++;GD.Print("[Ui14Refinement] PASS "+name);}
    private static string Key(Node node)=>(string)Rmp("MirrorNativeUi").GetMethod("Key",Any)!.Invoke(null,new[]{node})!;
    public static void Initialize()
    {
        if(Renderer?Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST")!="1":Environment.GetEnvironmentVariable("RMP_MULTI_REFINEMENT_TEST")!="1")return;
        var h=new Harmony("RMP.Ui14Refinement.Observe");
        h.Patch(typeof(MerchantEntry).GetMethod("InvokePurchaseFailed")!,postfix:new HarmonyMethod(typeof(Ui14RefinementProbe).GetMethod(nameof(Failed),Any)!));
        h.Patch(typeof(NMerchantDialogue).GetMethod("ShowForPurchaseAttempt")!,postfix:new HarmonyMethod(typeof(Ui14RefinementProbe).GetMethod(nameof(Dialogue),Any)!));
        if(Renderer)TaskHelper.RunSafely(Observe());else TaskHelper.RunSafely(Run());
    }
    private static void Failed(PurchaseStatus status){_failedAttempts++;_lastFailure=status.ToString();}
    private static void Dialogue(PurchaseStatus status){if(status!=PurchaseStatus.Success)_dialogueAttempts++;}
    private static bool SteamId(ref ulong __result){__result=SteamAccount;return false;}
    private static bool Name(ulong __0,ref string __result){_nameAccount=__0;__result=SteamName;return false;}
    private static object SteamSnapshot(RunState state)
    {
        var h=new Harmony("RMP.Ui14Refinement.SteamFixture");
        var initialized=typeof(SteamInitializer).GetField("<Initialized>k__BackingField",Any)!;
        bool previous=(bool)initialized.GetValue(null)!;
        try
        {
            // PlatformUtil's tiny wrappers are inlined in the already running
            // capture. Substitute the platform strategy, before setting the
            // flag, and restore it synchronously without any frame advancing.
            h.Patch(typeof(SteamPlatformUtilStrategy).GetMethod("GetLocalPlayerId")!,prefix:new HarmonyMethod(typeof(Ui14RefinementProbe).GetMethod(nameof(SteamId),Any)!));
            h.Patch(typeof(SteamPlatformUtilStrategy).GetMethod("GetPlayerName")!,prefix:new HarmonyMethod(typeof(Ui14RefinementProbe).GetMethod(nameof(Name),Any)!));
            initialized.SetValue(null,true);
            Field(Static("_source")!,"_participant");Static("_source")!.GetType().GetField("_participantExpires",Any)!.SetValue(Static("_source"),0UL);
            return Invoke(Static("_source")!,"CaptureCommands",state)!;
        }
        finally {initialized.SetValue(null,previous);h.UnpatchAll(h.Id);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct WinRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd,out WinRect rect);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint hwnd,uint msg,nuint key,nint data,uint flags,uint timeout,out nuint result);
    private static float[] Rect(Control control)
    {
        var viewport=NGame.Instance.GetViewport();var r=(viewport.GetFinalTransform()*control.GetGlobalTransformWithCanvas())*new Rect2(Vector2.Zero,control.Size);
        var hwnd=(nint)Rmp("MirrorWin32").GetProperty("OwnWindow",Any)!.GetValue(null)!;GetClientRect(hwnd,out var client);
        return new[]{r.Position.X/client.Right,r.Position.Y/client.Bottom,r.Size.X/client.Right,r.Size.Y/client.Bottom};
    }
    private static object State()
    {
        var inspector=NGame.Instance?.InspectCardScreen;var relic=NGame.Instance?.InspectRelicScreen;var player=RunManager.Instance.DebugOnlyGetState()?.Players[0];
        var tick=inspector?.IsVisibleInTree()==true?Desc<NTickbox>(inspector).FirstOrDefault():null;
        var preview=inspector?.IsVisibleInTree()==true?(NCard)Field(inspector,"_card"):null;
        var buttons=new Control?[]{tick}.Concat(inspector?.IsVisibleInTree()==true?Desc<NClickableControl>(inspector).Where(n=>n.IsVisibleInTree()):Array.Empty<Control>()).Concat(relic?.IsVisibleInTree()==true?Desc<NClickableControl>(relic).Where(n=>n.IsVisibleInTree()):Array.Empty<Control>())
            .Where(n=>n!=null).Select(n=>new{key=Key(n!),name=n!.Name.ToString(),type=n.GetType().Name,rect=Rect(n!)}).ToArray();
        return new{failures=_failedAttempts,dialogues=_dialogueAttempts,lastFailure=_lastFailure,cardPreview=inspector?.IsVisibleInTree()==true,relicPreview=relic?.IsVisibleInTree()==true,upgraded=tick?.IsTicked==true,previewLevel=preview?.Model?.CurrentUpgradeLevel??-1,gold=player?.Gold??-1,deck=player?.Deck.Cards.Count??-1,buttons};
    }
    private static async Task Observe()
    {
        while(true)
        {
            await Frames(6);if(NGame.Instance==null||!Directory.Exists(Output))continue;
            try
            {
                var path=Path.Combine(Output,"refinement-state.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(State()));File.Move(path+".tmp",path,true);
                var request=Path.Combine(Output,"capture-request.txt");
                if(File.Exists(request)){string name=File.ReadAllText(request);File.Delete(request);await Frames(2);using var image=NGame.Instance.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(Output,name+".png"));}
            }
            catch(IOException) { /* Concurrent diagnostic file replacement; retry. */ }
            catch(UnauthorizedAccessException) { /* Windows sharing lock; retry. */ }
        }
    }
    private static string ChildRoot=>(string)Field(Process,"DirectoryPath");
    private static JsonElement Child(){try{using var stream=new FileStream(Path.Combine(ChildRoot,"refinement-state.json"),FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var doc=JsonDocument.Parse(stream);return doc.RootElement.Clone();}catch{return default;}}
    private static bool ChildBool(string name)=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty(name).GetBoolean();
    private static int ChildInt(string name)=>Child().ValueKind==JsonValueKind.Object?Child().GetProperty(name).GetInt32():-1;
    private static void Send(uint message,float[] rect)
    {
        var hwnd=(nint)(long)Field(Process,"Window");GetClientRect(hwnd,out var client);int x=(int)((rect[0]+rect[2]/2)*client.Right),y=(int)((rect[1]+rect[3]/2)*client.Bottom);
        if(SendMessageTimeoutW(hwnd,message,message==0x201?1u:message==0x204?2u:0u,(nint)((y<<16)|(x&65535)),2,5000,out _)==0)throw new Exception("Owned replica input timeout");
    }
    private static float[] Hit(string key)
    {
        var hits=((IEnumerable)Field(Process,"LastHits")).Cast<object>();var hit=hits.Last(h=>(string)Field(h,"NodeKey")==key);return (float[])Field(hit,"Rect");
    }
    private static async Task Click(float[] rect,bool right=false){Send(0x200,rect);await Pause(.15);Send(right?0x204u:0x201u,rect);await Frames(2);Send(right?0x205u:0x202u,rect);}
    private static async Task ClickKey(string key,bool right=false)
    {
        await Wait(()=>((IEnumerable)Field(Process,"LastHits")).Cast<object>().Any(h=>(string)Field(h,"NodeKey")==key),"replica hit "+key);
        // Hover scales original shop slots; wait for both that layout and the
        // authority-ready reply, then use the current authenticated hit rect.
        Send(0x200,Hit(key));await Pause(.5);var rect=Hit(key);
        File.AppendAllText(Path.Combine(Output,"clicks.jsonl"),JsonSerializer.Serialize(new{key,right,rect,verified=Verified})+"\n");await Click(rect,right);
    }
    private static async Task Capture(string name){File.WriteAllText(Path.Combine(ChildRoot,"capture-request.txt"),name);await Pause(.3);}
    private static async Task Run()
    {
        int exit=0;
        try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Frames(120);SaveManager.Instance.SetFtuesEnabled(false);
            var character=ModelDb.Character<Ironclad>();var player=Player.CreateForNewRun(character,SaveManager.Instance.GenerateUnlockStateFromProgress(),1);player.Gold=0;
            var state=RunState.CreateForNewRun(new[]{player},ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPUI14REFINE");
            RunManager.Instance.SetUpNewSingleplayer(state,true);await PreloadManager.LoadRunAssets(new[]{character});RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);
            Controller("Suspend");var preferences=Rmp("SpectatorPreferences");string prefs=ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("rmp-spectator.json"));
            File.WriteAllText(prefs,"{\"LayoutRevision\":14,\"HasLayout\":true,\"Width\":576,\"X\":1344,\"Y\":156,\"Pinned\":true,\"DockEdge\":2}");preferences.GetField("_path",Any)!.SetValue(null,"");preferences.GetMethod("LoadProfile",Any)!.Invoke(null,new object[]{(Action)(()=>{})});
            Rmp("LiveSharingController").GetMethod("Open",Any)!.Invoke(null,new object[]{state});var view=Static("_view")!;
            await Wait(()=>Verified&&(bool)Field(Process,"DisplayReady"),"map verified");await Frames(30);
            var panel=(Control)Field(view,"_panel");Check(Math.Abs(panel.Size.Y-panel.GetViewport().GetVisibleRect().Size.Y/2)<1,"new default height equals half of game screen");
            var reset=(Button)Field(view,"_refresh");var icon=reset.GetNode<TextureRect>("Icon");using(var pixels=icon.Texture.GetImage()){File.WriteAllText(Path.Combine(Output,"icon-metrics.json"),JsonSerializer.Serialize(new{width=pixels.GetWidth(),height=pixels.GetHeight(),mips=pixels.HasMipmaps(),display=new[]{icon.Size.X,icon.Size.Y},position=new[]{icon.Position.X,icon.Position.Y}}));pixels.SavePng(Path.Combine(Output,"reset-icon-512.png"));Check(pixels.GetWidth()==512&&pixels.GetHeight()==512&&pixels.HasMipmaps()&&icon.Size==new Vector2(24,24),"reset downsamples a 512 pixel mipmapped icon");}
            reset.EmitSignal(Control.SignalName.MouseEntered);Check(icon.Scale.X>1,"high resolution reset retains toolbar hover feedback");reset.EmitSignal(Control.SignalName.MouseExited);
            var pin=(Button)Field(view,"_pin");var close=panel.GetNode<Button>("SpectatorClose");
            Check(reset.Size==pin.Size&&pin.Size==close.Size&&icon.Position+icon.Size/2==pin.GetNode<Node2D>("Icon").Position&&Math.Abs((reset.GetGlobalTransform()*new Vector2(18,18)).Y-(pin.GetGlobalTransform()*new Vector2(18,18)).Y)<.1,"reset icon shares button size and center line with pin and close");
            var snapshot=SteamSnapshot(state);Check(_nameAccount==SteamAccount&&_nameAccount!=player.NetId,"Steam nickname lookup uses local account rather than session NetId");
            Invoke(view,"Update",snapshot,true,"");var options=(IList)Field(view,"_sourceOptions");Check(options.Count==7&&options.Cast<object>().All(p=>(string)p.GetType().GetProperty("Name")!.GetValue(p)! ==SteamName+" · "+(options.IndexOf(p)+1)),"production singleplayer navigation has seven local Steam named sources");
            var labels=Desc<Label>((Control)Field(view,"_sourceSelector")).Where(l=>l.Text.StartsWith(SteamName)).ToArray();Check(labels.Length==3&&labels.All(l=>l.Size.X>=l.GetThemeFont("font").GetStringSize(SteamName,HorizontalAlignment.Left,-1,l.GetThemeFontSize("font_size")).X),"each username slot fits eight Chinese characters");
            Check(Desc<Panel>((Control)Field(view,"_sourceSelector")).All(p=>p.GetThemeStylebox("panel") is StyleBoxFlat s&&s.CornerRadiusTopLeft==6&&s.CornerRadiusBottomRight==6),"username backgrounds have rounded corners");
            Invoke(view,"SetControlEnabled",true);await Wait(()=>(bool)Field(Process,"LastControlEnabled"),"control enabled");object process=Process;string sourceId=(string)snapshot.GetType().GetProperty("SourceId")!.GetValue(snapshot)!;
            var next=(Button)Field(view,"_sourceNext");var previous=(Button)Field(view,"_sourcePrevious");var selector=(Control)Field(view,"_sourceSelector");
            for(int i=1;i<=7;i++)
            {
                int page=(i-1)/3;while((int)Field(view,"_sourcePageIndex")<page)next.EmitSignal(Button.SignalName.Pressed);
                selector.GetNode<Button>("SpectatorSource"+((i-1)%3)).EmitSignal(Button.SignalName.Pressed);Invoke(view,"Update",snapshot,true,"");
                Check((string)Field(view,"_selectedSourceId")=="local-preview:"+i&&(bool)view.GetType().GetProperty("ControlEnabled",Any)!.GetValue(view)!&&ReferenceEquals(process,Process)&&(string)snapshot.GetType().GetProperty("SourceId")!.GetValue(snapshot)! ==sourceId,"source "+i+" remains selected with same local process and authority");
            }
            Check(previous.Visible&&!next.Visible,"seven-source last page hides unavailable arrow");await Frames(2);using(var image=NGame.Instance.GetViewport().GetTexture().GetImage())image.SavePng(Path.Combine(Output,"navigation-refinement.png"));
            await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Shop).coord);await Wait(()=>Verified&&NMerchantRoom.Instance!=null,"merchant entry verified");
            await ClickKey(Key(NMerchantRoom.Instance.MerchantButton));await Wait(()=>Verified&&NMerchantRoom.Instance.Inventory.IsOpen,"inventory open");await Pause(1);
            int deck=player.Deck.Cards.Count;var slots=NMerchantRoom.Instance.Inventory.GetAllSlots().Where(s=>s.Entry.IsStocked).ToArray();
            foreach(Type type in new[]{typeof(NMerchantCard),typeof(NMerchantRelic),typeof(NMerchantPotion),typeof(NMerchantCardRemoval)})
            {
                var slot=slots.First(s=>s.GetType()==type);int sourceBefore=_failedAttempts,childBefore=ChildInt("failures"),dialogueBefore=ChildInt("dialogues");
                await ClickKey(Key(slot));await Wait(()=>_failedAttempts>sourceBefore&&ChildInt("failures")>childBefore&&ChildInt("dialogues")>dialogueBefore&&Verified,"unaffordable "+type.Name);
                Check(_lastFailure=="FailureGold"&&Child().GetProperty("lastFailure").GetString()=="FailureGold"&&player.Gold==0&&player.Deck.Cards.Count==deck&&slot.Entry.IsStocked,"unaffordable "+type.Name+" emits native dialogue and keeps state synchronized");
            }
            var card=slots.OfType<NMerchantCard>().First(s=>((MerchantCardEntry)s.Entry).CreationResult!.Card.IsUpgradable);
            float[] cardRect=Hit(Key(card));await ClickKey(Key(card),true);await Wait(()=>ChildBool("cardPreview"),"merchant card preview");Check(NGame.Instance.InspectCardScreen?.IsVisibleInTree()!=true&&Verified,"right click opens native card preview only inside replica");
            var inspected=Child();File.WriteAllText(Path.Combine(Output,"merchant-card-inspector.json"),inspected.GetRawText());
            var tick=inspected.GetProperty("buttons").EnumerateArray().First(b=>b.GetProperty("name").GetString()=="Upgrade");await Click(tick.GetProperty("rect").EnumerateArray().Select(v=>v.GetSingle()).ToArray());
            await Wait(()=>ChildBool("upgraded")&&ChildInt("previewLevel")>0,"upgrade preview");Check(player.Gold==0&&player.Deck.Cards.Count==deck&&Verified,"upgrade preview changes no authoritative card or purchase");await Capture("merchant-card-upgrade");
            await Click(cardRect,true);await Wait(()=>!ChildBool("cardPreview"),"preview close");
            var relic=slots.OfType<NMerchantRelic>().First();float[] relicRect=Hit(Key(relic));await ClickKey(Key(relic),true);await Wait(()=>ChildBool("relicPreview"),"relic preview");Check(NGame.Instance.InspectRelicScreen?.IsVisibleInTree()!=true&&Verified,"merchant relic inspection is native and local");await Capture("merchant-relic-preview");await Click(relicRect,true);await Wait(()=>!ChildBool("relicPreview"),"relic preview close");
            File.WriteAllText(Path.Combine(Output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks=_checks,scope=".14 UI refinements, Steam name routing and merchant feedback/inspection only",steam="fixture; isolated runtime disables Steam"}));GD.Print("[Ui14Refinement] RESULT checks="+_checks);
        }
        catch(Exception e){exit=1;GD.Print("[Ui14Refinement] FAIL "+e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());if(Static("_mirror")!=null&&Child().ValueKind==JsonValueKind.Object)File.WriteAllText(Path.Combine(Output,"failure-child-state.json"),Child().GetRawText());}
        Controller("Suspend");((SceneTree)Engine.GetMainLoop()).Quit(exit);
    }
}
