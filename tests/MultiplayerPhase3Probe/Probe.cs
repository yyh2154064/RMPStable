using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using Env = System.Environment;

// Stage three only: authorization, battle requests and original multiplayer results.
[ModInitializer(nameof(Initialize))]
public static class MultiplayerPhase3Probe
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static bool Renderer => Env.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    static bool Host => Env.GetEnvironmentVariable("RMP_PHASE3_ROLE") == "host";
    static string Root => Renderer ? Env.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")! : Env.GetEnvironmentVariable("RMP_PHASE3_EVIDENCE")!;
    static string Role => Host ? "host" : "client";
    static int checks;
    static Type Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).First(t => t != null)!;
    static object? Get(object? o, string name, Type? type = null)
    { for (var t = o?.GetType() ?? type; t != null; t = t.BaseType) { if(t.GetProperty(name,Any|BindingFlags.DeclaredOnly) is {} p && (o!=null||p.GetMethod!.IsStatic)) return p.GetValue(o); if(t.GetField(name,Any|BindingFlags.DeclaredOnly) is {} f && (o!=null||f.IsStatic)) return f.GetValue(o); } return null; }
    static object? Call(object? o, string name, params object[] args) => o!.GetType().GetMethod(name,Any)!.Invoke(o,args);
    static object? Static(string type, string name, params object[] args) => Rmp(type).GetMethod(name,Any)!.Invoke(null,args);
    static object? Session(string name) => Get(null,name,Rmp("MultiplayerMirrorSession"));
    static object? View => Get(null,"_view",Rmp("LiveSharingController"));
    static object Mirror => Session("Mirror")!;
    static object Proc => Get(Mirror,"Process")!;
    static int Pid => ((Process)Get(Proc,"Child")!).Id;
    static string Hash(RunState state) => (string)Static("MirrorState","MultiplayerHash",state)!;
    static async Task Frames(int n=1) {var tree=(SceneTree)Engine.GetMainLoop(); for(int i=0;i<n;i++) await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);}
    static async Task Wait(Func<bool> c,string label,int seconds=75)
    {ulong end=Time.GetTicksMsec()+(ulong)(seconds*1000); while(Time.GetTicksMsec()<end) {if(c())return; if(Session("Error") is string e && e.Length>0) throw new Exception(e); await Frames();} throw new Exception("Timeout: "+label+" phase="+Get(Session("Mirror") is {} m?Get(m,"Process"):null,"Phase",Rmp("MirrorProcess"))+" mirror="+Get(Session("Mirror"),"Error",Rmp("MirrorHost")));}
    static void Check(bool c,string label)
    {File.AppendAllText(Path.Combine(Root,Role+"-checks.jsonl"),JsonSerializer.Serialize(new{label,passed=c})+"\n"); if(!c)throw new Exception(label); checks++; GD.Print("[MultiplayerPhase3] PASS "+label);}
    static void Write(string name,object value) {string p=Path.Combine(Root,name); File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value)); File.Move(p+".tmp",p,true);}
    static JsonElement ReadPath(string p) {try{using var s=new FileStream(p,FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}catch{return default;}}
    static async Task Barrier(string stage) {Write(Role+"-"+stage+"-barrier.json",new{ready=true});await Wait(()=>File.Exists(Path.Combine(Root,(Host?"client":"host")+"-"+stage+"-barrier.json")),stage);}
    static JsonElement Child() => Session("Mirror")==null?default:ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"phase3-state.json"));
    static JsonElement Authority() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("authority"):default;}
    static JsonElement Personal() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("choice"):default;}
    static string ChoiceKind() => Personal().ValueKind==JsonValueKind.Object?Personal().GetProperty("Kind").GetString()!:"";
    static int Sequence;
    static async Task<JsonElement> Remote(string kind,string model="",int index=-1,int target=-1)
    {
        int seq=++Sequence;string dir=(string)Get(Proc,"DirectoryPath")!;
        WritePath(Path.Combine(dir,"phase3-input.json"),new{seq,kind,model,index,target});
        await Wait(()=>ReadPath(Path.Combine(dir,"phase3-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"phase3-output.json")).GetProperty("seq").GetInt32()==seq,"replica command "+kind);
        var output=ReadPath(Path.Combine(dir,"phase3-output.json"));Check(output.GetProperty("issued").GetBoolean(),"replica submits authorized "+kind+" "+model);return output.GetProperty("request").Clone();
    }
    static void WritePath(string p,object value){File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value));File.Move(p+".tmp",p,true);}
    public static void Initialize()
    {if(Env.GetEnvironmentVariable("RMP_PHASE3_TEST")!="1")return;TaskHelper.RunSafely(Renderer?Observe():Run());}
    static async Task Observe()
    {
        int handled=0; object? pendingOutput=null;
        while(true)
        {
            await Frames(2);var renderer=Get(null,"Active",Rmp("MirrorRenderer"));var state=RunManager.Instance.DebugOnlyGetState();if(renderer==null||state==null)continue;
            try
            {
                if(pendingOutput!=null){WritePath(Path.Combine(Root,"phase3-output.json"),pendingOutput);pendingOutput=null;}
                var authority=Get(renderer,"_battleAuthority");var json=JsonSerializer.SerializeToElement(authority);
                var input=ReadPath(Path.Combine(Root,"phase3-input.json"));
                if(input.ValueKind==JsonValueKind.Object&&input.GetProperty("seq").GetInt32()>handled&&json.ValueKind==JsonValueKind.Object&&(bool)Get(renderer,"MultiplayerInputReady")!)
                {
                    int seq=input.GetProperty("seq").GetInt32();string kind=input.GetProperty("kind").GetString()!,model=input.GetProperty("model").GetString()!;
                    int index=input.GetProperty("index").GetInt32(),target=input.GetProperty("target").GetInt32();
                    var actions=json.GetProperty("Actions").EnumerateArray().Where(a=>a.GetProperty("Enabled").GetBoolean()).ToArray();
                    if(kind=="play")index=LocalContext.GetMe(state)!.PlayerCombatState!.Hand.Cards.ToList().FindIndex(c=>c.GetType().Name==model);
                    if(kind=="potion")index=LocalContext.GetMe(state)!.Potions.ToList().FindIndex(p=>p.GetType().Name==model);
                    var action=actions.FirstOrDefault(a=>a.GetProperty("Kind").GetString()==kind&&(kind is "play" or "potion"?a.GetProperty("NativeIndex").GetInt32()==index:kind!="select"||(!a.GetProperty("NativePath").GetString()!.Contains("NSelectedHandCardHolder")&&(model.Length==0||a.GetProperty("CardId").GetString()!.Contains(model,StringComparison.OrdinalIgnoreCase)))));
                    if(action.ValueKind!=JsonValueKind.Object){await Frames();continue;}
                    if(target==-3)target=LocalContext.GetMe(state)!.Creature.CombatState.Creatures.ToList().FindIndex(c=>c.IsPlayer&&c.Player!.NetId!=LocalContext.NetId);
                    if(target==-2)target=json.GetProperty("Targets").EnumerateArray().First(t=>t.GetProperty("Enemy").GetBoolean()).GetProperty("NativeIndex").GetInt32();
                    long before=(long)Get(renderer,"_request")!;
                    if(kind=="potion")
                    {
                        var potion=LocalContext.GetMe(state)!.Potions.ElementAt(index);
                        var holder=Descendants<NPotionHolder>(NRun.Instance.GlobalUi.TopBar).First(h=>h.Potion?.Model==potion);
                        holder.ForceClick();await Frames(4);
                        var popup=Descendants<NPotionPopup>(NRun.Instance).First(p=>!p.IsMarkedForRemoval);
                        ((NClickableControl)Get(popup,"_useButton")!).ForceClick();await Frames(4);
                        if(NTargetManager.Instance.IsInSelection){if(target<0)target=LocalContext.GetMe(state)!.Creature.CombatState.Creatures.ToList().IndexOf(LocalContext.GetMe(state)!.Creature);var creature=LocalContext.GetMe(state)!.Creature.CombatState.Creatures[target];var node=NRun.Instance.CombatRoom.GetCreatureNode(creature);NTargetManager.Instance.OnNodeHovered(node);NTargetManager.Instance._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});await Frames(4);}
                        for(int i=0;i<30&&(long)Get(renderer,"_request")! ==before;i++)await Frames();
                    }
                    else if(kind=="select"||kind=="choice")
                    {
                        await Frames(20);
                        var node=Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,true) as Control ?? throw new Exception("Replica native action path missing");
                        Control hitbox=node is NCardHolder cardHolder?cardHolder.Hitbox:node;
                        var point=hitbox.GetGlobalTransformWithCanvas()*(hitbox.Size/2)/NGame.Instance.GetViewport().GetVisibleRect().Size;
                        GD.Print("[MultiplayerPhase3] Mouse "+kind+" path="+action.GetProperty("NativePath").GetString()+" point="+point+" visible="+hitbox.IsVisibleInTree());
                        var queue=(System.Collections.Concurrent.ConcurrentQueue<(uint,Vector2,long,long,float)>)Get(renderer,"_buttons")!;
                        queue.Enqueue((0x201,point,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!,0));await Frames(2);
                        queue.Enqueue((0x202,point,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!,0));await Frames(4);
                        for(int i=0;i<60&&(long)Get(renderer,"_request")! ==before;i++)await Frames();
                    }
                    else if(kind=="play"&&model=="Prepared"||kind=="endTurn")
                    {
                        var queue=(System.Collections.Concurrent.ConcurrentQueue<(uint,uint,long,long,long)>)Get(renderer,"_keyboard")!;
                        string shortcut=kind=="endTurn"?"ui_end_turn":"mega_select_card_"+(NPlayerHand.Instance.ActiveHolders.ToList().FindIndex(h=>h.CardModel?.GetType().Name==model)+1);
                        uint key=(uint)NInputManager.Instance.GetMKbHotkey(shortcut);
                        queue.Enqueue((0x100,key,0,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!));await Frames(3);
                        queue.Enqueue((0x101,key,0,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!));
                        if(kind=="play"){await Frames(2);queue.Enqueue((0x100,13,0,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!));await Frames(2);queue.Enqueue((0x101,13,0,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!));}
                        for(int i=0;i<30&&(long)Get(renderer,"_request")! ==before;i++)await Frames();
                    }
                    else Call(renderer,"SendIntent",kind,index,target,Hash(state),(int)Get(renderer,"_events")!,action.GetProperty("NativePath").GetString()!,"");
                    handled=seq;
                    var req=new{Epoch=Get(renderer,"_epoch"),Request=Get(renderer,"_request"),Generation=Get(renderer,"_generation"),Events=Get(renderer,"_events"),NodeKey=action.GetProperty("Id").GetString(),Value=json.GetProperty("Context").GetString(),TargetKey=json.GetProperty("Targets").EnumerateArray().FirstOrDefault(t=>t.GetProperty("NativeIndex").GetInt32()==target).ValueKind==JsonValueKind.Object?json.GetProperty("Targets").EnumerateArray().First(t=>t.GetProperty("NativeIndex").GetInt32()==target).GetProperty("Id").GetString():""};
                    pendingOutput=new{seq,issued=(long)Get(renderer,"_request")!>before,request=req};
                    WritePath(Path.Combine(Root,"phase3-output.json"),pendingOutput);pendingOutput=null;
                }
                var me=LocalContext.GetMe(state);
                WritePath(Path.Combine(Root,"phase3-state.json"),new{authority=Get(renderer,"_battleAuthority"),choice=Static("MultiplayerPersonalUi","Capture"),control=Get(renderer,"_control"),inputReady=Get(renderer,"MultiplayerInputReady"),hash=Hash(state),events=Get(renderer,"_events"),local=LocalContext.NetId,hand=me?.PlayerCombatState?.Hand.Cards.Select(c=>c.GetType().Name),waiting=Get(renderer,"_waitingForAuthority"),epoch=Get(renderer,"_epoch")});
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    static IEnumerable<T> Descendants<T>(Node root) where T:Node
    {foreach(var node in root.GetChildren()){if(node is T t)yield return t;foreach(var child in Descendants<T>(node))yield return child;}}
    static async Task Grant(string tag,bool deny=false)
    {
        ((Button)Get(View!,"_controlToggle")!).EmitSignal(Button.SignalName.Toggled,true);
        await Wait(()=>Get(null,"_pendingViewer",Rmp("MultiplayerMirrorSession")) is ulong id&&id!=0,"source receives permission request");
        Check(Get(null,"_controlViewer",Rmp("MultiplayerMirrorSession")) is ulong id&&id==0,"request does not auto-grant another player's authority");
        await Wait(()=>Get(null,"_denyButton",Rmp("MultiplayerMirrorSession")) is Button,"permission panel initialized");
        if(deny){((Button)Get(null,"_denyButton",Rmp("MultiplayerMirrorSession"))!).EmitSignal(Button.SignalName.Pressed);await Barrier(tag);await Frames(20);Check(!(bool)Get(View!,"ControlEnabled")!,"source denial keeps replica read-only");return;}
        await Wait(()=>Get(null,"_allowButton",Rmp("MultiplayerMirrorSession")) is Button,"native permission buttons");
        ((Button)Get(null,"_allowButton",Rmp("MultiplayerMirrorSession"))!).EmitSignal(Button.SignalName.Pressed);
        await Barrier(tag);await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("control").GetBoolean(),"grant reaches authenticated replica");
        Check((bool)Get(View!,"ControlEnabled")!,"shell follows explicit remote grant");
    }
    static void Forward(JsonElement request,Action<object>? change=null)
    {var m=JsonSerializer.Deserialize(request.GetRawText(),Rmp("MirrorMessage"))!;change?.Invoke(m);Static("MultiplayerMirrorSession","ForwardControlIntent",m);}
    static void Set(object o,string field,object value)=>o.GetType().GetProperty(field,Any)!.SetValue(o,value);
    static async Task Sync(RunState state,string tag)
    {await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning&&(bool)Get(Mirror,"Verified")!&&(string)Get(Proc,"LastHash")! ==Hash(state)&&ChoiceKind()=="",tag);Check((string)Get(Proc,"LastHash")! ==Hash(state),tag+" original results converge in independent replica");await Barrier(tag);}
    static async Task Select(string kind,int count,string model,string tag)
    {
        await Wait(()=>ChoiceKind()==kind,"remote native "+kind);
        for(int i=0;i<count;i++) {await Remote("select",model);await Frames(10);}
        await Frames(12);
        if(ChoiceKind()==kind&&Authority().GetProperty("Actions").EnumerateArray().Any(a=>a.GetProperty("Kind").GetString()=="choice"&&a.GetProperty("Id").GetString()!.StartsWith("confirm:")))await Remote("choice");
        await Barrier(tag);
    }
    static async Task Run()
    {
        int exit=0;
        try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Frames(60);Engine.MaxFps=30;SaveManager.Instance.SetFtuesEnabled(false);Static("LiveSharingController","Suspend");
            AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.PayloadMessageRegistration")).First(t=>t!=null)!.GetMethod("EnsureRegistered",Any)!.Invoke(null,null);
            ushort port=ushort.Parse(Env.GetEnvironmentVariable("RMP_PHASE3_PORT")!);INetGameService service;
            if(Host){var host=new NetHostGameService(PeerVersionInfo.LocalDefault());service=host;if(host.StartENetHost(port,2).HasValue)throw new Exception("ENet host failed");Write("host-listening.json",new{port});ulong end=Time.GetTicksMsec()+60000;while(host.ConnectedPeers.Count==0&&Time.GetTicksMsec()<end){host.Update();await Frames();}if(host.ConnectedPeers.Count!=1)throw new Exception("Client missing");host.SetPeerReadyForBroadcasting(2);}
            else{await Wait(()=>File.Exists(Path.Combine(Root,"host-listening.json")),"listen");var client=new NetClientGameService(PeerVersionInfo.LocalDefault());service=client;var transport=new ENetClient(client);client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);if((await transport.ConnectToHost(2,"127.0.0.1",port)).HasValue)throw new Exception("ENet client failed");}
            var players=new[]{Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1),Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),2)};
            var state=RunState.CreateForNewRun(players,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPMULTIPHASE3");
            foreach(var player in players)
            {
                foreach(var relic in new RelicModel[]{ModelDb.Relic<ChoicesParadox>(),ModelDb.Relic<BagOfPreparation>(),ModelDb.Relic<RingOfTheSnake>(),ModelDb.Relic<PhilosophersStone>(),ModelDb.Relic<Lantern>(),ModelDb.Relic<Ectoplasm>()})player.AddRelicInternal(relic.ToMutable(),silent:true);
                player.Deck.Clear(true);var grit=state.CreateCard<TrueGrit>(player);grit.UpgradeInternal();grit.FinalizeUpgradeInternal();player.Deck.AddInternal(grit,silent:true);
                player.Deck.AddInternal(state.CreateCard<Armaments>(player),silent:true);player.Deck.AddInternal(state.CreateCard<Headbutt>(player),silent:true);player.Deck.AddInternal(state.CreateCard<Discovery>(player),silent:true);var prepared=state.CreateCard<Prepared>(player);prepared.UpgradeInternal();prepared.FinalizeUpgradeInternal();player.Deck.AddInternal(prepared,silent:true);
                for(int i=0;i<4;i++)player.Deck.AddInternal(state.CreateCard<DefendIronclad>(player),silent:true);
                player.AddPotionInternal(ModelDb.Potion<FocusPotion>().ToMutable(),silent:true);player.AddPotionInternal(ModelDb.Potion<BlockPotion>().ToMutable(),silent:true);player.AddPotionInternal(ModelDb.Potion<FirePotion>().ToMutable(),silent:true);
            }
            RunManager.Instance.SetUpTest(state,service,disableCombatStateSync:false,shouldSave:false);RunManager.Instance.GenerateRooms();await PreloadManager.LoadRunAssets(players.Select(p=>p.Character));RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);
            var entry=RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MapPointType.Monster).coord);
            await Wait(()=>NOverlayStack.Instance?.Peek()!=null,"opening grid");Static("LiveSharingController","SelectMultiplayerSource",(Host?2UL:1UL).ToString());await Wait(()=>(bool)Get(Proc,"DisplayReady")!,"native window");await Barrier("windows");
            Check(!Child().GetProperty("control").GetBoolean(),"initial remote window has no gameplay authority");
            int rejected=(int)Session("RejectedControlRequests")!;
            var fake=JsonSerializer.SerializeToElement(new{Epoch=-1,Request=1,Generation=Get(null,"_receivedGeneration",Rmp("MultiplayerMirrorSession")),Events=0,NodeKey="end:1",Value="forged"});Forward(fake);await Barrier("unauthorized");await Wait(()=>(int)Session("RejectedControlRequests")!>rejected,"unauthorized rejected");Check((int)Session("AcceptedControlRequests")! ==0,"ungranted network request cannot make a decision");
            await Grant("denied",true);await Grant("granted");await Select("Grid",1,"","grid-input");await entry;await Sync(state,"grid-result");
            var me=LocalContext.GetMe(state)!;
            // Both real players act while two controllers send requests. A stale
            // private context must not spend resources or select a shifted card.
            var old=Authority().Clone();int accepted=(int)Session("AcceptedControlRequests")!,beforeReject=(int)Session("RejectedControlRequests")!;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState!.Hand.Cards.OfType<DefendIronclad>().First(),null));await Wait(()=>!RunManager.Instance.ActionExecutor.IsRunning,"own native input");await Barrier("own-input");
            var stale=JsonSerializer.SerializeToElement(new{Epoch=old.GetProperty("Epoch").GetInt64(),Request=999,Generation=Get(null,"_receivedGeneration",Rmp("MultiplayerMirrorSession")),Events=0,NodeKey=old.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()=="play").GetProperty("Id").GetString(),Value=old.GetProperty("Context").GetString()});Forward(stale);await Barrier("stale-input");await Wait(()=>(int)Session("RejectedControlRequests")!>beforeReject,"stale private context rejected");Check((int)Session("AcceptedControlRequests")! ==accepted,"simultaneous own input invalidates stale controller request");
            await Wait(()=>Authority().GetProperty("Context").GetString()!=old.GetProperty("Context").GetString(),"fresh own state metadata");var fresh=Authority();beforeReject=(int)Session("RejectedControlRequests")!;
            await Barrier("future-baseline");
            var future=JsonSerializer.SerializeToElement(new{Epoch=fresh.GetProperty("Epoch").GetInt64(),Request=1000,Generation=Get(null,"_receivedGeneration",Rmp("MultiplayerMirrorSession")),Events=int.MaxValue,NodeKey=fresh.GetProperty("Actions").EnumerateArray().First(a=>a.GetProperty("Kind").GetString()=="play").GetProperty("Id").GetString(),Value=fresh.GetProperty("Context").GetString()});Forward(future);await Barrier("future-events");await Wait(()=>(int)Session("RejectedControlRequests")!>beforeReject,"future event prefix rejected");Check((int)Session("AcceptedControlRequests")! ==accepted,"future event prefix cannot execute a current otherwise legal card");
            Static("MultiplayerMirrorSession","RevokeControl");await Barrier("revoke");await Frames(20);Check(!Child().GetProperty("control").GetBoolean(),"source revocation disables renderer authority");Forward(stale);await Barrier("old-epoch");await Frames(10);Check((int)Session("AcceptedControlRequests")! ==accepted,"revoked epoch cannot execute delayed request");
            await Grant("regrant");await Remote("potion","FocusPotion");await Sync(state,"self-potion");Check(!me.Potions.OfType<FocusPotion>().Any(),"untargeted potion executed for selected source");
            await Remote("potion","BlockPotion",target:-3);await Sync(state,"ally-potion");Check(!me.Potions.OfType<BlockPotion>().Any(),"multiplayer potion native targeting supports another living player");
            var last=await Remote("potion","FirePotion",target:-2);await Sync(state,"target-potion");Check(!me.Potions.OfType<FirePotion>().Any(),"targeted potion original action consumes correct source slot");accepted=(int)Session("AcceptedControlRequests")!;Forward(last);Forward(last);await Barrier("duplicates");await Frames(20);Check((int)Session("AcceptedControlRequests")! ==accepted,"duplicate reliable request returns cached result without repeating action");
            await Remote("play","Prepared");await Select("Hand",2,"DEFEND","prepared-select");await Sync(state,"prepared-result");
            await Remote("play","TrueGrit");await Select("Hand",1,"DEFEND","grit-select");await Sync(state,"grit-result");
            await Remote("play","Armaments");await Select("Upgrade",1,"","upgrade-select");await Sync(state,"upgrade-result");
            await Remote("play","Headbutt",target:-2);await Select("Pile",1,"","pile-select");await Sync(state,"pile-result");
            await Remote("play","Discovery");await Select("Choose",1,"","choose-select");await Sync(state,"choose-result");
            await Remote("endTurn");await Wait(()=>players.All(p=>p.PlayerCombatState!.TurnNumber>=2),"both controllers ended original turn");await Sync(state,"end-turn-result");Check(me.PlayerCombatState!.TurnNumber==2,"remote end-turn follows original all-player advancement");
            Rmp("MultiplayerMirrorSession").GetField("_nextControlHeartbeat",Any)!.SetValue(null,ulong.MaxValue);
            await Barrier("heartbeat-stopped");await Wait(()=>Get(null,"_controlViewer",Rmp("MultiplayerMirrorSession")) is ulong viewer&&viewer==0,"15-second control heartbeat expires",20);await Barrier("lease-expired");await Wait(()=>!Child().GetProperty("control").GetBoolean(),"expired permission reaches renderer");Check(!Child().GetProperty("control").GetBoolean(),"lost control heartbeat revokes the source capability and native replica");
            Rmp("MultiplayerMirrorSession").GetField("_nextControlHeartbeat",Any)!.SetValue(null,0UL);
            await Grant("grant-before-source-change");
            // Source changes revoke the exact old subscription and its grant.
            Static("LiveSharingController","SelectMultiplayerSource",me.NetId.ToString());await Barrier("source-change");await Frames(25);Check(Get(null,"_controlViewer",Rmp("MultiplayerMirrorSession")) is ulong owner&&owner==0,"switching viewer source revokes its grant at remote game");
            await Wait(()=>(bool)Get(Proc,"DisplayReady")!,"self replica");Static("MultiplayerMirrorSession","RequestControl",true);await Wait(()=>Child().GetProperty("control").GetBoolean(),"self view permission");
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure,showTransition:false);await Barrier("room-exit");await Frames(20);Check(Get(null,"_controlViewer",Rmp("MultiplayerMirrorSession")) is ulong owner2&&owner2==0,"leaving combat revokes own and remote battle capability");Check(!(bool)Get(Proc,"DisplayReady")!,"noncombat boundary does not accept combat UI operations");
            Write(Role+"-result.json",new{passed=true,checks,headless=true,scope="phase3 permission and original battle control"});await Barrier("done");
        }
        catch(Exception e){exit=1;Write(Role+"-result.json",new{passed=false,checks,error=e.ToString()});GD.PrintErr("[MultiplayerPhase3] FAIL "+e);}
        finally{Static("LiveSharingController","Suspend");Static("MultiplayerMirrorSession","Stop");((SceneTree)Engine.GetMainLoop()).Quit(exit);}
    }
}
