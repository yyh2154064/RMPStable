using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Env=System.Environment;

public static partial class MultiplayerVisibleProbe
{
    static ulong loadedRunBegan;
    static void LoadedRunBegin()=>loadedRunBegan=Time.GetTicksMsec();
    static async Task<LoadRunLobby> JoinLoadedPlayers(INetGameService service,MegaCrit.Sts2.Core.Saves.SerializableRun save)
    {
        LoadRunLobby? lobby=null;var stack=NGame.Instance.MainMenu.SubmenuStack;
        var screen=stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
        void ReceiveLoadedResponse(ClientLoadJoinResponseMessage response,ulong sender){if(sender!=1)throw new Exception("Unexpected loaded-run host");screen.InitializeAsClient(service,response);stack.Push(screen);lobby=(LoadRunLobby)Get(screen,"_runLobby")!;}
        if(Host){screen.InitializeAsHost(service,save);stack.Push(screen);lobby=(LoadRunLobby)Get(screen,"_runLobby")!;}
        else service.RegisterMessageHandler<ClientLoadJoinResponseMessage>(ReceiveLoadedResponse);
        await Barrier("loaded-lobby-listening");
        // Each response constructs the client's original lobby immediately,
        // before the next player's original PlayerReconnected broadcast.
        if(Id==2){service.SendMessage(new ClientLoadJoinRequestMessage());await Wait(()=>lobby!=null,"first native loaded-run join response");}
        await Barrier("loaded-player-two-joined");
        if(Id==3){service.SendMessage(new ClientLoadJoinRequestMessage());await Wait(()=>lobby!=null,"second native loaded-run join response");}
        await Wait(()=>lobby?.PlayerIds.OrderBy(id=>id).SequenceEqual(new ulong[]{1,2,3})==true,"original loaded-run lobby contains all connected players");
        Check(lobby!.PlayerIds.Count()==3,"native load join responses and reconnect broadcast preserve three real players");
        await Barrier("loaded-three-joined");
        if(!Host)service.UnregisterMessageHandler<ClientLoadJoinResponseMessage>(ReceiveLoadedResponse);return lobby!;
    }
    static async Task<INetGameService> ConnectPlayers(ushort port,string tag)
    {
        networkPump=true;
        if(Host)
        {
            var host=new MegaCrit.Sts2.Core.Multiplayer.NetHostGameService(PeerVersionInfo.LocalDefault());
            if(host.StartENetHost(port,3).HasValue)throw new Exception("ENet host failed");
            _=TaskHelper.RunSafely(PumpNetwork(host));Write(tag+"-listen.json",new{port});
            await Wait(()=>host.ConnectedPeers.Count==2,"two clients connect to "+tag);
            host.SetPeerReadyForBroadcasting(2);host.SetPeerReadyForBroadcasting(3);return host;
        }
        await Wait(()=>File.Exists(Path.Combine(Root,tag+"-listen.json")),tag+" host listen");
        var client=new MegaCrit.Sts2.Core.Multiplayer.NetClientGameService(PeerVersionInfo.LocalDefault());
        var transport=new ENetClient(client);client.Initialize(transport,MegaCrit.Sts2.Core.Platform.PlatformType.None);
        if((await transport.ConnectToHost(Id,"127.0.0.1",port)).HasValue)throw new Exception("ENet client failed");
        _=TaskHelper.RunSafely(PumpNetwork(client));return client;
    }
    // Local ENet replaces the excluded Steam reconnect period. Once connected,
    // use the same original load screen, ready messages and BeginRun/LoadRun
    // path as mod Quick SL; do not substitute a test-only saved-run loader.
    static async Task<RunState> RestoreVisible(RunState state)
    {
        await Stage("多人快速 SL · 保留副本并恢复原版存档");await Barrier("before-visible-sl");
        int pid=Pid;ulong source=SourceId;long generation=(long)Get(Mirror,"LocalGeneration")!;
        bool normalOpening=Env.GetEnvironmentVariable("RMP_VISIBLE_CASE")!.StartsWith("openingMulti");
        if(normalOpening&&SaveManager.Instance.CurrentRunSaveTask is {} saving)await saving;
        var save=RunManager.Instance.ToSave(null);
        if(normalOpening)
        {
            // Vanilla writes the multiplayer checkpoint on the host. The
            // original loaded lobby then distributes that same save to clients.
            if(Host)
            {
                var saved=SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(Id);
                if(!saved.Success||saved.SaveData==null)throw new Exception("Actual native host multiplayer checkpoint unavailable");
                Write("opening-native-checkpoint.json",new{payload=Convert.ToBase64String((byte[])Static("MirrorState","Pack",saved.SaveData)!)});
            }
            await Barrier("opening-native-checkpoint");
            var payload=Convert.FromBase64String(ReadPath(Path.Combine(Root,"opening-native-checkpoint.json")).GetProperty("payload").GetString()!);
            save=(SerializableRun)Rmp("MirrorState").GetMethod("Unpack",Any)!.MakeGenericMethod(typeof(SerializableRun)).Invoke(null,new object[]{payload})!;
        }
        Static("MultiplayerMirrorSession","BeginQuickSlRestore");
        // Let the clients leave before closing this fixture's old ENet host.
        // Simultaneously tearing down it and both clients creates an unrelated
        // host-loss popup during the native fade-out.
        if(Host)await Wait(()=>Roles.Skip(1).All(role=>File.Exists(Path.Combine(Root,role+"-sl-left.json"))),"clients leave old fixture host");
        await NGame.Instance.ReturnToMainMenu();await Frames(30);Write(Role+"-sl-left.json",new{left=true});await Barrier("sl-disconnected");
        Check(Pid==pid&&Session("IsRestoring") is true,"multiplayer SL disconnect retains its exact authenticated process");
        Check(!(bool)Get(Proc,"DisplayReady")!&&Get(Mirror,"MultiplayerAuthority")==null,"SL clears old display readiness and control before reconnect");
        var service=await ConnectPlayers((ushort)(ushort.Parse(Env.GetEnvironmentVariable("RMP_VISIBLE_PORT")!)+1),"restore");
        var lobby=await JoinLoadedPlayers(service,save);
        var timingPatch=new Harmony("RMP.VisibleLoadedRunTiming");
        var begin=typeof(NMultiplayerLoadGameScreen).GetMethod("BeginRun")!;
        timingPatch.Patch(begin,prefix:new HarmonyMethod(typeof(MultiplayerVisibleProbe).GetMethod(nameof(LoadedRunBegin),Any)!));
        if(!Host)lobby.SetReady(true);
        await Barrier("sl-clients-ready");
        if(Host){await Wait(()=>lobby.IsPlayerReady(2)&&lobby.IsPlayerReady(3),"original clients ready before host start");lobby.SetReady(true);}
        await Wait(()=>loadedRunBegan!=0&&!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(),state)&&RunManager.Instance.DebugOnlyGetState()?.CurrentRoom!=null&&NRun.Instance?.IsNodeReady()==true,"native loaded-run screen enters game");
        var restored=RunManager.Instance.DebugOnlyGetState()!;ulong gameEntered=Time.GetTicksMsec();networkPump=false;
        typeof(RunManager).GetProperty("ShouldSave",Any)!.SetValue(RunManager.Instance,false);
        Check(NModalContainer.Instance?.OpenModal==null,"native restore has no disconnected popup blocking controls");
        await Wait(()=>Session("IsRestoring") is false&&Session("Selected") is ulong selected&&selected==source&&(bool)Get(Proc,"DisplayReady")!&&(normalOpening||(bool)Get(Mirror,"Verified")!),"retained mirror restored from new room subscription");
        ulong ready=Time.GetTicksMsec();Write(Role+"-sl-window-timing.json",new{fromNativeBeginMilliseconds=ready-loadedRunBegan,fromGameEnteredMilliseconds=ready-gameEntered,reused=Pid==pid,players=3,excludesReconnect=true});
        Check(ready-gameEntered<=3000,"verified mod SL window recovers within three seconds after original game entry");
        timingPatch.Unpatch(begin,HarmonyPatchType.All,timingPatch.Id);await Barrier("sl-source-restored");
        Check(((System.Collections.ICollection)Static("MultiplayerMirrorSession","Sources")!).Count==3,"SL restored public roster still contains all three connected sources");
        await Wait(()=>Child().ValueKind==System.Text.Json.JsonValueKind.Object&&Child().GetProperty("local").ValueKind==System.Text.Json.JsonValueKind.Number&&Child().GetProperty("local").GetUInt64()==source,"restored observer identity");
        Check(Pid==pid&&(long)Get(Mirror,"LocalGeneration")!>generation,"SL new original checkpoint reuses process with fresh local generation");
        Check(LocalContext.NetId==Id&&!Child().GetProperty("control").GetBoolean(),"SL preserves source identity and requires fresh permission");
        await Sync(restored,"retained-sl-result");return restored;
    }
}
