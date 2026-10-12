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
public static partial class MultiplayerVisibleProbe
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static bool Renderer => Env.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    static bool Host => Id==1;
    static string Root => Renderer ? Env.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")! : Env.GetEnvironmentVariable("RMP_VISIBLE_EVIDENCE")!;
    static string Role => Env.GetEnvironmentVariable("RMP_VISIBLE_ROLE")!;
    static ulong Id => Role=="me"?1UL:Role=="testerA"?2UL:3UL;
    static readonly string[] Roles=Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="openingSingle"?new[]{"me"}:new[]{"me","testerA","testerB"};
    static ulong DefaultSource => Id%3+1;
    static ulong SourceId => Session("Selected") is ulong id?id:DefaultSource;
    static string SourceRole => Roles[(int)SourceId-1];
    static int checks;
    static bool allowExpectedEndingOffline;
    static Type Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).First(t => t != null)!;
    static object? Get(object? o, string name, Type? type = null)
    { for (var t = o?.GetType() ?? type; t != null; t = t.BaseType) { if(t.GetProperty(name,Any|BindingFlags.DeclaredOnly) is {} p && (o!=null||p.GetMethod!.IsStatic)) return p.GetValue(o); if(t.GetField(name,Any|BindingFlags.DeclaredOnly) is {} f && (o!=null||f.IsStatic)) return f.GetValue(o); } return null; }
    static object? Call(object? o, string name, params object[] args) => o!.GetType().GetMethod(name,Any)!.Invoke(o,args);
    static object? Static(string type, string name, params object[] args)
    {var method=Rmp(type).GetMethod(name,Any)!;if(method.IsGenericMethodDefinition)method=method.MakeGenericMethod(args[0].GetType());return method.Invoke(null,args);}
    static object? Session(string name) => Get(null,name,Rmp("MultiplayerMirrorSession"));
    static object? View => Get(null,"_view",Rmp("LiveSharingController"));
    static object Mirror => Session("Mirror")!;
    static object Proc => Get(Mirror,"Process")!;
    static int Pid => ((Process)Get(Proc,"Child")!).Id;
    static string Hash(RunState state) => (string)Static("MirrorState","MultiplayerHash",state)!;
    static string GameplayHash(RunState state)
    {
        var save=RunManager.Instance.ToSave(null);
        save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0;save.NumReloads=0;save.MapDrawings=null;
        save.PlatformType=MegaCrit.Sts2.Core.Platform.PlatformType.None;
        foreach(var player in save.Players){player.DiscoveredCards.Clear();player.DiscoveredEnemies.Clear();player.DiscoveredEpochs.Clear();player.DiscoveredPotions.Clear();player.DiscoveredRelics.Clear();}
        // Vanilla peers bookkeep local merchant spending / card acquisition
        // history differently. Compare gameplay across peers, while the separate
        // production hash still requires exact selected-source history in its replica.
        save.MapPointHistory=new();
        byte[] run=(byte[])Static("MirrorState","Pack",save)!;
        byte[] combat=state.CurrentRoom is CombatRoom&&(CombatManager.Instance.IsInProgress||CombatManager.Instance.IsStarting)?
            (byte[])Static("MirrorState","Pack",MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.FromRun(state,null))!:Array.Empty<byte>();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(run.Concat(combat).ToArray()));
    }
    static async Task Frames(int n=1) {var tree=(SceneTree)Engine.GetMainLoop(); for(int i=0;i<n;i++){await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);FlushWrites();}}
    static async Task DumpSource()
    {
        string last="";while(true){await Frames(3);await VisibleStatus();var state=RunManager.Instance.DebugOnlyGetState();if(state==null)continue;Write(Role+"-crystal.json",CrystalSnapshot());Write(Role+"-journal.json",new{choice=Static("MultiplayerPersonalUi","Capture"),ids=RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds,operations=Get(null,"Operations",Rmp("MultiplayerNoncombat")),mapVotes=MapVotes(state)});string hash=Hash(state);if(hash==last)continue;last=hash;var save=RunManager.Instance.ToSave(null);save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0;save.NumReloads=0;save.MapDrawings=null;save.PlatformType=MegaCrit.Sts2.Core.Platform.PlatformType.None;Write(Role+"-saved.json",save);Write(Role+"-digest.json",new{hash,gameplay=GameplayHash(state)});}
    }
    static async Task Wait(Func<bool> c,string label,int seconds=75)
    {ulong end=Time.GetTicksMsec()+(ulong)(seconds*1000); while(Time.GetTicksMsec()<end) {if(c())return; if(Session("Error") is string e && e.Length>0&&!(allowExpectedEndingOffline&&e=="多人来源已离线")) throw new Exception(e); await Frames();} throw new Exception("Timeout: "+label+" phase="+Get(Session("Mirror") is {} m?Get(m,"Process"):null,"Phase",Rmp("MirrorProcess"))+" mirror="+Get(Session("Mirror"),"Error",Rmp("MirrorHost")));}
    static void Check(bool c,string label)
    {File.AppendAllText(Path.Combine(Root,Role+"-checks.jsonl"),JsonSerializer.Serialize(new{label,passed=c})+"\n"); if(!c)throw new Exception(label); checks++; GD.Print("[MultiplayerVisible] PASS "+label);}
    static void Write(string name,object value) => WritePath(Path.Combine(Root,name),value);
    static JsonElement ReadPath(string p) {try{using var s=new FileStream(p,FileMode.Open,System.IO.FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}catch{return default;}}
    static async Task Barrier(string stage) {Write(Role+"-"+stage+"-barrier.json",new{ready=true});await Wait(()=>Roles.All(role=>File.Exists(Path.Combine(Root,role+"-"+stage+"-barrier.json"))),stage);}
    static JsonElement Child() => Session("Mirror")==null?default:ReadPath(Path.Combine((string)Get(Proc,"DirectoryPath")!,"visible-state.json"));
    static JsonElement Authority() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("authority"):default;}
    static JsonElement Personal() {var c=Child();return c.ValueKind==JsonValueKind.Object?c.GetProperty("choice"):default;}
    static string ChoiceKind() => Personal().ValueKind==JsonValueKind.Object?Personal().GetProperty("Kind").GetString()!:"";
    static bool ChoiceMatches()
    {
        var local=Personal();var journal=ReadPath(Path.Combine(Root,SourceRole+"-journal.json"));
        if(local.ValueKind!=JsonValueKind.Object||journal.ValueKind!=JsonValueKind.Object||journal.GetProperty("choice").ValueKind!=JsonValueKind.Object)return false;
        var source=journal.GetProperty("choice");return new[]{"Id","Kind","Cards","Eligible","Min","Max"}.All(p=>local.GetProperty(p).GetRawText()==source.GetProperty(p).GetRawText());
    }
    static int sequence;
    static void WritePath(string p,object value)
    {
        // PowerShell/UI evidence readers can briefly deny replacement on Windows.
        // Keep the latest output pending; never terminate the source sampler.
        File.WriteAllText(p+".tmp",JsonSerializer.Serialize(value)); pendingWrites.Add(p);FlushWrites();
    }
    static readonly System.Collections.Generic.HashSet<string> pendingWrites=new();
    static void FlushWrites(){foreach(string p in pendingWrites.ToArray()){try{File.Move(p+".tmp",p,true);pendingWrites.Remove(p);}catch(IOException){}catch(UnauthorizedAccessException){}}}
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
    {if(Env.GetEnvironmentVariable("RMP_VISIBLE_TEST")!="1")return;InstallShopFeedback();InstallOpeningPlayback();TaskHelper.RunSafely(Renderer?Observe():Run());}
    static async Task Observe()
    {
        Process.GetCurrentProcess().PriorityClass=ProcessPriorityClass.BelowNormal;
        int handled=0,pendingSequence=0;string last="";object? pendingOutput=null;
        while(true)
        {
            await Frames(2);await VisibleStatus();var renderer=Get(null,"Active",Rmp("MirrorRenderer"));var state=RunManager.Instance.DebugOnlyGetState();if(renderer==null||state==null)continue;
            ObserveCatchup(renderer);
            // Hidden reconstruction cannot accept test input. Defer expensive
            // full-state/UI evidence sampling until the native scene is ready;
            // production synchronization and timing remain active throughout.
            if(Get(renderer,"_busy") is true)continue;
            try
            {
                bool finished=Roles.Any(role=>File.Exists(Path.Combine(Shared,role+"-finished.json")));
                if(!finished){ObserveShopFeedback();ObserveDeathVisuals();WritePath(Path.Combine(Root,"visible-crystal.json"),CrystalSnapshot());await ObserveEnding();await ObserveCursor(renderer);await ObserveWindowInput(renderer,state);await ObservePotionFeedback(renderer,state);await ObserveBattle(renderer,state);await ObserveReadonly(renderer,state);}if(pendingOutput!=null){WritePath(Path.Combine(Root,"visible-output.json"),pendingOutput);handled=pendingSequence;pendingOutput=null;}
                if(Hash(state)!=last){last=Hash(state);var save=RunManager.Instance.ToSave(null);save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0;save.NumReloads=0;save.MapDrawings=null;save.PlatformType=MegaCrit.Sts2.Core.Platform.PlatformType.None;WritePath(Path.Combine(Root,"visible-saved.json"),save);}
                var request=ReadPath(Path.Combine(Root,"visible-input.json"));
                if(!finished&&request.ValueKind==JsonValueKind.Object&&request.GetProperty("seq").GetInt32()!=handled&&Get(renderer,"MultiplayerInputReady") is true)
                {
                    string kind=request.GetProperty("kind").GetString()!,path=request.GetProperty("path").GetString()!;
                    var actions=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority")).GetProperty("Actions").EnumerateArray().Where(a=>a.GetProperty("Kind").GetString()==kind&&a.GetProperty("Enabled").GetBoolean()&&(path.StartsWith("=")?a.GetProperty("NativePath").GetString()!.EndsWith(path.Substring(1)):a.GetProperty("NativePath").GetString()!.Contains(path))).ToList();
                    if(actions.Count<=request.GetProperty("index").GetInt32())continue;
                    var action=actions[request.GetProperty("index").GetInt32()];
                    var node=Static("MirrorNativeUi","Resolve",action.GetProperty("NativePath").GetString()!,true) as Control;if(node==null||!node.IsVisibleInTree())continue;
                    var hit=node is NCardHolder h?h.Hitbox:node is MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantSlot s?s.Hitbox:node;
                    Vector2 point=NativePoint(hit);
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
                    WritePath(Path.Combine(Root,"visible-response.json"),new{seq=request.GetProperty("seq").GetInt32(),before,issued,current=Get(renderer,"_request"),result=Get(renderer,"LastMultiplayerResult"),accepted=Get(renderer,"LastMultiplayerAccepted"),generation=Get(renderer,"_generation"),epoch=Get(renderer,"_epoch")});
                    if((long)Get(renderer,"LastMultiplayerResult")! !=issued)throw new Exception("Source response missing for native room operation "+kind+" "+path);
                    // Native option entrance animations can enable another
                    // button between snapshots. A rejected stale click has no
                    // effect; try again from refreshed source authority.
                    if(Get(renderer,"LastMultiplayerAccepted") is not true){await Frames(6);continue;}
                    await Frames(15);
                    pendingSequence=request.GetProperty("seq").GetInt32();
                    pendingOutput=new{seq=pendingSequence,issued=(long)Get(renderer,"_request")!>before,request=new{Epoch=Get(renderer,"_epoch"),Request=Get(renderer,"_request"),Generation=Get(renderer,"_generation"),Events=Get(renderer,"_events"),NodeKey=action.GetProperty("Id").GetString(),Value=JsonSerializer.SerializeToElement(Get(renderer,"_battleAuthority")).GetProperty("Context").GetString(),TargetKey=""}};
                    WritePath(Path.Combine(Root,"visible-output.json"),pendingOutput);handled=pendingSequence;pendingOutput=null;
                }
                WritePath(Path.Combine(Root,"visible-state.json"),new{authority=Get(renderer,"_battleAuthority"),choice=Static("MultiplayerPersonalUi","Capture"),overlay=NOverlayStack.Instance?.Peek()?.GetType().Name,portraits=Portraits(),players=state.Players.Select(p=>p.NetId).ToArray(),service=RunManager.Instance.NetService.Type.ToString(),save=RunManager.Instance.ShouldSave,readonlyAttempt=readonlyAttempt,selectedRow=SelectedRow(state),ids=RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds,personalKind=Get(null,"_kind",Rmp("MultiplayerPersonalUi")),control=Get(renderer,"_control"),inputReady=Get(renderer,"MultiplayerInputReady"),hash=Hash(state),events=Get(renderer,"_events"),local=LocalContext.NetId,generation=Get(renderer,"_generation"),room=state.CurrentRoom?.RoomType.ToString(),restFront=RestFront(state),mapVotes=MapVotes(state),inventory=state.CurrentRoom is MerchantRoom shop?Inventory(shop,LocalContext.GetMe(state)!):"",waiting=Get(renderer,"_waitingForAuthority"),epoch=Get(renderer,"_epoch"),map=MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen,error=Get(renderer,"_error"),rendering=RenderingServer.RenderLoopEnabled,fastReplica=Get(null,"Replica",Rmp("MirrorFastRestore"))});
            }catch(IOException){}catch(UnauthorizedAccessException){}
            catch(Exception e){var request=ReadPath(Path.Combine(Root,"visible-input.json"));if(request.ValueKind==JsonValueKind.Object&&request.TryGetProperty("seq",out var seq)){handled=seq.GetInt32();WritePath(Path.Combine(Root,"visible-output.json"),new{seq=handled,issued=false,error=e.ToString(),request=new{}});}WritePath(Path.Combine(Root,"visible-observer-error.json"),new{error=e.ToString()});GD.PrintErr("[MultiplayerVisible] Renderer "+e);}
        }
    }
    static async Task<JsonElement> Remote(string kind,string path="",int index=0)
    {
        await Pace();int seq=++sequence;string dir=(string)Get(Proc,"DirectoryPath")!;WritePath(Path.Combine(dir,"visible-input.json"),new{seq,kind,path,index});
        await Wait(()=>ReadPath(Path.Combine(dir,"visible-output.json")).ValueKind==JsonValueKind.Object&&ReadPath(Path.Combine(dir,"visible-output.json")).GetProperty("seq").GetInt32()==seq,"native replica click "+kind+" "+path);
        var output=ReadPath(Path.Combine(dir,"visible-output.json"));Check(output.GetProperty("issued").GetBoolean(),"authorized native replica mouse "+kind+" "+path);return output.GetProperty("request").Clone();
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
    {await Barrier(tag+"-source-ready");await Frames(4);await Wait(()=>(bool)Get(Mirror,"Verified")!&&ReadPath(Path.Combine(Root,SourceRole+"-digest.json")).GetProperty("hash").GetString()==(string)Get(Proc,"LastHash")!&&ChoiceKind()=="",tag);Check((string)Get(Proc,"LastHash")! ==(string)Get(Mirror,"SourceHash")!,tag+" matches selected source's original all-player state including history");await Wait(()=>Roles.All(role=>ReadPath(Path.Combine(Root,role+"-digest.json")).GetProperty("gameplay").GetString()==GameplayHash(state)),tag+" original three-player gameplay agreement");Check(Roles.All(role=>ReadPath(Path.Combine(Root,role+"-digest.json")).GetProperty("gameplay").GetString()==GameplayHash(state)),tag+" all three original peers agree on run/combat gameplay");await Barrier(tag);}
    static async Task Enter(RoomType type,string tag,MegaCrit.Sts2.Core.Models.AbstractModel? model=null)
    {
        await Stage("第四阶段 · "+tag);await Barrier("before-"+tag);await RunManager.Instance.EnterRoomDebug(type,model:model,showTransition:false);await Barrier("entered-"+tag);
        await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("room").GetString()==type.ToString()&&(bool)Get(Proc,"DisplayReady")!,"native "+tag+" room");
        Check(!Child().GetProperty("control").GetBoolean(),tag+" invalidates old room grant");
    }
    static async Task Run()
    {
        int exit=0;
        try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");Static("LiveSharingController","Suspend");await ConfigureVisible();await Frames(60);Engine.MaxFps=30;SaveManager.Instance.SetFtuesEnabled(false);
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="openingSingle"){await TestOpeningSingle();return;}
            var warmHost=Get(null,"Host",Rmp("MirrorResident"));int warmPid=warmHost==null?0:((Process)Get(Get(warmHost,"Process"),"Child")!).Id;
            Write(Role+"-prewarm.json",new{pid=warmPid});
            AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.PayloadMessageRegistration")).First(t=>t!=null)!.GetMethod("EnsureRegistered",Any)!.Invoke(null,null);
            ushort port=ushort.Parse(Env.GetEnvironmentVariable("RMP_VISIBLE_PORT")!);INetGameService service;
            if(Host){var host=new NetHostGameService(PeerVersionInfo.LocalDefault());service=host;if(host.StartENetHost(port,3).HasValue)throw new Exception("ENet host failed");_ = TaskHelper.RunSafely(PumpNetwork(host));Write("host-listening.json",new{port});ulong end=Time.GetTicksMsec()+60000;while(host.ConnectedPeers.Count<2&&Time.GetTicksMsec()<end){host.Update();await Frames();}if(host.ConnectedPeers.Count!=2)throw new Exception("Client missing");host.SetPeerReadyForBroadcasting(2);host.SetPeerReadyForBroadcasting(3);}
            else{await Wait(()=>File.Exists(Path.Combine(Root,"host-listening.json")),"listen");var client=new NetClientGameService(PeerVersionInfo.LocalDefault());service=client;var transport=new ENetClient(client);client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);if((await transport.ConnectToHost(Id,"127.0.0.1",port)).HasValue)throw new Exception("ENet client failed");_ = TaskHelper.RunSafely(PumpNetwork(client));}
            var players=new[]{Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1),Player.CreateForNewRun(ModelDb.Character<Silent>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),2),Player.CreateForNewRun(ModelDb.Character<Defect>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),3)};
            var state=RunState.CreateForNewRun(players,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,0,"RMPVISIBLETHREE");
            foreach(var p in players){p.Gold=1000;p.Creature.SetCurrentHpInternal(60);}PrepareBattleDeck(state,players);
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE") is "potions" or "shopFeedback" or "returnExtras" or "purity"||Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("events")||Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("ending")||Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("death"))foreach(var p in players){foreach(var relic in p.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.ChoicesParadox>().ToArray())p.RemoveRelicInternal(relic,silent:true);}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("events"))foreach(var p in players){foreach(var relic in p.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.Ectoplasm>().ToArray())p.RemoveRelicInternal(relic,silent:true);}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("death"))PrepareDeathFixture(state,players);
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="returnExtras")PrepareReturnFixture(state);
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="purity")PreparePurityFixture(state);
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("openingMulti"))PrepareOpeningFixture(state,Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.EndsWith("3"));
            foreach(var p in players)SaveManager.Instance.Progress.GetOrCreateCharacterStats(p.Character.Id);
            RunManager.Instance.SetUpTest(state,service,disableCombatStateSync:false,shouldSave:Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("openingMulti"));_ = TaskHelper.RunSafely(DumpSource());RunManager.Instance.GenerateRooms();await PreloadManager.LoadRunAssets(players.Select(p=>p.Character));RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);networkPump=false;await Frames(30);await Barrier("setup");
            Static("LiveSharingController","SelectMultiplayerSource",DefaultSource.ToString());await Wait(()=>(bool)Get(Proc,"DisplayReady")!,"initial native map replica");Write(Role+"-replica-path.json",new{directory=Get(Proc,"DirectoryPath")});await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("local").ValueKind==JsonValueKind.Number&&Child().GetProperty("local").GetUInt64()==DefaultSource,"observer identity is ready");await Barrier("windows");
            Check(warmPid==0||Pid==warmPid,"main-menu prewarmed replica is reused for multiplayer entry");Check(Child().GetProperty("local").GetUInt64()==DefaultSource,"noncombat replica binds selected original player");await Wait(()=>Get(Proc,"WindowVisible") is true&&Get(Proc,"DrawFrames") is long draws&&draws>0,"actual native window visible and drawing");Check(Get(Proc,"WindowVisible") is true,"actual native replica window is visible, not headless");await Sync(state,"initial-map");
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="window"){await TestWindowInteractions(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="cursor"){await TestCursorResize(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="potions"){await TestPotionRooms(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("ending")){await TestEnding(state,Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="endingVictory");return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("events")){await TestSpecialEvents(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("death")){await TestDeathVisuals(state,Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="deathVictory");return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="shopFeedback"){await TestShopFeedback(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="returnExtras"){await TestReturnExtras(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="purity"){await TestPurity(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("openingMulti")){await TestOpeningMulti(state);return;}
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!="rooms19"){state=await RestoreVisible(state);players=state.Players.ToArray();await VisibleBattle(state,players);}
            else foreach(var p in players)foreach(var potion in p.Potions.ToArray())p.DiscardPotionInternal(potion,silent:false);
            if(Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="battleSwitch19")
            {
                bool timingPassed=latencyFailures.Count==0;
                Write(Role+"-result.json",new{passed=timingPassed,functionalPassed=true,performancePassed=timingPassed,latencyFailures,checks,visible=false,actualRendering=true,scope="current stages one through three, sequential 72-record combat history source switches, strict three-second timing, original full hash and resident PID"});
                await Barrier("done");if(!timingPassed)exit=1;return;
            }
            await PrepareNoncombat(state,players);await Enter(RoomType.Shop,"shop");await Sync(state,"shop-inventory");
            var me=LocalContext.GetMe(state)!;Write(Role+"-inventory.json",new{inventory=Inventory((MerchantRoom)state.CurrentRoom!,me)});await Barrier("inventory-data");
            Check(Child().GetProperty("inventory").GetString()==ReadPath(Path.Combine(Root,SourceRole+"-inventory.json")).GetProperty("inventory").GetString(),"selected player's generated inventory, prices and stock match");
            foreach(var inventory in Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory>(NRun.Instance.MerchantRoom))inventory.Open();await Frames(20);await Grant("shop");
            int gold=me.Gold;await Barrier("before-card-purchase");var duplicate=await Remote("buy","NMerchantCard#",0);await Wait(()=>me.Gold<gold,"native merchant purchase completes");await Sync(state,"card-purchase");Check(me.Gold<gold,"source native merchant purchase spends selected player's gold");
            int accepted=(int)Session("AcceptedControlRequests")!;var resend=JsonSerializer.Deserialize(duplicate.GetRawText(),Rmp("MirrorMessage"))!;Static("MultiplayerMirrorSession","ForwardControlIntent",resend);await Barrier("purchase-duplicate");await Frames(15);Check((int)Session("AcceptedControlRequests")! ==accepted,"reliable purchase duplicate does not buy a second entry");
            int relics=me.Relics.Count,potions=me.Potions.Count();await Barrier("before-relic-purchase");await Remote("buy","NMerchantRelic");await Wait(()=>me.Relics.Count>relics,"native relic purchase completes");await Sync(state,"merchant-relic");await Barrier("before-potion-purchase");await Remote("buy","NMerchantPotion");await Wait(()=>me.Potions.Count()>potions,"native potion purchase completes");await Sync(state,"merchant-potion");
            Write(Role+"-purchased-inventory.json",new{inventory=Inventory((MerchantRoom)state.CurrentRoom!,me)});await Barrier("purchased-inventory");
            Check(Child().GetProperty("inventory").GetString()==ReadPath(Path.Combine(Root,SourceRole+"-purchased-inventory.json")).GetProperty("inventory").GetString(),"card, relic and potion stock follows the selected source's actual purchases");
            int deckCount=me.Deck.Cards.Count;await Barrier("before-removal");await Remote("buy","NMerchantCardRemoval");await Wait(()=>ChoiceKind()=="Deck"&&ChoiceMatches(),"native removal chooser");Check(Personal().GetProperty("Cards").GetArrayLength()>0&&ChoiceMatches(),"removal candidates and constraints match the selected source's real deck");await Remote("select");
            await Wait(()=>Personal().GetProperty("Preview").GetBoolean(),"source removal preview");await Remote("native","NBackButton");await Wait(()=>!Personal().GetProperty("Preview").GetBoolean(),"source cancels removal preview");
            Check(me.Deck.Cards.Count==deckCount,"cancelled removal preview keeps source deck intact");await Remote("select","",1);await Frames(20);await Remote("native","NConfirmButton");await Wait(()=>me.Deck.Cards.Count==deckCount-1,"confirmed native removal completes");await Sync(state,"merchant-removal");Check(me.Deck.Cards.Count==deckCount-1,"only confirmed original removal changes the source deck");
            await Enter(RoomType.RestSite,"rest");await Sync(state,"rest-entry");
            Check(RestFront(state),"source rest portrait foreground follows its own player without slot reordering");await Wait(()=>Child().GetProperty("room").GetString()=="RestSite"&&Child().GetProperty("restFront").GetBoolean(),"native rest portrait foreground observation");Check(Child().GetProperty("restFront").GetBoolean(),"replica rest portrait foreground follows selected source without slot reordering");
            int rejected=(int)Session("RejectedControlRequests")!,oldAccepted=(int)Session("AcceptedControlRequests")!;await Barrier("old-shop-counter-baseline");Static("MultiplayerMirrorSession","ForwardControlIntent",resend);await Barrier("old-shop-request");await Frames(15);Check((int)Session("AcceptedControlRequests")! ==oldAccepted,"obsolete local-generation shop request cannot execute after room changes");
            // The viewer drops the original stale generation before sending.
            // Also test the source boundary with current transport generation
            // but the original shop epoch/context/action retained unchanged.
            var staleContext=JsonSerializer.Deserialize(duplicate.GetRawText(),Rmp("MirrorMessage"))!;staleContext.GetType().GetProperty("Generation")!.SetValue(staleContext,Get(Mirror,"LocalGeneration"));await Barrier("old-shop-source-baseline");Static("MultiplayerMirrorSession","ForwardControlIntent",staleContext);await Wait(()=>(int)Session("RejectedControlRequests")!>rejected,"source rejects obsolete shop permission and context");Check((int)Session("RejectedControlRequests")!>rejected&&(int)Session("AcceptedControlRequests")! ==oldAccepted,"old shop context is rejected at source after room checkpoint changes");await Grant("rest");
            await Remote("native","NRestSiteButton",1);await Wait(()=>ChoiceKind()=="DeckUpgrade"&&ChoiceMatches(),"native smith chooser");Check(ChoiceMatches(),"smith candidates and constraints match the selected source's real deck");await Remote("select");await Frames(20);await Remote("native","NConfirmButton");await Wait(()=>me.Deck.Cards.Any(c=>c.CurrentUpgradeLevel>0),"native smith completes");await Sync(state,"smith");Check(me.Deck.Cards.Any(c=>c.CurrentUpgradeLevel>0),"source native smith upgrades actual source deck");
            await Enter(RoomType.RestSite,"heal");await Sync(state,"heal-entry");await Grant("heal");decimal hp=me.Creature.CurrentHp;await Barrier("before-heal");await Remote("native","NRestSiteButton",0);await Wait(()=>me.Creature.CurrentHp>hp,"native rest completes");await Sync(state,"heal-result");Check(me.Creature.CurrentHp>hp,"original rest restores only the acting player's health");
            await Enter(RoomType.Treasure,"treasure");await Sync(state,"treasure-entry");await Grant("treasure");await Remote("native","NTreasureButton");await Frames(40);await Remote("native","NTreasureRoomRelicHolder");await Sync(state,"treasure-votes");
            await Enter(RoomType.Event,"personal-event",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.ThisOrThat>());await Sync(state,"personal-event-entry");await Grant("personal-event");
            if(Host)await Remote("native","NEventOptionButton",1);await Barrier("one-personal-option");await Frames(20);
            Check(RunManager.Instance.EventSynchronizer.GetEventForPlayer(me).IsFinished==(Id==2),"personal event completion belongs only to the selected player");
            if(!Host)await Remote("native","NEventOptionButton",1);await Sync(state,"personal-event-option");
            await Enter(RoomType.Event,"shared-event",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.DenseVegetation>());await Sync(state,"shared-event-entry");await Grant("shared-event");
            if(Host)await Remote("native","NEventOptionButton",0);await Barrier("one-shared-vote");await Frames(20);
            Check(!RunManager.Instance.EventSynchronizer.GetEventForPlayer(me).IsFinished,"one shared-event vote waits for the team's original decision");
            if(!Host)await Remote("native","NEventOptionButton",0);await Frames(35);await Sync(state,"shared-event-votes");
            await Wait(()=>RunManager.Instance.EventSynchronizer.GetEventForPlayer(me).IsFinished,"shared event finished");await Remote("native","NEventOptionButton");await Wait(()=>Child().GetProperty("map").GetBoolean(),"source completed event opens native map");await Barrier("map-open");
            long generation=(long)Session("_receivedGeneration")!;
            if(Host){await Remote("native","MapPoint@");await Frames(15);Write("first-map-vote.json",new{ready=true});}
            else await Wait(()=>File.Exists(Path.Combine(Root,"first-map-vote.json")),"one player's map vote");
            await Frames(10);Check((long)Session("_receivedGeneration")! ==generation,"one map vote does not advance the team or passive replica");await Wait(()=>Child().GetProperty("mapVotes").GetString()==ReadPath(Path.Combine(Root,SourceRole+"-journal.json")).GetProperty("mapVotes").GetString(),"native map vote indicators follow selected source");Check(Child().GetProperty("mapVotes").GetString()==ReadPath(Path.Combine(Root,SourceRole+"-journal.json")).GetProperty("mapVotes").GetString(),"all player map vote indicators, including selected player, match source");await Barrier("one-vote");
            if(Id==2)await Remote("native","MapPoint@");await Barrier("two-map-votes");await Frames(30);Check((long)Session("_receivedGeneration")! ==generation,"two map votes still wait for third player");if(Id==3)await Remote("native","MapPoint@");
            await Wait(()=>(long)Session("_receivedGeneration")!>generation&&Child().GetProperty("generation").GetInt64()>generation&&(bool)Get(Proc,"DisplayReady")!&&!Child().GetProperty("control").GetBoolean(),"host-authoritative all-player map travel");Check(!Child().GetProperty("control").GetBoolean(),"map travel revokes previous room authority");await Barrier("map-travel");
            if(state.CurrentRoom is CombatRoom)
            {
                // The map vote entered an actual battle. Do not debug-jump
                // out while native asynchronous start hooks/checksums are
                // still running; finish this fixture battle normally first.
                await Wait(()=>CombatManager.Instance.IsInProgress&&!CombatManager.Instance.IsStarting&&!RunManager.Instance.ActionExecutor.IsRunning&&state.Players.All(p=>p.PlayerCombatState?.Phase==PlayerTurnPhase.Play),"map-travel original opening fully settles");
                await Sync(state,"map-travel-combat-opening");await Barrier("map-travel-combat-finish");
                await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState().Enemies.ToList(),true);await CombatManager.Instance.CheckWinCondition();
                await Wait(()=>!CombatManager.Instance.IsInProgress&&NOverlayStack.Instance.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen,"map-travel original battle rewards");await Sync(state,"map-travel-combat-ended");await Grant("map-travel-reward-proceed");
                await Remote("native","NProceedButton");await Wait(()=>MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance.IsOpen&&Child().GetProperty("map").GetBoolean(),"map-travel battle finishes through original continue");await Sync(state,"map-travel-combat-proceed");
            }
            await Enter(RoomType.Event,"card-rewards",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.BrainLeech>());await Sync(state,"card-rewards-entry");await Grant("card-rewards");deckCount=me.Deck.Cards.Count;await Barrier("before-event-card-reward");await Remote("native","NEventOptionButton",1);
            await Remote("native","NRewardButton");await Wait(()=>ChoiceKind()=="Reward"&&ChoiceMatches(),"real card reward options");Check(Personal().GetProperty("Cards").GetArrayLength()>0&&ChoiceMatches(),"native card reward candidates match the selected source");await Remote("select");await Wait(()=>me.Deck.Cards.Count>deckCount,"original event card reward completes");await Sync(state,"event-card-reward");
            await Enter(RoomType.Event,"skipped-reward",ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.BrainLeech>());await Sync(state,"skipped-reward-entry");await Grant("skipped-reward");
            deckCount=me.Deck.Cards.Count;await Remote("native","NEventOptionButton",1);await Remote("native","NProceedButton");await Wait(()=>NOverlayStack.Instance.Peek() is not MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen,"original personal reward skip closes source overlay");await Sync(state,"skipped-reward-result");
            await Wait(()=>Child().GetProperty("overlay").GetString()!="NRewardsScreen","original personal reward skip closes replica overlay");Check(Child().GetProperty("overlay").GetString()!="NRewardsScreen","skipped personal reward closes the replica's original overlay");
            Check(me.Deck.Cards.Count==deckCount,"skipping the original personal reward does not add a card");
            await Stage("第四阶段 · terminal-rewards-fixture");await Barrier("before-terminal-rewards-fixture");
            await RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster&&p.coord!=state.CurrentMapCoord).coord);
            await Wait(()=>CombatManager.Instance.IsInProgress&&!RunManager.Instance.ActionExecutor.IsRunning,"reward fixture combat initialized");await Barrier("reward-fixture-ready");
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(CombatManager.Instance.DebugOnlyGetState().Enemies.ToList(),true);
            await CombatManager.Instance.CheckWinCondition();
            await Wait(()=>!CombatManager.Instance.IsInProgress&&NOverlayStack.Instance.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen,"original battle-end reward page");await Barrier("battle-ended");
            await Wait(()=>Child().GetProperty("room").GetString()=="Monster"&&Child().GetProperty("choice").ValueKind==JsonValueKind.Null&&(bool)Get(Mirror,"Verified")!,"postcombat checkpoint follows stopped replay writer");await Grant("terminal-rewards");
            gold=me.Gold;await Barrier("before-combat-gold");await Remote("native","NRewardButton",0);await Wait(()=>me.Gold>gold,"original combat gold reward completes");await Frames(20);await Sync(state,"terminal-gold-reward");
            await Remote("native","NProceedButton");await Wait(()=>Child().GetProperty("map").GetBoolean(),"original terminal reward proceed opens the map");Check(Child().GetProperty("map").GetBoolean(),"completed combat reward page follows original map proceed");
            Check(state.Players.Count==3&&Child().GetProperty("local").GetUInt64()!=LocalContext.NetId,"all original slots preserved through noncombat transitions");
            Write(Role+"-result.json",new{passed=true,checks,visible=Env.GetEnvironmentVariable("RMP_VISIBLE_SILENT")!="1",actualRendering=true,scope=Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="rooms19"?"current fourth stage, original three-player room decisions and native windows":"three distinct original players, four stages, native windows and source decisions"});await Barrier("done");
        }
        catch(Exception e){exit=1;Write(Role+"-result.json",new{passed=false,checks,error=e.ToString()});GD.PrintErr("[MultiplayerVisible] FAIL "+e);}
        finally{await FinishVisible(exit);}
    }
}
