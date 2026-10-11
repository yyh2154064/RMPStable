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
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Runs;
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
    public List<byte[]> Delta { get; set; } = new();
    public string Hash { get; set; } = "";
    public bool Settled { get; set; }
    public string Room { get; set; } = "";
    public string Screen { get; set; } = "";
    public string HandMode { get; set; } = "";
    public List<byte[]> Candidates { get; set; } = new();
}

// Phase one: real room subscriptions and passive native combat replication.
// Navigation and remote control are deliberately left to later phases.
internal static class MultiplayerMirrorSession
{
    private sealed class Subscription { internal string Token = ""; internal long Generation; internal int Events; }
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
    private static int _transfer;
    private static long _generation, _receivedGeneration;
    private static int _receivedEvents;
    private static CombatReplay? _checkpoint;
    private static byte[] _base = Array.Empty<byte>();
    private static readonly Dictionary<ulong, Subscription> Subscribers = new();
    private static readonly Dictionary<(ulong, string), FragmentAssembly> Fragments = new();
    private static double _timer;
    internal static MirrorHost? Mirror { get; private set; }
    internal static MultiplayerMirrorFrame? Latest { get; private set; }
    internal static string Error { get; private set; } = "";
    internal static int SentFrames { get; private set; }
    internal static int ReceivedFrames { get; private set; }
    internal static int RejectedPackets { get; private set; }
    internal static bool Settled => NRun.Instance != null && !CombatManager.Instance.IsStarting &&
        !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo &&
        !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused;
    internal static void Tick(double delta)
    {
        if (MirrorRenderer.IsRenderer) return;
        var service = RunManager.Instance.NetService; var state = RunManager.Instance.DebugOnlyGetState();
        if (service?.Type is not NetGameType.Host and not NetGameType.Client || state == null || !service.IsConnected) { Reset(); return; }
        if (!ReferenceEquals(_service, service) || !ReferenceEquals(_state, state))
        {
            Reset(); _service = service; _state = state; _session = Guid.NewGuid().ToString("N");
            service.RegisterMessageHandler<RmpMirrorPacket>(Receive);
        }
        if (_selected != 0 && !Connected(_selected)) { Error = "多人来源已离线"; Stop(); }
        foreach (var key in Fragments.Where(p => Time.GetTicksMsec() - p.Value.Began > 15000).Select(p => p.Key).ToArray())
        { Fragments[key].Buffer.Dispose(); Fragments.Remove(key); }
        Mirror?.Tick(null, delta); _timer -= delta; if (_timer > 0) return; _timer = .1;
        // Do not reset or mutate the source game's original replay writer.
        var replay = MirrorState.CurrentReplay;
        if (replay == null || state.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.CombatRoom) return;
        if (!ReferenceEquals(_checkpoint, replay))
        {
            _checkpoint = replay; _generation++;
            var copy = MirrorState.Unpack<CombatReplay>(MirrorState.Pack(replay));
            copy.events.Clear(); copy.checksumData.Clear(); _base = MirrorState.Pack(copy);
        }
        foreach (var pair in Subscribers.ToArray())
        {
            if (!Connected(pair.Key)) { Subscribers.Remove(pair.Key); continue; }
            var sub = pair.Value; bool load = sub.Generation != _generation; int start = load ? 0 : sub.Events;
            if (start > replay.events.Count) { Error = "Native replay prefix shortened"; continue; }
            var frame = Capture(load ? "load" : "delta");
            frame.Generation = _generation; frame.Start = start; frame.Events = replay.events.Count;
            if (load) frame.Checkpoint = _base;
            frame.Delta = replay.events.Skip(start).Select(e => MirrorState.Pack(e)).ToList();
            Send(pair.Key, sub.Token, frame); sub.Generation = _generation; sub.Events = frame.Events;
        }
    }
    internal static void Select(ulong source)
    {
        if (_service == null || _state?.GetPlayer(source) == null || !Connected(source)) throw new InvalidOperationException("Source is not in this room");
        Stop(); _selected = source; _token = Guid.NewGuid().ToString("N"); Error = "";
        Mirror = new MirrorHost(source, multiplayer: true);
        Send(source, _token, new MultiplayerMirrorFrame { Kind = "subscribe", Build = MirrorState.Build });
    }
    internal static void Stop()
    {
        if (_selected != 0 && _service?.IsConnected == true) Send(_selected, _token, new MultiplayerMirrorFrame { Kind = "unsubscribe" });
        Mirror?.Dispose(); Mirror = null; Latest = null; _selected = 0; _token = _remoteSession = "";
        _receivedEvents = 0; _receivedGeneration = 0;
    }
    private static void Reset()
    {
        if (_service == null) return;
        Stop(); _service.UnregisterMessageHandler<RmpMirrorPacket>(Receive); _service = null; _state = null;
        Subscribers.Clear(); foreach (var part in Fragments.Values) part.Buffer.Dispose(); Fragments.Clear();
        _checkpoint = null; _base = Array.Empty<byte>(); _generation = 0;
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
            Screen = NOverlayStack.Instance?.Peek()?.GetType().Name ?? "Room" };
        if (NOverlayStack.Instance?.Peek() is Node screen)
            frame.Candidates = LocalSpectatorSource.Descendants<NCardHolder>(screen).Where(c => c.CardModel != null)
                .Select(c => MirrorState.Pack(c.CardModel!.ToSerializable())).ToList();
        return frame;
    }
    private static void Send(ulong destination, string token, MultiplayerMirrorFrame frame)
    {
        if (_service == null) return;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        if (bytes.Length > MirrorWire.MaxPacket) throw new InvalidDataException("Multiplayer mirror frame too large");
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
            Subscribers[packet.Origin] = new Subscription { Token = packet.Token };
            Send(packet.Origin, packet.Token, new() { Kind = "ack", Session = _session, Build = MirrorState.Build }); return;
        }
        if (frame.Kind == "unsubscribe")
        { if (Subscribers.TryGetValue(packet.Origin, out var sub) && sub.Token == packet.Token) Subscribers.Remove(packet.Origin); return; }
        if (packet.Origin != _selected || packet.Token != _token) { RejectedPackets++; return; }
        if (frame.Kind == "reject") { Error = "多人来源游戏或 Mod 版本不一致"; return; }
        if (frame.Build != MirrorState.Build) { RejectedPackets++; return; }
        if (frame.Kind == "ack") { _remoteSession = frame.Session; return; }
        if (frame.Session != _remoteSession || frame.Generation < _receivedGeneration) { RejectedPackets++; return; }
        if (frame.Kind == "load")
        {
            if (frame.Generation <= _receivedGeneration || frame.Start != 0) { RejectedPackets++; return; }
            _receivedGeneration = frame.Generation; _receivedEvents = 0;
        }
        else if (frame.Kind != "delta" || frame.Generation != _receivedGeneration) { RejectedPackets++; return; }
        if (frame.Start != _receivedEvents || frame.Events != frame.Start + frame.Delta.Count) throw new InvalidDataException("Multiplayer event sequence gap");
        _receivedEvents = frame.Events; Latest = frame; ReceivedFrames++; Mirror?.QueueMultiplayer(frame);
    }
}
