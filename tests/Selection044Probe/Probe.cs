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

// Independent v0.4.4 card-selection entry; reuse original startup, identity, input and evidence helpers.
[ModInitializer(nameof(Initialize))]
public static partial class MultiplayerVisibleProbe
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static bool Renderer => Env.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    static bool Host => Id==1;
    static string Root => Renderer ? Env.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")! : Env.GetEnvironmentVariable("RMP_VISIBLE_EVIDENCE")!;
    static string Role => Env.GetEnvironmentVariable("RMP_VISIBLE_ROLE")!;
    static ulong Id => Role=="me"?1UL:Role=="testerA"?2UL:3UL;
    static readonly string[] Roles=Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="feedbackSingle"?new[]{"me"}:new[]{"me","testerA","testerB"};
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
        string last="";while(true){await Frames(3);await VisibleStatus();var state=RunManager.Instance.DebugOnlyGetState();if(state==null)continue;Write(Role+"-crystal.json",CrystalSnapshot());Write(Role+"-journal.json",new{choice=Static("MultiplayerPersonalUi","Capture"),ids=RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds,operations=Get(null,"Operations",Rmp("MultiplayerNoncombat")),eventFinished=state.CurrentRoom is EventRoom&&RunManager.Instance.EventSynchronizer.Events.FirstOrDefault(e=>e.Owner?.NetId==Id)?.IsFinished==true,mapVotes=MapVotes(state)});string hash=Hash(state);if(hash==last)continue;last=hash;var save=RunManager.Instance.ToSave(null);save.SaveTime=save.StartTime=save.RunTime=save.WinTime=0;save.NumReloads=0;save.MapDrawings=null;save.PlatformType=MegaCrit.Sts2.Core.Platform.PlatformType.None;Write(Role+"-saved.json",save);Write(Role+"-digest.json",new{hash,gameplay=GameplayHash(state)});}
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
        var source=journal.GetProperty("choice");return new[]{"Id","Kind","Cards","Eligible","Min","Max","BundleSizes"}.All(p=>local.GetProperty(p).GetRawText()==source.GetProperty(p).GetRawText());
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
        int own=state.GetPlayerSlotIndex(LocalContext.GetMe(state)!);var controls=seats.Cast<Control>().ToList();return controls[own].GetIndex()==controls.Max(c=>c.GetIndex())&&controls.All(c=>c.ZIndex==0);
    }
    public static void Initialize()
    {if(Env.GetEnvironmentVariable("RMP_VISIBLE_TEST")!="1")return;InstallSelectionFixture();TaskHelper.RunSafely(Renderer?Observe():Run());}
    static async Task Observe()
    {
        Process.GetCurrentProcess().PriorityClass=ProcessPriorityClass.BelowNormal;
        int handled=0,pendingSequence=0;string last="";object? pendingOutput=null;
        while(true)
        {
            await Frames(2);await VisibleStatus();var renderer=Get(null,"Active",Rmp("MirrorRenderer"));var state=RunManager.Instance.DebugOnlyGetState();if(renderer==null||state==null)continue;
            ObserveCatchup(renderer);await ObserveReadonly044(renderer,state);
            // Hidden reconstruction cannot accept test input. Defer expensive
            // full-state/UI evidence sampling until the native scene is ready;
            // production synchronization and timing remain active throughout.
            if(Get(renderer,"_busy") is true)continue;
            try
            {
                bool finished=Roles.Any(role=>File.Exists(Path.Combine(Shared,role+"-finished.json")));
                if(!finished)await ObserveSelections(renderer,state);await ObserveBattle(renderer,state);await ObserveWindowInput(renderer,state);if(pendingOutput!=null){WritePath(Path.Combine(Root,"visible-output.json"),pendingOutput);handled=pendingSequence;pendingOutput=null;}
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
                WritePath(Path.Combine(Root,"visible-state.json"),new{authority=Get(renderer,"_battleAuthority"),choice=Static("MultiplayerPersonalUi","Capture"),overlay=NOverlayStack.Instance?.Peek()?.GetType().Name,capstone=MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance.CurrentCapstoneScreen?.GetType().Name,portraits=Portraits(),players=state.Players.Select(p=>p.NetId).ToArray(),service=RunManager.Instance.NetService.Type.ToString(),save=RunManager.Instance.ShouldSave,readonlyAttempt=readonlyAttempt,selectedRow=SelectedRow(state),ids=RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds,personalKind=Get(null,"_kind",Rmp("MultiplayerPersonalUi")),control=Get(renderer,"_control"),inputReady=Get(renderer,"MultiplayerInputReady"),hash=Hash(state),events=Get(renderer,"_events"),local=LocalContext.NetId,generation=Get(renderer,"_generation"),room=state.CurrentRoom?.RoomType.ToString(),restFront=RestFront(state),mapVotes=MapVotes(state),inventory=state.CurrentRoom is MerchantRoom shop?Inventory(shop,LocalContext.GetMe(state)!):"",waiting=Get(renderer,"_waitingForAuthority"),epoch=Get(renderer,"_epoch"),map=MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen,error=Get(renderer,"_error"),rendering=RenderingServer.RenderLoopEnabled,fastReplica=Get(null,"Replica",Rmp("MirrorFastRestore"))});
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
        Check(!Descendants<CanvasLayer>(NGame.Instance).Any(n=>n.Name=="MultiplayerBattlePermission"),"no source approval dialog");
        await Barrier("grant-"+tag);await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("control").GetBoolean()&&Child().GetProperty("inputReady").GetBoolean(),"room grant reaches replica");
    }
    static async Task Sync(RunState state,string tag)
    {await Barrier(tag+"-source-ready");await Frames(4);await Wait(()=>(bool)Get(Mirror,"Verified")!&&ReadPath(Path.Combine(Root,SourceRole+"-digest.json")).GetProperty("hash").GetString()==(string)Get(Proc,"LastHash")!&&ChoiceKind()=="",tag);Check((string)Get(Proc,"LastHash")! ==(string)Get(Mirror,"SourceHash")!,tag+" matches selected source's original all-player state including history");await Wait(()=>Roles.All(role=>ReadPath(Path.Combine(Root,role+"-digest.json")).GetProperty("gameplay").GetString()==GameplayHash(state)),tag+" original three-player gameplay agreement");Check(Roles.All(role=>ReadPath(Path.Combine(Root,role+"-digest.json")).GetProperty("gameplay").GetString()==GameplayHash(state)),tag+" all three original peers agree on run/combat gameplay");await Barrier(tag);}
    static async Task Enter(RoomType type,string tag,MegaCrit.Sts2.Core.Models.AbstractModel? model=null)
    {
        await Stage("第四阶段 · "+tag);await Barrier("before-"+tag);await RunManager.Instance.EnterRoomDebug(type,model:model,showTransition:false);await Barrier("entered-"+tag);
        await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("room").GetString()==type.ToString()&&(bool)Get(Proc,"DisplayReady")!,"native "+tag+" room");
        Check(Child().GetProperty("local").GetUInt64()==SourceId,tag+" retains selected source identity");
    }
    static async Task Run()
    {
        int exit=0;
        try
        {
            await Wait(()=>NGame.Instance?.MainMenu!=null,"main menu");Static("LiveSharingController","Suspend");await ConfigureVisible();await Frames(60);Engine.MaxFps=30;SaveManager.Instance.SetFtuesEnabled(false);

            var warmHost=Get(null,"Host",Rmp("MirrorResident"));int warmPid=warmHost==null?0:((Process)Get(Get(warmHost,"Process"),"Child")!).Id;
            Write(Role+"-prewarm.json",new{pid=warmPid});
            AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RemoveMultiplayerPlayerLimit.Network.PayloadMessageRegistration")).First(t=>t!=null)!.GetMethod("EnsureRegistered",Any)!.Invoke(null,null);
            ushort port=ushort.Parse(Env.GetEnvironmentVariable("RMP_VISIBLE_PORT")!);INetGameService service;
            if(Host){var host=new NetHostGameService(PeerVersionInfo.LocalDefault());service=host;if(host.StartENetHost(port,3).HasValue)throw new Exception("ENet host failed");_ = TaskHelper.RunSafely(PumpNetwork(host));Write("host-listening.json",new{port});ulong end=Time.GetTicksMsec()+60000;while(host.ConnectedPeers.Count<2&&Time.GetTicksMsec()<end){host.Update();await Frames();}if(host.ConnectedPeers.Count!=2)throw new Exception("Client missing");host.SetPeerReadyForBroadcasting(2);host.SetPeerReadyForBroadcasting(3);}
            else{await Wait(()=>File.Exists(Path.Combine(Root,"host-listening.json")),"listen");var client=new NetClientGameService(PeerVersionInfo.LocalDefault());service=client;var transport=new ENetClient(client);client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);if((await transport.ConnectToHost(Id,"127.0.0.1",port)).HasValue)throw new Exception("ENet client failed");_ = TaskHelper.RunSafely(PumpNetwork(client));}
            var players=new[]{Player.CreateForNewRun(ModelDb.Character<Ironclad>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),1),Player.CreateForNewRun(ModelDb.Character<Silent>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),2),Player.CreateForNewRun(ModelDb.Character<Defect>(),SaveManager.Instance.GenerateUnlockStateFromProgress(),3)};
            var state=RunState.CreateForNewRun(players,ActModel.GetDefaultList().Select(a=>a.ToMutable()).ToList(),Array.Empty<ModifierModel>(),GameMode.Standard,Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")=="feedbackVictory"?10:0,"RMPVISIBLETHREE");
            foreach(var p in players){p.Gold=1000;p.Creature.SetCurrentHpInternal(60);}
            PrepareSelections(state,players);
            foreach(var p in players)SaveManager.Instance.Progress.GetOrCreateCharacterStats(p.Character.Id);
            RunManager.Instance.SetUpTest(state,service,disableCombatStateSync:false,shouldSave:Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("openingMulti"));_ = TaskHelper.RunSafely(DumpSource());state.ExtraFields.StartedWithNeow=true;RunManager.Instance.GenerateRooms();Get(state.Act,"_rooms")!.GetType().GetProperty("Ancient")!.SetValue(Get(state.Act,"_rooms"),ModelDb.AncientEvent<MegaCrit.Sts2.Core.Models.Events.Neow>());await PreloadManager.LoadRunAssets(players.Select(p=>p.Character));RunManager.Instance.Launch();NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state));await RunManager.Instance.SetActInternal(0);await RunManager.Instance.LoadIntoLatestMapCoord(null);networkPump=false;await Frames(30);await Barrier("setup");
            Static("LiveSharingController","SelectMultiplayerSource",DefaultSource.ToString());await Wait(()=>(bool)Get(Proc,"DisplayReady")!,"initial native map replica");Write(Role+"-replica-path.json",new{directory=Get(Proc,"DirectoryPath")});await Wait(()=>Child().ValueKind==JsonValueKind.Object&&Child().GetProperty("local").ValueKind==JsonValueKind.Number&&Child().GetProperty("local").GetUInt64()==DefaultSource,"observer identity is ready");await Barrier("windows");
            Check(warmPid==0||Pid==warmPid,"main-menu prewarmed replica is reused for multiplayer entry");Check(Child().GetProperty("local").GetUInt64()==DefaultSource,"noncombat replica binds selected original player");await Wait(()=>Get(Proc,"WindowVisible") is true&&Get(Proc,"DrawFrames") is long draws&&draws>0,"actual native window visible and drawing");Check(Get(Proc,"WindowVisible") is true,"actual native replica window is visible, not headless");await Sync(state,"initial-map");
            await TestSelections(state);
        }
        catch(Exception e){exit=1;Write(Role+"-result.json",new{passed=false,checks,error=e.ToString()});GD.PrintErr("[MultiplayerVisible] FAIL "+e);}
        finally{await FinishVisible(exit);}
    }
}
