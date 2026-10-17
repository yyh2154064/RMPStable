using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Platform;
using RemoveMultiplayerPlayerLimit.Network;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MultiplayerMirrorFrame
{
    public int Protocol { get; set; } = 1;
    public string Kind { get; set; } = "";
    public string Build { get; set; } = "";
    public string Session { get; set; } = "";
    public long Generation { get; set; }
    public int Start { get; set; }
    public int Events { get; set; }
    public byte[] Checkpoint { get; set; } = Array.Empty<byte>();
    public byte[] NoncombatRoom { get; set; } = Array.Empty<byte>();
    public bool MapOpen { get; set; }
    public bool MapTravelEnabled { get; set; }
    public bool ShopOpen { get; set; }
    public bool ResultCardsOpen { get; set; }
    public List<byte[]> Delta { get; set; } = new();
    public string Hash { get; set; } = "";
    public bool Settled { get; set; }
    public string Room { get; set; } = "";
    public string Screen { get; set; } = "";
    public string HandMode { get; set; } = "";
    public List<byte[]> Candidates { get; set; } = new();
    public MultiplayerChoiceView? Choice { get; set; }
    public string Timer { get; set; } = "";
    public MultiplayerControlView? Authority { get; set; }
    public MultiplayerControlRequest? Request { get; set; }
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
    public List<ParticipantSnapshot> Names { get; set; } = new();
    public RestTargetView? RestTarget { get; set; }
    public List<MirrorOperation> Drawings { get; set; } = new();
    public int DrawingStart { get; set; }
    public string PresentationProgress { get; set; } = "";
    public int ActFloor { get; set; } = -1;
}

// Native room subscriptions, passive replay and source-owned battle control.
internal static partial class MultiplayerMirrorSession
{
    private sealed class Subscription
    {
        internal string Token = "";
        internal long Generation;
        internal int Events, Drawings;
        internal byte[] LastFrame = Array.Empty<byte>();
        internal ulong LastSent;
    }
    private sealed class FragmentAssembly
    {
        internal int Transfer, Parts, Next;
        internal ulong Began;
        internal readonly MemoryStream Buffer = new();
    }
    private static INetGameService? _service;
    private static RunState? _state;
    private static string _session = "", _token = "", _remoteSession = "";
    private static ulong _selected;
    private static ulong _nextSubscribe;
    private static int _transfer;
    private static long _generation, _receivedGeneration;
    private static int _receivedEvents;
    private static CombatReplay? _checkpoint;
    private static byte[] _base = Array.Empty<byte>();
    private static readonly Dictionary<ulong, Subscription> Subscribers = new();
    private static readonly Dictionary<(ulong, string), FragmentAssembly> Fragments = new();
    private static double _timer;
    internal static MirrorHost? Mirror { get; private set; }
    internal static ulong Selected => _selected;
    internal static List<ParticipantSnapshot> Sources() => _state?.Players.Where(p => Connected(p.NetId)).Select(p =>
    {
        string name = "";
        try { if (_service?.Platform != PlatformType.None) name = PlatformUtil.GetPlayerNameRaw(_service!.Platform, p.NetId); } catch { }
        return new ParticipantSnapshot { Id = p.NetId.ToString(), Name = string.IsNullOrWhiteSpace(name) ? "玩家 " + (_state!.GetPlayerSlotIndex(p) + 1) : name };
    }).ToList() ?? new();
    internal static MultiplayerMirrorFrame? Latest { get; private set; }
    internal static string Error { get; private set; } = "";
    internal static int SentFrames { get; private set; }
    internal static int ReceivedFrames { get; private set; }
    internal static int RejectedPackets { get; private set; }
    internal static bool Settled => NRun.Instance != null && (RunManager.Instance.DebugOnlyGetState()?.IsGameOver == true || !CombatManager.Instance.IsStarting &&
        !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo &&
        !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused);
    internal static void Tick(double delta)
    {
        if (MirrorRenderer.IsRenderer) return;
        var service = RunManager.Instance.NetService; var state = RunManager.Instance.DebugOnlyGetState();
        if (ProcessQuickSlRestore(service, state, delta)) return;
        if (service?.Type is not NetGameType.Host and not NetGameType.Client || state == null || !service.IsConnected) { Reset(); return; }
        if (!ReferenceEquals(_service, service) || !ReferenceEquals(_state, state))
        {
            Reset(); _service = service; _state = state; _session = Guid.NewGuid().ToString("N");
            service.RegisterMessageHandler<RmpMirrorPacket>(Receive);
        }
        if (_selected != 0 && !Connected(_selected)) { Error = "多人来源已离线"; Stop(); }
        // Quick SL peers finish native room loading at different times. A
        // reliable packet received before the peer installs this handler has
        // no subscriber yet; retry the same authenticated token until its ack.
        if (_selected != 0 && _remoteSession.Length == 0 && Time.GetTicksMsec() >= _nextSubscribe)
        {
            _nextSubscribe = Time.GetTicksMsec() + 1000;
            Send(_selected, _token, new MultiplayerMirrorFrame { Kind = "subscribe", Build = MirrorState.Build });
        }
        foreach (var key in Fragments.Where(p => Time.GetTicksMsec() - p.Value.Began > 15000).Select(p => p.Key).ToArray())
        { Fragments[key].Buffer.Dispose(); Fragments.Remove(key); }
        TickControl(); Mirror?.Tick(null, delta); _timer -= delta; if (_timer > 0) return; _timer = .033;
        // Do not reset or mutate the source game's original replay writer.
        // An event-owned fight is reconstructed from its original event and
        // ordered decisions/actions. Its unfinished CombatRoom cannot be saved.
        bool noncombat = MultiplayerNoncombat.IsCurrent(state);
        // Vanilla stops its replay writer before the game-over overlay. Keep
        // the already published checkpoint alive through its immutable ending.
        var replay = InFinal ? _checkpoint : noncombat ? MultiplayerNoncombat.Checkpoint :
            MirrorState.CurrentReplay ?? (state.CurrentRoom is MegaCrit.Sts2.Core.Rooms.CombatRoom ? _checkpoint : null);
        if (replay == null || !InFinal && !noncombat && state.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.CombatRoom)
        {
            RevokeControl();
            foreach (var pair in Subscribers.ToArray())
                if (Connected(pair.Key)) { var frame = Capture("room"); frame.Generation = _generation; frame.Events = pair.Value.Events; frame.Authority = ControlFor(pair.Key, pair.Value.Token); Send(pair.Key, pair.Value.Token, frame); }
                else Subscribers.Remove(pair.Key);
            return;
        }
        if (!ReferenceEquals(_checkpoint, replay))
        {
            RevokeControl();
            ControlIds.Clear();
            _checkpoint = replay; _generation++;
            var copy = MirrorState.Unpack<CombatReplay>(MirrorState.Pack(replay));
            copy.events.Clear(); copy.checksumData.Clear(); _base = MirrorState.Pack(copy);
        }
        foreach (var pair in Subscribers.ToArray())
        {
            if (!Connected(pair.Key)) { Subscribers.Remove(pair.Key); continue; }
            var sub = pair.Value; bool load = sub.Generation != _generation; int start = load ? 0 : sub.Events;
            var operations = replay.events.Select(e => new MirrorOperation { Kind = "multiplayer", Payload = MirrorState.Pack(e) }).Concat(MultiplayerNoncombat.Operations).Concat(InFinal ? FinalOperations : Enumerable.Empty<MirrorOperation>()).ToList();
            if (start > operations.Count) { Error = "Native replay prefix shortened"; continue; }
            var frame = Capture(load ? "load" : "delta");
            frame.Authority = ControlFor(pair.Key, sub.Token);
            frame.Generation = _generation; frame.Start = start; frame.Events = operations.Count;
            frame.DrawingStart = load ? 0 : sub.Drawings;
            frame.Drawings = MultiplayerNoncombat.Drawings.Skip(frame.DrawingStart).ToList();
            if (load)
            {
                frame.Checkpoint = _base;
                frame.PresentationProgress = MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.ToJson(MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.ToSerializable());
                if (noncombat) frame.NoncombatRoom = MultiplayerNoncombat.Room;
            }
            frame.Delta = operations.Skip(start).Select(e => JsonSerializer.SerializeToUtf8Bytes(e)).ToList();
            SendFrame(pair.Key, sub.Token, frame, sub); sub.Generation = _generation; sub.Events = frame.Events; sub.Drawings = MultiplayerNoncombat.Drawings.Count;
        }
    }
    internal static void Select(ulong source)
    {
        if (_service == null || _state?.GetPlayer(source) == null || !Connected(source)) throw new InvalidOperationException("Source is not in this room");
        bool reuse = Mirror?.CanReuse == true;
        if (reuse) { Unsubscribe(); Mirror!.SwitchMultiplayerSource(source); }
        else Stop();
        _selected = source; _token = Guid.NewGuid().ToString("N"); Error = "";
        if (!reuse) Mirror = MirrorResident.Acquire(source, multiplayer: true);
        Mirror.Intent = ForwardControlIntent;
        _nextSubscribe = Time.GetTicksMsec() + 1000;
        Send(source, _token, new MultiplayerMirrorFrame { Kind = "subscribe", Build = MirrorState.Build });
    }
    internal static void Reload(ulong source) { Stop(); Select(source); }
    internal static void Stop()
    {
        IsRestoring = false; _restoreState = null; _restorePlayers = Array.Empty<ulong>();
        Unsubscribe(); MirrorResident.Release(Mirror); Mirror = null;
    }
    private static void Unsubscribe()
    {
        if (_selected != 0 && _service?.IsConnected == true) Send(_selected, _token, new MultiplayerMirrorFrame { Kind = "unsubscribe" });
        Latest = null; _selected = 0; _token = _remoteSession = ""; ControlRequested = false;
        _receivedEvents = 0; _receivedGeneration = 0;
    }
    private static void Reset()
    {
        if (_service == null) return;
        RevokeControl();
        ControlIds.Clear();
        Stop(); _service.UnregisterMessageHandler<RmpMirrorPacket>(Receive); _service = null; _state = null;
        Subscribers.Clear(); foreach (var part in Fragments.Values) part.Buffer.Dispose(); Fragments.Clear();
        _checkpoint = null; _base = Array.Empty<byte>(); _generation = 0;
        _finalState = null; FinalOperations.Clear();
    }
    private static bool Connected(ulong id)
    {
        if (_service == null) return false;
        if (id == _service.NetId) return true;
        if (_service is MegaCrit.Sts2.Core.Multiplayer.NetHostGameService host) return host.ConnectedPeers.Any(p => p.peerId == id);
#if STS2_0111
        return RunManager.Instance.RunLobby?.PlayerIds.Contains(id) == true;
#else
        return RunManager.Instance.RunLobby?.ConnectedPlayerIds.Contains(id) == true;
#endif
    }
    private static MultiplayerMirrorFrame Capture(string kind)
    {
        var frame = new MultiplayerMirrorFrame { Kind = kind, Build = MirrorState.Build, Session = _session,
            Settled = Settled, Hash = Settled && _state != null ? MirrorState.MultiplayerHash(_state) : "",
            Room = _state?.CurrentRoom?.RoomType.ToString() ?? "", HandMode = NPlayerHand.Instance?.CurrentMode.ToString() ?? "",
            Screen = NOverlayStack.Instance?.Peek()?.GetType().Name ?? "Room", Choice = MultiplayerPersonalUi.Capture(), Timer = MirrorClock.Label?.Text ?? "",
            Names = Sources(), RestTarget = MultiplayerPresentation042.CaptureTarget(), ActFloor = _state?.ActFloor ?? -1,
            MapOpen = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen == true,
            ResultCardsOpen = NCapstoneContainer.Instance?.CurrentCapstoneScreen is MegaCrit.Sts2.Core.Nodes.Screens.NSimpleCardsViewScreen,
            MapTravelEnabled = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsTravelEnabled == true,
            ShopOpen = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.MerchantRoom is { } shop && LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory>(shop).Any(i => i.IsOpen) };
        // Transform previews cycle canonical templates, which are not mutable
        // run cards and cannot be serialized as choices. Publish only the exact
        // native choice contract, independent of recycled/preview holders.
        frame.Candidates = frame.Choice?.Cards ?? new();
        return frame;
    }
    private static void Send(ulong destination, string token, MultiplayerMirrorFrame frame) => SendFrame(destination, token, frame, null);
    private static void SendFrame(ulong destination, string token, MultiplayerMirrorFrame frame, Subscription? subscription)
    {
        if (_service == null) return;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        if (bytes.Length > MirrorWire.MaxPacket) throw new InvalidDataException("Multiplayer mirror frame too large");
        // A full pile can contain many legal card actions. Do not keep queuing
        // identical metadata ahead of clicks and their results. Changed state
        // still publishes immediately; stable state retains a 1s keepalive.
        if (subscription != null)
        {
            ulong now = Time.GetTicksMsec();
            if (now - subscription.LastSent < 1000 && bytes.AsSpan().SequenceEqual(subscription.LastFrame)) return;
            subscription.LastFrame = bytes; subscription.LastSent = now;
        }
        int transfer = ++_transfer, parts = (bytes.Length + 32767) / 32768;
        for (int part = 0; part < parts; part++) Route(new RmpMirrorPacket { Origin = _service.NetId, Destination = destination, Token = token,
            Transfer = transfer, Part = part, Parts = parts, Data = bytes.Skip(part * 32768).Take(32768).ToArray() });
        SentFrames++;
    }
    private static void Route(RmpMirrorPacket packet)
    {
        if (_service == null) return;
        if (packet.Destination == _service.NetId) Accept(packet);
        else if (_service.Type == NetGameType.Host) _service.SendMessage(packet, packet.Destination);
        else _service.SendMessage(packet);
    }
    private static void Receive(RmpMirrorPacket packet, ulong sender)
    {
        try
        {
            if (_service == null || !Connected(sender) || !Connected(packet.Destination)) { RejectedPackets++; return; }
            if (_service.Type == NetGameType.Host)
            {
                if (packet.Origin != sender) { RejectedPackets++; return; }
                Route(packet);
            }
            else
            {
                if (_service is not INetClientGameService client || sender != client.NetClient?.HostNetId || packet.Destination != _service.NetId || !Connected(packet.Origin))
                { RejectedPackets++; return; }
                Accept(packet);
            }
        }
        catch (Exception e) { Error = e.Message; GD.PrintErr("[RMP:MultiplayerMirror] " + e); }
    }
    private static void Accept(RmpMirrorPacket packet)
    {
        var key = (packet.Origin, packet.Token);
        if (!Fragments.TryGetValue(key, out var assembly))
        {
            if (packet.Part != 0 || Fragments.Count >= 32) { RejectedPackets++; return; }
            Fragments[key] = assembly = new FragmentAssembly { Transfer = packet.Transfer, Parts = packet.Parts, Began = Time.GetTicksMsec() };
        }
        if (assembly.Transfer != packet.Transfer || assembly.Parts != packet.Parts || assembly.Next != packet.Part || assembly.Buffer.Length + packet.Data.Length > MirrorWire.MaxPacket)
        { assembly.Buffer.Dispose(); Fragments.Remove(key); RejectedPackets++; return; }
        assembly.Buffer.Write(packet.Data); assembly.Next++;
        if (assembly.Next < assembly.Parts) return;
        byte[] bytes = assembly.Buffer.ToArray(); assembly.Buffer.Dispose(); Fragments.Remove(key);
        var frame = JsonSerializer.Deserialize<MultiplayerMirrorFrame>(bytes) ?? throw new InvalidDataException("Empty mirror frame");
        if (frame.Protocol != 1) { RejectedPackets++; return; }
        if (frame.Kind == "subscribe")
        {
            if (frame.Build != MirrorState.Build) { RejectedPackets++; Send(packet.Origin, packet.Token, new() { Kind = "reject", Build = MirrorState.Build }); return; }
            if (Subscribers.TryGetValue(packet.Origin, out var existing)) ReleaseControl(packet.Origin, existing.Token);
            Subscribers[packet.Origin] = new Subscription { Token = packet.Token };
            Send(packet.Origin, packet.Token, new() { Kind = "ack", Session = _session, Build = MirrorState.Build }); return;
        }
        if (frame.Kind == "unsubscribe")
        { if (Subscribers.TryGetValue(packet.Origin, out var sub) && sub.Token == packet.Token) { Subscribers.Remove(packet.Origin); ReleaseControl(packet.Origin, packet.Token); } return; }
        if (frame.Kind is "control-request" or "control-release" or "control-intent" or "control-heartbeat")
        { ReceiveControl(packet.Origin, packet.Token, frame); return; }
        if (packet.Origin != _selected || packet.Token != _token) { RejectedPackets++; return; }
        if (frame.Kind == "reject") { Error = "多人来源游戏或 Mod 版本不一致"; return; }
        if (frame.Build != MirrorState.Build) { RejectedPackets++; return; }
        if (frame.Kind == "ack") { _remoteSession = frame.Session; return; }
        if (frame.Session != _remoteSession || frame.Generation < _receivedGeneration) { RejectedPackets++; return; }
        if (frame.Kind == "control-result") { ReceiveControlResult(frame); return; }
        if (frame.Kind == "room") { ControlRequested = false; Latest = frame; Mirror?.WaitForMultiplayerRoom(frame.Room); Mirror?.ApplyMultiplayerPermission(frame.Authority); return; }
        if (frame.Kind == "load")
        {
            if (frame.Generation <= _receivedGeneration || frame.Start != 0) { RejectedPackets++; return; }
            _receivedGeneration = frame.Generation; _receivedEvents = 0;
        }
        else if (frame.Kind != "delta" || frame.Generation != _receivedGeneration) { RejectedPackets++; return; }
        if (frame.Start != _receivedEvents || frame.Events != frame.Start + frame.Delta.Count) throw new InvalidDataException("Multiplayer event sequence gap");
        if (Mirror?.MultiplayerAuthority?.Granted == true && frame.Authority?.Granted != true) ControlRequested = false;
        _receivedEvents = frame.Events; Latest = frame; ReceivedFrames++; Mirror?.QueueMultiplayer(frame);
    }
}
