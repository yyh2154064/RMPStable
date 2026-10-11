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

// Independent stage four probe. The shared setup only launches isolated native peers.
[ModInitializer(nameof(Initialize))]
public static class MultiplayerPhase4Probe
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static bool Renderer => Env.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    static bool Host => Env.GetEnvironmentVariable("RMP_PHASE4_ROLE") == "host";
    static string Root => Renderer ? Env.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")! : Env.GetEnvironmentVariable("RMP_PHASE4_EVIDENCE")!;
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
    static async Task DumpSource()
    {
        string last="";while(true){await Frames(3);var state=RunManager.Instance.DebugOnlyGetState();if(state==null)continue;Write(Role+"-journal.json",new{choice=Static("MultiplayerPersonalUi","Capture"),ids=RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds,operations=Get(null,"Operations",Rmp("MultiplayerNoncombat")),mapVotes=MapVotes(state)});string hash=Hash(state);if(hash==last)continue;last=hash;var save=RunManager.Instance.ToSave(null);save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0;save.NumReloads=0;save.MapDrawings=null;save.PlatformType=MegaCrit.Sts2.Core.Platform.PlatformType.None;Write(Role+"-saved.json",save);Write(Role+"-digest.json",new{hash});}
    }
    static async Task Wait(Func<bool> c,string label,int seconds=75)
    {ulong end=Time.GetTicksMsec()+(ulong)(seconds*1000); while(Time.GetTicksMsec()<end) {if(c())return; if(Session("Error") is string e && e.Length>0) throw new Exception(e); await Frames();} throw new Exception("Timeout: "+label+" phase="+Get(Session("Mirror") is {} m?Get(m,"Process"):null,"Phase",Rmp("MirrorProcess"))+" mirror="+Get(Session("Mirror"),"Error",Rmp("MirrorHost")));}
    static void Check(bool c,string label)
    {File.AppendAllText(Path.Combine(Root,Role+"-checks.jsonl"),JsonSerializer.Serialize(new{label,passed=c})+"\n"); if(!c)throw new Exception(label); checks++; GD.Print("[MultiplayerPhase4] PASS "+label);}
    static void Write(string name,object value) {string p=Path.Combine(Root,name); File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value)); File.Move(p+".tmp",p,true);}
    static JsonElement ReadPath(string p) {try{using var s=new FileStream(p,FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}catch{return default;}}
    static async Task Barrier(string stage) {Write(Role+"-"+stage+"-barrier.json",new{ready=true});await Wait(()=>File.Exists(Path.Combine(Root,(Host?"client":"host")+"-"+stage+"-barrier.json")),stage);}
    static JsonElement Child() => Session("Mirror")==null?default:ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"phase4-state.json"));
    static JsonElement Authority() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("authority"):default;}
    static JsonElement Personal() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("choice"):default;}
    static string ChoiceKind() => Personal().ValueKind==JsonValueKind.Object?Personal().GetProperty("Kind").GetString()!:"";
    static bool ChoiceMatches()
    {
        var local=Personal();var journal=ReadPath(Path.Combine(Root,(Host?"client":"host")+"-journal.json"));
        if(local.ValueKind!=JsonValueKind.Object||journal.ValueKind!=JsonValueKind.Object||journal.GetProperty("choice").ValueKind!=JsonValueKind.Object)return false;
        var source=journal.GetProperty("choice");return new[]{"Id","Kind","Cards","Eligible","Min","Max"}.All(p=>local.GetProperty(p).GetRawText()==source.GetProperty(p).GetRawText());
    }
    static int sequence;
    static void WritePath(string p,object value){File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value));File.Move(p+".tmp",p,true);}
    static IEnumerable<T> Descendants<T>(Node root) where T:Node
    {foreach(var node in root.GetChildren()){if(node is T t)yield return t;foreach(var child in Descendants<T>(node))yield return child;}}
    static string Inventory(MerchantRoom room,Player player)=>JsonSerializer.Serialize(room.Inventories[player.RunState.GetPlayerSlotIndex(player)].AllEntries.Select(e=>new{type=e.GetType().Name,e.Cost,e.IsStocked,model=e is MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry c?c.CreationResult?.Card.Id.ToString():e is MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry r?r.Model?.Id.ToString():e is MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry p?p.Model?.Id.ToString():""}));
    static string MapVotes(RunState state)=>JsonSerializer.Serialize(state.Players.Select(p=>new{id=p.NetId,vote=RunManager.Instance.MapSelectionSynchronizer.GetVote(p)?.coord.ToString(),ui=MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.PlayerVoteDictionary.TryGetValue(p,out var coord)==true?coord?.ToString():null}));
    static bool RestFront(RunState state)
    {
        if(state.CurrentRoom is not RestSiteRoom||NRun.Instance?.RestSiteRoom is not {} room||Get(room,"_characterContainers") is not IList seats)return false;
        int own=state.GetPlayerSlotIndex(LocalContext.GetMe(state)!);var controls=seats.Cast<Control>().ToList();return controls[own].ZIndex>controls.Where((_,i)=>i!=own).Max(c=>c.ZIndex);
    }
    public static void Initialize()
    {if(Env.GetEnvironmentVariable("RMP_PHASE4_TEST")!="1")return;TaskHelper.RunSafely(Renderer?Observe():Run());}
    static async Task Observe()
    {
        int handled=0,pendingSequence=0;string last="";object? pendingOutput=null;
        while(true)
        {
            await Frames(2);var renderer=Get(null,"Active",Rmp("MirrorRenderer"));var state=RunManager.Instance.DebugOnlyGetState();if(renderer==null||state==null)continue;
            try
            {
                if(pendingOutput!=null){WritePath(Path.Combine(Root,"phase4-output.json"),pendingOutput);handled=pendingSequence;pendingOutput=null;}
                if(Hash(state)!=last){last=Hash(state);var save=RunManager.Instance.ToSave(null);save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0;save.NumReloads=0;save.MapDrawings=null;save.PlatformType=MegaCrit.Sts2.Core.Platform.PlatformType.None;WritePath(Path.Combine(Root,"phase4-saved.json"),save);}
                var request=ReadPath(Path.Combine(Root,"phase4-input.json"));
                if(request.ValueKind==JsonValueKind.Object&&request.GetProperty("seq").GetInt32()!=handled&&Get(renderer,"MultiplayerInputReady") is true)
                {
                    string kind=request.GetProperty("kind").GetString()!,path=request.GetProperty("path").GetString()!;
                    var actions=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority")).GetProperty("Actions").EnumerateArray().Where(a=>a.GetProperty("Kind").GetString()==kind&&a.GetProperty("Enabled").GetBoolean()&&a.GetProperty("NativePath").GetString()!.Contains(path)).ToList();
                    if(actions.Count<=request.GetProperty("index").GetInt32())continue;
                    var action=actions[request.GetProperty("index").GetInt32()];
                    var node=Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,true) as Control;if(node==null||!node.IsVisibleInTree())continue;
                    var hit=node is NCardHolder h?h.Hitbox:node is MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot s?s.Hitbox:node;
                    Vector2 point=hit.GetGlobalTransformWithCanvas()*(hit.Size/2)/NGame.Instance.GetViewport().GetVisibleRect().Size;
                    long before=(long)Get(renderer,"_request")!;var queue=(System.Collections.Concurrent.ConcurrentQueue<(uint,Vector2,long,long,float)>)Get(renderer,"_buttons")!;
                    queue.Enqueue((0x201,point,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!,0));await Frames(2);
                    queue.Enqueue((0x202,point,(long)Get(renderer,"_generation")!,(long)Get(renderer,"_epoch")!,0));
                    for(int i=0;i<50&&(long)Get(renderer,"_request")! ==before;i++)await Frames();
                    long issued=(long)Get(renderer,"_request")!;
                    // The native UI can move or disable a button while its
                    // entrance animation runs. No submitted request means no
                    // source operation; retry using the next advertised path.
                    if(issued==before){await Frames(6);continue;}
                    for(int i=0;i<120&&(long)Get(renderer,"LastMultiplayerResult")! < issued;i++)await Frames();
                    WritePath(Path.Combine(Root,"phase4-response.json"),new{seq=request.GetProperty("seq").GetInt32(),before,issued,current=Get(renderer,"_request"),result=Get(renderer,"LastMultiplayerResult"),accepted=Get(renderer,"LastMultiplayerAccepted"),generation=Get(renderer,"_generation"),epoch=Get(renderer,"_epoch")});
                    if((long)Get(renderer,"LastMultiplayerResult")! !=issued)throw new Exception("Source response missing for native room operation "+kind+" "+path);
                    // Native option entrance animations can enable another
                    // button between snapshots. A rejected stale click has no
                    // effect; try again from refreshed source authority.
                    if(Get(renderer,"LastMultiplayerAccepted") is not true){await Frames(6);continue;}
                    await Frames(8);
                    pendingSequence=request.GetProperty("seq").GetInt32();
                    pendingOutput=new{seq=pendingSequence,issued=(long)Get(renderer,"_request")!>before,request=new{Epoch=Get(renderer,"_epoch"),Request=Get(renderer,"_request"),Generation=Get(renderer,"_generation"),Events=Get(renderer,"_events"),NodeKey=action.GetProperty("Id").GetString(),Value=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority")).GetProperty("Context").GetString(),TargetKey=""}};
                    WritePath(Path.Combine(Root,"phase4-output.json"),pendingOutput);handled=pendingSequence;pendingOutput=null;
                }
                WritePath(Path.Combine(Root,"phase4-state.json"),new{authority=Get(renderer,"_battleAuthority"),choice=Static("MultiplayerPersonalUi","Capture"),overlay=NOverlayStack.Instance?.Peek()?.GetType().Name,ids=RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds,personalKind=Get(null,"_kind",Rmp("MultiplayerPersonalUi")),control=Get(renderer,"_control"),inputReady=Get(renderer,"MultiplayerInputReady"),hash=Hash(state),events=Get(renderer,"_events"),local=LocalContext.NetId,generation=Get(renderer,"_generation"),room=state.CurrentRoom?.RoomType.ToString(),restFront=RestFront(state),mapVotes=MapVotes(state),inventory=state.CurrentRoom is MerchantRoom shop?Inventory(shop,LocalContext.GetMe(state)!):"",waiting=Get(renderer,"_waitingForAuthority"),epoch=Get(renderer,"_epoch"),map=MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen,error=Get(renderer,"_error")});
            }catch(IOException){}catch(UnauthorizedAccessException){}
            catch(Exception e){var request=ReadPath(Path.Combine(Root,"phase4-input.json"));handled=request.GetProperty("seq").GetInt32();WritePath(Path.Combine(Root,"phase4-output.json"),new{seq=handled,issued=false,error=e.ToString(),request=new{}});GD.PrintErr("[MultiplayerPhase4] Renderer "+e);}
        }
    }
    static async Task<JsonElement> Remote(string kind,string path="",int index=0)
    {
        int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;WritePath(Path.Combine(dir,"phase4-input.json"),new{seq,kind,path,index});
        await Wait(()=>ReadPath(Path.Combine(dir,"phase4-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"phase4-output.json")).GetProperty("seq").GetInt32()==seq,"native replica click "+kind+" "+path);
        var output=ReadPath(Path.Combine(dir,"phase4-output.json"));Check(output.GetProperty("issued").GetBoolean(),"authorized native replica mouse "+kind+" "+path);return output.GetProperty("request").Clone();
    }
    static async Task Grant(string tag)
    {
        Static("MultiplayerMirrorSession","RequestControl",true);
        await Wait(()=>Session("_pendingViewer") is ulong id&&id!=0,"room permission request");
        Check(Session("_controlViewer") is ulong id&&id==0,"another source requires explicit permission for "+tag);
        await Wait(()=>Session("_allowButton") is Button,"permission UI");((Button)Session("_allowButton")!).EmitSignal(Button.SignalName.Pressed);
        await Barrier("grant-"+tag);await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("control").GetBoolean()&&Child().GetProperty("inputReady").GetBoolean(),"room grant reaches replica");
    }
    static async Task Sync(RunState state,string tag)
    {await Barrier(tag+"-source-ready");await Frames(4);await Wait(()=>(bool)Get(Mirror,"Verified")!&&ReadPath(Path.Combine(Root,(Host?"client":"host")+"-digest.json")).GetProperty("hash").GetString()==(string)Get(Proc,"LastHash")!&&ChoiceKind()=="",tag);Check((string)Get(Proc,"LastHash")! ==(string)Get(Mirror,"SourceHash")!,tag+" matches selected source's original all-player state");await Barrier(tag);}
    static async Task Enter(RoomType type,string tag,MegaCrit.Sts2.Core.Models.AbstractModel? model=null)
    {
        await Barrier("before-"+tag);await RunManager.Instance.EnterRoomDebug(type,model:model,showTransition:false);await Barrier("entered-"+tag);
        await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("room").GetString()==type.ToString()&&(bool)Get(Proc,"DisplayReady")!,"native "+tag+" room");
        Check(!Child().GetProperty("control").GetBoolean(),tag+" invalidates old room grant");
    }
    static async Task Run()
    {
        int exit=0;
        try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");await Frames(60);Engine.MaxFps=30;SaveManager.Instance.SetFtuesEnabled(false);Static("LiveSharingController","Suspend");
            AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.PayloadMessageRegistration")).First(t=>t!=null)!.GetMethod("EnsureRegistered",Any)!.Invoke(null,null);
            ushort port=ushort.Parse(Env.GetEnvironmentVariable("RMP_PHASE4_PORT")!);INetGameService service;
            if(Host){var host=new NetHostGameService(PeerVersionInfo.LocalDefault());service=host;if(host.StartENetHost(port,2).HasValue)throw new Exception("ENet host failed");Write("host-listening.json",new{port});ulong end=Time.GetTicksMsec()+60000;while(host.ConnectedPeers.Count==0&&Time.GetTicksMsec()<end){host.Update();await Frames();}if(host.ConnectedPeers.Count!=1)throw new Exception("Client missing");host.SetPeerReadyForBroadcasting(2);}
            else{await Wait(()=>File.Exists(Path.Combine(Root,"host-listening.json")),"listen");var client=new NetClientGameService(PeerVersionInfo.LocalDefault());service=client;var transport=new ENetClient(client);client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);if((await transport.ConnectToHost(2,"127.0.0.1",port)).HasValue)throw new Exception("ENet client failed");}
            var players=new[]{Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1),Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),2)};
            var state=RunState.CreateForNewRun(players,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPMULTIPHASE4");
            foreach(var p in players){p.Gold=1000;p.Creature.SetCurrentHpInternal(40);}
            RunManager.Instance.SetUpTest(state,service,disableCombatStateSync:false,shouldSave:false);_ = TaskHelper.RunSafely(DumpSource());RunManager.Instance.GenerateRooms();await PreloadManager.LoadRunAssets(players.Select(p=>p.Character));RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);await Frames(30);await Barrier("setup");
            Static("LiveSharingController","SelectMultiplayerSource",(Host?2UL:1UL).ToString());await Wait(()=>(bool)Get(Proc,"DisplayReady")!,"initial native map replica");Write(Role+"-replica-path.json",new{directory=Get(Proc,"DirectoryPath")});await Barrier("windows");
            Check(Child().GetProperty("local").GetUInt64()==(Host?2UL:1UL),"noncombat replica binds selected original player");await Sync(state,"initial-map");
            await Enter(RoomType.Shop,"shop");await Sync(state,"shop-inventory");
            var me=LocalContext.GetMe(state)!;Write(Role+"-inventory.json",new{inventory=Inventory((MerchantRoom)state.CurrentRoom!,me)});await Barrier("inventory-data");
            Check(Child().GetProperty("inventory").GetString()==ReadPath(Path.Combine(Root,(Host?"client":"host")+"-inventory.json")).GetProperty("inventory").GetString(),"selected player's generated inventory, prices and stock match");
            foreach(var inventory in Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory>(NRun.Instance.MerchantRoom))inventory.Open();await Frames(20);await Grant("shop");
            int gold=me.Gold;await Barrier("before-card-purchase");var duplicate=await Remote("buy","NMerchantCard#",0);await Wait(()=>me.Gold<gold,"native merchant purchase completes");await Sync(state,"card-purchase");Check(me.Gold<gold,"source native merchant purchase spends selected player's gold");
            int accepted=(int)Session("AcceptedControlRequests")!;var resend=JsonSerializer.Deserialize(duplicate.GetRawText(),Rmp("MirrorMessage"))!;Static("MultiplayerMirrorSession","ForwardControlIntent",resend);await Barrier("purchase-duplicate");await Frames(15);Check((int)Session("AcceptedControlRequests")! ==accepted,"reliable purchase duplicate does not buy a second entry");
            int relics=me.Relics.Count,potions=me.Potions.Count();await Barrier("before-relic-purchase");await Remote("buy","NMerchantRelic");await Wait(()=>me.Relics.Count>relics,"native relic purchase completes");await Sync(state,"merchant-relic");await Barrier("before-potion-purchase");await Remote("buy","NMerchantPotion");await Wait(()=>me.Potions.Count()>potions,"native potion purchase completes");await Sync(state,"merchant-potion");
            Write(Role+"-purchased-inventory.json",new{inventory=Inventory((MerchantRoom)state.CurrentRoom!,me)});await Barrier("purchased-inventory");
            Check(Child().GetProperty("inventory").GetString()==ReadPath(Path.Combine(Root,(Host?"client":"host")+"-purchased-inventory.json")).GetProperty("inventory").GetString(),"card, relic and potion stock follows the selected source's actual purchases");
            int deckCount=me.Deck.Cards.Count;await Barrier("before-removal");await Remote("buy","NMerchantCardRemoval");await Wait(()=>ChoiceKind()=="Deck"&&ChoiceMatches(),"native removal chooser");Check(Personal().GetProperty("Cards").GetArrayLength()>0&&ChoiceMatches(),"removal candidates and constraints match the selected source's real deck");await Remote("select");
            await Wait(()=>Personal().GetProperty("Preview").GetBoolean(),"source removal preview");await Remote("native","NBackButton");await Wait(()=>!Personal().GetProperty("Preview").GetBoolean(),"source cancels removal preview");
            Check(me.Deck.Cards.Count==deckCount,"cancelled removal preview keeps source deck intact");await Remote("select","",1);await Frames(20);await Remote("native","NConfirmButton");await Wait(()=>me.Deck.Cards.Count==deckCount-1,"confirmed native removal completes");await Sync(state,"merchant-removal");Check(me.Deck.Cards.Count==deckCount-1,"only confirmed original removal changes the source deck");
            await Enter(RoomType.RestSite,"rest");await Sync(state,"rest-entry");
            Check(RestFront(state),"source rest portrait foreground follows its own player without slot reordering");Check(Child().GetProperty("restFront").GetBoolean(),"replica rest portrait foreground follows selected source without slot reordering");
            int rejected=(int)Session("RejectedControlRequests")!;Static("MultiplayerMirrorSession","ForwardControlIntent",resend);await Barrier("old-shop-request");await Frames(15);Check((int)Session("RejectedControlRequests")!>rejected,"old shop request is rejected after room checkpoint changes");await Grant("rest");
            await Remote("native","NRestSiteButton",1);await Wait(()=>ChoiceKind()=="DeckUpgrade"&&ChoiceMatches(),"native smith chooser");Check(ChoiceMatches(),"smith candidates and constraints match the selected source's real deck");await Remote("select");await Frames(20);await Remote("native","NConfirmButton");await Wait(()=>me.Deck.Cards.Any(c=>c.CurrentUpgradeLevel>0),"native smith completes");await Sync(state,"smith");Check(me.Deck.Cards.Any(c=>c.CurrentUpgradeLevel>0),"source native smith upgrades actual source deck");
            await Enter(RoomType.RestSite,"heal");await Sync(state,"heal-entry");await Grant("heal");decimal hp=me.Creature.CurrentHp;await Barrier("before-heal");await Remote("native","NRestSiteButton",0);await Wait(()=>me.Creature.CurrentHp>hp,"native rest completes");await Sync(state,"heal-result");Check(me.Creature.CurrentHp>hp,"original rest restores only the acting player's health");
            await Enter(RoomType.Treasure,"treasure");await Sync(state,"treasure-entry");await Grant("treasure");await Remote("native","NTreasureButton");await Frames(40);await Remote("native","NTreasureRoomRelicHolder");await Sync(state,"treasure-votes");
            await Enter(RoomType.Event,"personal-event",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.ThisOrThat>());await Sync(state,"personal-event-entry");await Grant("personal-event");
            if(Host)await Remote("native","NEventOptionButton",1);await Barrier("one-personal-option");await Frames(20);
            Check(RunManager.Instance.EventSynchronizer.GetEventForPlayer(me).IsFinished==!Host,"personal event completion belongs only to the selected player");
            if(!Host)await Remote("native","NEventOptionButton",1);await Sync(state,"personal-event-option");
            await Enter(RoomType.Event,"shared-event",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.DenseVegetation>());await Sync(state,"shared-event-entry");await Grant("shared-event");
            if(Host)await Remote("native","NEventOptionButton",0);await Barrier("one-shared-vote");await Frames(20);
            Check(!RunManager.Instance.EventSynchronizer.GetEventForPlayer(me).IsFinished,"one shared-event vote waits for the team's original decision");
            if(!Host)await Remote("native","NEventOptionButton",0);await Frames(35);await Sync(state,"shared-event-votes");
            await Wait(()=>RunManager.Instance.EventSynchronizer.GetEventForPlayer(me).IsFinished,"shared event finished");await Remote("native","NEventOptionButton");await Wait(()=>Child().GetProperty("map").GetBoolean(),"source completed event opens native map");await Barrier("map-open");
            long generation=(long)Session("_receivedGeneration")!;
            if(Host){await Remote("native","MapPoint@");await Frames(15);Write("first-map-vote.json",new{ready=true});}
            else await Wait(()=>File.Exists(Path.Combine(Root,"first-map-vote.json")),"one player's map vote");
            await Frames(10);Check((long)Session("_receivedGeneration")! ==generation,"one map vote does not advance the team or passive replica");await Wait(()=>Child().GetProperty("mapVotes").GetString()==ReadPath(Path.Combine(Root,(Host?"client":"host")+"-journal.json")).GetProperty("mapVotes").GetString(),"native map vote indicators follow selected source");Check(Child().GetProperty("mapVotes").GetString()==ReadPath(Path.Combine(Root,(Host?"client":"host")+"-journal.json")).GetProperty("mapVotes").GetString(),"all player map vote indicators, including selected player, match source");await Barrier("one-vote");
            if(!Host)await Remote("native","MapPoint@");
            await Wait(()=>(long)Session("_receivedGeneration")!>generation&&Child().GetProperty("generation").GetInt64()>generation&&(bool)Get(Proc,"DisplayReady")!&&!Child().GetProperty("control").GetBoolean(),"host-authoritative all-player map travel");Check(!Child().GetProperty("control").GetBoolean(),"map travel revokes previous room authority");await Barrier("map-travel");
            await Enter(RoomType.Event,"card-rewards",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.BrainLeech>());await Sync(state,"card-rewards-entry");await Grant("card-rewards");deckCount=me.Deck.Cards.Count;await Barrier("before-event-card-reward");await Remote("native","NEventOptionButton",1);
            await Remote("native","NRewardButton");await Wait(()=>ChoiceKind()=="Reward"&&ChoiceMatches(),"real card reward options");Check(Personal().GetProperty("Cards").GetArrayLength()>0&&ChoiceMatches(),"native card reward candidates match the selected source");await Remote("select");await Wait(()=>me.Deck.Cards.Count>deckCount,"original event card reward completes");await Sync(state,"event-card-reward");
            await Enter(RoomType.Event,"skipped-reward",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.BrainLeech>());await Sync(state,"skipped-reward-entry");await Grant("skipped-reward");
            deckCount=me.Deck.Cards.Count;await Remote("native","NEventOptionButton",1);await Remote("native","NProceedButton");await Wait(()=>NOverlayStack.Instance.Peek() is not MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen,"original personal reward skip closes source overlay");await Sync(state,"skipped-reward-result");
            await Wait(()=>Child().GetProperty("overlay").GetString()!="NRewardsScreen","original personal reward skip closes replica overlay");Check(Child().GetProperty("overlay").GetString()!="NRewardsScreen","skipped personal reward closes the replica's original overlay");
            Check(me.Deck.Cards.Count==deckCount,"skipping the original personal reward does not add a card");
            await Enter(RoomType.Monster,"terminal-rewards-fixture");await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning,"reward fixture combat initialized");await Barrier("reward-fixture-ready");
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState().Enemies.ToList(),true);
            await CombatManager.Instance.CheckWinCondition();
            await Wait(()=>!CombatManager.Instance.IsInProgress&&NOverlayStack.Instance.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen,"original battle-end reward page");await Barrier("battle-ended");
            await Wait(()=>Child().GetProperty("room").GetString()=="Monster"&&Child().GetProperty("choice").ValueKind==JsonValueKind.Null&&(bool)Get(Mirror,"Verified")!,"postcombat checkpoint follows stopped replay writer");await Grant("terminal-rewards");
            gold=me.Gold;await Barrier("before-combat-gold");await Remote("native","NRewardButton",0);await Wait(()=>me.Gold>gold,"original combat gold reward completes");await Frames(20);await Sync(state,"terminal-gold-reward");
            await Remote("native","NProceedButton");await Wait(()=>Child().GetProperty("map").GetBoolean(),"original terminal reward proceed opens the map");Check(Child().GetProperty("map").GetBoolean(),"completed combat reward page follows original map proceed");
            Check(state.Players.Count==2&&Child().GetProperty("local").GetUInt64()!=LocalContext.NetId,"all original slots preserved through noncombat transitions");
            Write(Role+"-result.json",new{passed=true,checks,headless=true,scope="phase4 native noncombat rooms and source validated decisions"});await Barrier("done");
        }
        catch(Exception e){exit=1;Write(Role+"-result.json",new{passed=false,checks,error=e.ToString()});GD.PrintErr("[MultiplayerPhase4] FAIL "+e);}
        finally{Static("LiveSharingController","Suspend");Static("MultiplayerMirrorSession","Stop");((SceneTree)Engine.GetMainLoop()).Quit(exit);}
    }
}
