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
    private long _controlEpoch;
    private bool _control;
    private bool _controlDirty;
    private long _intentRequest;
    private string _lastDisplayStage = "";
    internal string Error { get; private set; } = "";
    internal string SourceHash { get; private set; } = "";
    internal bool Verified { get; private set; }
    internal Action<MirrorMessage>? Intent;
    internal MirrorWindowHost? Window { get; private set; }
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
                if (DisplayServer.GetName() != "headless") MirrorWin32.RequireOwned((nint)message.Window, Process.Child.Id);
                Process.Window = message.Window;
                Process.Authenticated = true;
                GD.Print("[RMP:Mirror:Window] authenticated pid=" + message.Process + " hwnd=" + message.Window);
                continue;
            }
            // Window attachment/liveness is process-scoped, while combat hashes
            // and permissions remain generation-scoped. Startup status can be
            // sent before the first load command has reached the renderer.
            if (message.Kind == "status")
            {
                Process.NativeAttached = message.Attached; Process.WindowVisible = message.WindowVisible;
                Process.Phase = message.Phase; Process.DrawFrames = message.DrawFrames;
                string stage = message.Phase + " attached=" + message.Attached + " visible=" + message.WindowVisible + " battleDrawn=" + (message.DrawFrames > 0);
                if (stage != _lastDisplayStage) { _lastDisplayStage = stage; GD.Print("[RMP:Mirror:Window] " + stage); }
            }
            if (!Process.Wire.Identity.Current(message)) continue;
            if (message.Kind == "status")
            {
                Process.LastHash = message.Hash; Process.LastEvents = message.Events; Process.LastIdle = message.Idle; Process.LastHits = message.Hits;
                Process.DisplayReady = message.DisplayReady; Process.NativeAttached = message.Attached; Process.Fps = message.Fps; Process.ProcessMs = message.ProcessMs;
                if (message.Room.Length > 0) Error = message.Room;
            }
            else if (message.Kind == "intent")
            {
                if (message.Request <= _intentRequest) continue;
                _intentRequest = message.Request;
                Intent?.Invoke(message);
            }
            else { Error = "独立进程返回了不支持的消息"; Process.Dispose(); return; }
        }
        if (Error.Length > 0 || !Process.Authenticated) return;
        var replay = MirrorState.CurrentReplay;
        bool combat = CombatManager.Instance.IsInProgress;
        if (!combat || replay == null)
        {
            SourceHash = ""; Process.DisplayReady = false; Process.LastIdle = false;
            // Invalidate old frames/control immediately at the room boundary.
            if (_checkpoint != null) { _checkpoint = null; _generation++; Process.Wire.Identity.Generation = _generation; }
            return;
        }
        if (!ReferenceEquals(_checkpoint, replay))
        {
            _checkpoint = replay; _generation++; Process.Wire.Identity.Generation = _generation;
            Process.LastIdle = false; Process.LastHash = "";
            _sentEvents = replay.events.Count;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "load", Generation = _generation, Room = "combat", Events = _sentEvents, Payload = MirrorState.Pack(replay) });
        }
        // Publish events on the next source frame, not a 100 ms polling tick.
        // Reliable gameplay retains order; no screenshot or pointer queue exists.
        if (_sentEvents != replay.events.Count && Process.Wire.Pending == 0)
        {
            _sentEvents = replay.events.Count; Process.LastIdle = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "events", Generation = _generation, Events = _sentEvents, Payload = MirrorState.Pack(replay) });
        }
        _timer -= delta;
        if (_timer <= 0)
        {
            _timer = .05;
            SourceHash = SourceIdle ? MirrorState.Hash(state) : "";
        }
        Verified = SourceIdle && SourceHash.Length > 0 && Process.LastIdle && Process.LastEvents == replay.events.Count && Process.LastHash == SourceHash;
        if (_controlDirty && Process.Wire.Pending == 0)
        {
            _controlDirty = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "control", Generation = _generation, Epoch = _controlEpoch, Control = _control });
        }
    }
    internal static bool SourceIdle => CombatManager.Instance.IsInProgress && !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused;
    internal bool Authorize(RunState state) => Process.Authenticated && Error.Length == 0 && SourceIdle && Process.LastIdle && Process.LastHash.Length > 0 && MirrorState.CurrentReplay?.events.Count == Process.LastEvents && MirrorState.Hash(state) == Process.LastHash;
    internal bool AuthorizeIntent(RunState state, MirrorMessage message) => Process.Wire.Identity.Current(message) && _control && message.Epoch == _controlEpoch && message.Events == Process.LastEvents && message.Hash == Process.LastHash && Authorize(state);
    internal void Control(bool enabled, long epoch)
    {
        _control = enabled; _controlEpoch = epoch; _controlDirty = true;
    }
    internal void AttachWindow()
    {
        if (Window != null || DisplayServer.GetName() == "headless" || !Process.Authenticated) return;
        Window = new MirrorWindowHost((nint)Process.Window, Process.Child.Id);
        GD.Print("[RMP:Mirror:Window] attach requested container=" + (long)Window.Container);
        _ = Process.Wire.Send(new MirrorMessage { Kind = "attach", Window = (long)Window.Container, Process = System.Environment.ProcessId });
    }
    public void Dispose() { Process.Dispose(); Window?.Dispose(); Window = null; }
}
