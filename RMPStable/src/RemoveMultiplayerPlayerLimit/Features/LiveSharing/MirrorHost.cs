using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MirrorHost : IDisposable
{
    internal readonly MirrorProcess Process;
    private CombatReplay? _checkpoint;
    private long _generation;
    private int _sentEvents = -1;
    private double _timer;
    private double _presentationTimer;
    private MirrorMessage? _pointer, _arrow;
    internal string Error { get; private set; } = "";
    internal string SourceHash { get; private set; } = "";
    internal bool Verified { get; private set; }
    internal int FrameEvents { get; private set; } = -1;
    internal Action<MirrorMessage>? Frame;
    internal MirrorHost(ulong source) => Process = new MirrorProcess(source);
    internal void Tick(RunState state, double delta)
    {
        Verified = false;
        if (Process.Child.HasExited) { Error = "独立游戏进程已退出"; return; }
        if (Process.Wire.Error.Length > 0) { Error = Process.Wire.Error; return; }
        while (Process.Wire.Incoming.TryDequeue(out var message))
        {
            if (!Process.Authenticated)
            {
                if (message.Kind != "hello" || message.Process != Process.Child.Id || message.Build != MirrorState.Build)
                { Error = "独立进程身份或游戏版本不一致"; Process.Dispose(); return; }
                Process.Authenticated = true; continue;
            }
            if (!Process.Wire.Identity.Current(message)) continue;
            if (message.Kind == "status")
            {
                Process.LastHash = message.Hash; Process.LastEvents = message.Events; Process.LastIdle = message.Idle; Process.LastHits = message.Hits;
                if (message.Room.Length > 0) Error = message.Room;
            }
            else if (message.Kind == "frame" && message.Width is > 0 and <= 4096 && message.Height is > 0 and <= 4096)
            { FrameEvents = message.Events; Frame?.Invoke(message); }
            else { Error = "独立进程返回了不支持的消息"; Process.Dispose(); return; }
        }
        if (Error.Length > 0 || !Process.Authenticated) return;
        var replay = MirrorState.CurrentReplay;
        bool combat = CombatManager.Instance.IsInProgress;
        if (!combat || replay == null)
        {
            SourceHash = ""; FrameEvents = -1; Process.LastIdle = false;
            // Invalidate old frames/control immediately at the room boundary.
            if (_checkpoint != null) { _checkpoint = null; _pointer = _arrow = null; _generation++; Process.Wire.Identity.Generation = _generation; }
            return;
        }
        if (!ReferenceEquals(_checkpoint, replay))
        {
            _checkpoint = replay; _generation++; Process.Wire.Identity.Generation = _generation;
            _pointer = _arrow = null;
            Process.LastIdle = false; Process.LastHash = ""; FrameEvents = -1;
            _sentEvents = replay.events.Count;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "load", Generation = _generation, Room = "combat", Events = _sentEvents, Payload = MirrorState.Pack(replay) });
        }
        _timer -= delta;
        if (_timer <= 0)
        {
            _timer = .1;
            if (_sentEvents != replay.events.Count && Process.Wire.Pending == 0)
            {
                _sentEvents = replay.events.Count; Process.LastIdle = false;
                _ = Process.Wire.Send(new MirrorMessage { Kind = "events", Generation = _generation, Events = _sentEvents, Payload = MirrorState.Pack(replay) });
            }
            SourceHash = SourceIdle ? MirrorState.Hash(state) : "";
        }
        Verified = SourceIdle && SourceHash.Length > 0 && Process.LastIdle && Process.LastEvents == replay.events.Count && Process.LastHash == SourceHash;
        _presentationTimer -= delta;
        if (_presentationTimer <= 0 && Verified && Process.Wire.Pending == 0)
        {
            _presentationTimer = 1d / 15;
            if (_pointer != null) { _pointer.Generation = _generation; _ = Process.Wire.Send(_pointer); _pointer = null; }
            if (_arrow != null) { _arrow.Generation = _generation; _ = Process.Wire.Send(_arrow); _arrow = null; }
        }
    }
    internal static bool SourceIdle => CombatManager.Instance.IsInProgress && !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused;
    internal bool Authorize(RunState state) => Verified && SourceIdle && MirrorState.CurrentReplay?.events.Count == Process.LastEvents && MirrorState.Hash(state) == Process.LastHash;
    internal void Presentation(MirrorMessage message)
    {
        if (!Process.Authenticated || Error.Length > 0) return;
        // Coalesce pointer motion while the replica applies an event batch.
        // It must never fill the reliable state queue with obsolete UI motion.
        if (message.Kind == "pointer") _pointer = message;
        if (message.Kind == "arrow") _arrow = message;
    }
    public void Dispose() => Process.Dispose();
}
