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
    private long _sourceGeneration;
    private ulong _viewSource;
    internal long LocalGeneration => _generation;
    internal long SourceGeneration => _sourceGeneration;
    private int _sentEvents = -1;
    private double _timer;
    private long _controlEpoch;
    private bool _control;
    private bool _controlDirty;
    private long _intentRequest;
    internal long ControlEpoch => _controlEpoch;
    private string _lastDisplayStage = "";
    internal string Error { get; private set; } = "";
    internal string SourceHash { get; private set; } = "";
    internal bool Verified { get; private set; }
    internal Action<MirrorMessage>? Intent;
    internal MirrorWindowHost? Window { get; private set; }
    internal bool Multiplayer { get; private set; }
    internal ulong ViewSource => _viewSource;
    private bool _parked, _fastRestoreNextLoad;
    private bool _normalOpeningNextLoad;
    internal void PreserveOpeningOnce() => _normalOpeningNextLoad = true;
    private readonly System.Collections.Generic.Queue<MultiplayerMirrorFrame> _multiFrames = new();
    private readonly System.Collections.Generic.Queue<MultiplayerMirrorFrame> _drawingFrames = new();
    private MultiplayerMirrorFrame? _multiLatest;
    private MultiplayerMirrorFrame? _personalSent;
    private string _waitingRoom = "";
    private bool _roomDirty;
    internal MultiplayerControlView? MultiplayerAuthority { get; private set; }
    internal void ApplyMultiplayerPermission(MultiplayerControlView? view)
    {
        bool enabled = view?.Granted == true;
        long epoch = view?.Epoch > 0 ? view.Epoch : _controlEpoch;
        MultiplayerAuthority = view;
        if (_control == enabled && _controlEpoch == epoch) return;
        _control = enabled; _controlEpoch = epoch;
        if (!Process.Authenticated) { _controlDirty = true; return; }
        _ = Process.Wire.Send(new MirrorMessage { Kind = "control", Multiplayer = true, Generation = _generation, Epoch = epoch, Control = enabled });
    }
    internal void WaitForMultiplayerRoom(string room)
    {
        if (string.IsNullOrEmpty(room)) room = "房间切换中";
        Verified = false; Process.DisplayReady = false; Window?.Hide();
        if (_waitingRoom == room) return;
        _waitingRoom = room; _roomDirty = true; _multiLatest = null; _multiFrames.Clear();
        Process.Phase = "当前房间：" + room + " · 等待来源房间检查点";
    }
    internal MirrorHost(ulong source, bool multiplayer = false) { Multiplayer = multiplayer; _viewSource = source; Process = new MirrorProcess(source); }
    internal bool CanReuse => Multiplayer && Error.Length == 0 && Process.Authenticated && !Process.Child.HasExited && Process.Wire.Connected && Process.Wire.Error.Length == 0;
    internal void SwitchMultiplayerSource(ulong source)
    {
        if (!CanReuse) throw new InvalidOperationException("Replica is not reusable");
        Rebind(source, true);
    }
    internal void Rebind(ulong source, bool multiplayer)
    {
        Window?.Hide(); _parked = false; Multiplayer = multiplayer; Error = ""; _normalOpeningNextLoad = false;
        _viewSource = source; _sourceGeneration = 0; _checkpoint = null; _sentEvents = -1;
        _multiFrames.Clear(); _drawingFrames.Clear(); _multiLatest = _personalSent = null;
        _waitingRoom = ""; _roomDirty = false;
        MultiplayerAuthority = null; _control = _controlDirty = false; _controlEpoch = 0;
        _publishedReady = false; _publishedHash = ""; _publishedEvents = -1;
        _timerText = ""; _timerGeneration = -1; _timer = 0; _fastRestoreNextLoad = true;
        Verified = false; SourceHash = "";
        Process.DisplayReady = Process.LastIdle = Process.LastControlEnabled = false;
        Process.LastHash = Process.LastDrawingHash = ""; Process.LastEvents = 0; Process.DrawFrames = 0;
        _generation++; Process.Wire.Identity.Generation = _generation;
        _resetDirty = true;
    }
    internal void Park()
    {
        if (_parked) return;
        Rebind(_viewSource, Multiplayer); _parked = true; Intent = null;
    }
    internal void QueueMultiplayer(MultiplayerMirrorFrame frame)
    {
        if (!Multiplayer) throw new InvalidOperationException("Multiplayer frame on singleplayer mirror");
        _multiLatest = frame;
        _waitingRoom = "";
        _roomDirty = false;
        if (frame.Kind == "load") _drawingFrames.Clear();
        if (frame.Drawings.Count > 0) _drawingFrames.Enqueue(frame);
        if (frame.Kind != "load" && frame.Delta.Count == 0) return;
        if (frame.Kind == "load") { _multiFrames.Clear(); Process.DisplayReady = false; Process.LastIdle = false; Verified = false; }
        if (_multiFrames.Count >= 128) throw new InvalidOperationException("Multiplayer replica backlog exceeded");
        _multiFrames.Enqueue(frame);
    }
    private void TickMultiplayer()
    {
        if (_controlDirty && Process.Wire.Pending == 0)
        {
            _controlDirty = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "control", Multiplayer = true, Generation = _generation, Epoch = _controlEpoch, Control = _control });
        }
        if (_waitingRoom.Length > 0)
        {
            Verified = false; SourceHash = ""; Process.DisplayReady = false; Process.LastIdle = false;
            Process.Phase = "当前房间：" + _waitingRoom + " · 等待来源房间检查点";
            Window?.Hide();
            if (_roomDirty && Process.Wire.Pending == 0)
            { _roomDirty = false; _ = Process.Wire.Send(new MirrorMessage { Kind = "multiplayer-room", Generation = _generation, Room = Process.Phase }); }
            return;
        }
        if (Process.Wire.Pending == 0 && _multiFrames.TryDequeue(out var frame))
        {
            if (frame.Kind == "load") { _sourceGeneration = frame.Generation; _generation++; Process.Wire.Identity.Generation = _generation; }
            _ = Process.Wire.Send(new MirrorMessage { Kind = frame.Kind == "load" ? "load" : "events", Multiplayer = true,
                Generation = _generation, ViewSource = _viewSource, Names = frame.Names, PresentationProgress = frame.PresentationProgress, ActFloor = frame.ActFloor, NormalOpening = frame.Kind == "load" && _normalOpeningNextLoad, Index = frame.Start, Events = frame.Events, Payload = frame.Checkpoint, NoncombatRoom = frame.NoncombatRoom,
                Operations = frame.Delta.ConvertAll(p => System.Text.Json.JsonSerializer.Deserialize<MirrorOperation>(p) ?? throw new System.IO.InvalidDataException("Empty native operation")) });
            if (frame.Kind == "load") _normalOpeningNextLoad = false;
        }
        var current = _multiLatest;
        if (Process.Wire.Pending == 0 && _drawingFrames.TryPeek(out var drawingFrame) && drawingFrame.Generation == _sourceGeneration)
        {
            _drawingFrames.Dequeue();
            _ = Process.Wire.Send(new MirrorMessage { Kind = "mapDrawings", Generation = _generation, Index = drawingFrame.DrawingStart, Drawings = drawingFrame.Drawings });
        }
        if (current != null && !ReferenceEquals(_personalSent, current) && current.Generation == _sourceGeneration && Process.Wire.Pending == 0)
        {
            _personalSent = current;
            ApplyMultiplayerPermission(current.Authority);
            _ = Process.Wire.Send(new MirrorMessage { Kind = "personal", Generation = _generation, Events = current.Events, Names = current.Names, RestTarget = current.RestTarget, Choice = current.Choice, Authority = current.Authority, TimerText = current.Timer, MapOpen = current.MapOpen, MapTravelEnabled = current.MapTravelEnabled, ShopOpen = current.ShopOpen });
        }
        SourceHash = current?.Hash ?? "";
        Verified = current?.Settled == true && SourceHash.Length > 0 && Process.DisplayReady && Process.LastIdle && Process.LastEvents == current.Events && Process.LastHash == SourceHash;
    }
    private bool _resetDirty;
    private bool _publishedReady;
    private string _publishedHash = "";
    private int _publishedEvents = -1;
    private string _timerText = "";
    private long _timerGeneration = -1;
    internal void Tick(RunState? state, double delta)
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
                if (message.Hash != Process.LastHash) _timer = 0;
                Process.LastHash = message.Hash; Process.LastEvents = message.Events; Process.LastIdle = message.Idle; Process.LastHits = message.Hits;
                Process.LastControlEpoch = message.Epoch; Process.LastControlEnabled = message.Control;
                Process.DisplayReady = message.DisplayReady; Process.NativeAttached = message.Attached; Process.Fps = message.Fps; Process.ProcessMs = message.ProcessMs;
                Process.TargetArrowVisible = message.TargetArrowVisible;
                Process.LastDrawingHash = message.DrawingHash;
                Process.Presentation = message.Presentation;
                if (message.Room.Length > 0) Error = message.Room;
            }
            else if (message.Kind == "intent")
            {
                if (message.Request <= _intentRequest) continue;
                _intentRequest = message.Request;
                if (!Multiplayer && message.Model == "mapStroke")
                {
                    if (_control && message.Epoch == _controlEpoch && state != null && ReferenceEquals(_checkpoint, MirrorJournal.Checkpoint))
                        MapDrawing042.Execute(message.Value, message.Epoch);
                    continue;
                }
                Intent?.Invoke(message);
            }
            else { Error = "独立进程返回了不支持的消息"; Process.Dispose(); return; }
        }
        if (Error.Length > 0 || !Process.Authenticated) return;
        if (_resetDirty)
        {
            _resetDirty = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "reset", Multiplayer = Multiplayer, Generation = _generation, Epoch = _controlEpoch });
        }
        if (_parked) return;
        if (Multiplayer) { TickMultiplayer(); return; }
        if (_controlDirty && Process.Wire.Pending == 0)
        {
            _controlDirty = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "control", Generation = _generation, Epoch = _controlEpoch, Control = _control });
        }
        if (state == null) return;
        MirrorJournal.EnsureCheckpoint(state);
        var replay = MirrorJournal.Checkpoint;
        if (replay == null)
        {
            SourceHash = ""; Process.DisplayReady = false; Process.LastIdle = false;
            // Invalidate old frames/control immediately at the room boundary.
            if (_checkpoint != null) Invalidate();
            return;
        }
        if (!ReferenceEquals(_checkpoint, replay))
        {
            PublishCheckpoint(replay);
        }
        // Publish events on the next source frame, not a 100 ms polling tick.
        // Reliable gameplay retains order; no screenshot or pointer queue exists.
        if (_sentEvents != MirrorJournal.Operations.Count && Process.Wire.Pending == 0)
        {
            _sentEvents = MirrorJournal.Operations.Count; Process.LastIdle = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "events", Generation = _generation, Events = _sentEvents, Payload = MirrorState.Pack(replay), Operations = new(MirrorJournal.Operations) });
        }
        _timer -= delta;
        if (_timer <= 0)
        {
            _timer = .05;
            SourceHash = SourceIdle ? MirrorState.Hash(state) : "";
        }
        Verified = SourceIdle && SourceHash.Length > 0 && Process.LastIdle && Process.LastEvents == MirrorJournal.Operations.Count && Process.LastHash == SourceHash;
        if (_publishedReady != Verified || Verified && (_publishedHash != SourceHash || _publishedEvents != Process.LastEvents))
        {
            _publishedReady = Verified; _publishedHash = Verified ? SourceHash : ""; _publishedEvents = Process.LastEvents;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "ready", Generation = _generation, Idle = Verified, Hash = _publishedHash, Events = _publishedEvents });
        }
        // Copy the original displayed clock, including its update phase and
        // frozen final value. Loading/replay time must not start a second clock.
        if (MirrorClock.Label is { } timer && Process.Wire.Pending == 0 &&
            (_timerText != timer.Text || _timerGeneration != _generation))
        {
            _timerText = timer.Text; _timerGeneration = _generation;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "clock", Generation = _generation, TimerText = _timerText });
        }
    }
    internal void PublishCheckpoint(CombatReplay replay, bool fastRestore = false, bool normalOpening = false)
    {
        if (!Process.Authenticated || Error.Length > 0 || ReferenceEquals(_checkpoint, replay)) return;
        if (_resetDirty)
        {
            _resetDirty = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "reset", Generation = _generation, Epoch = _controlEpoch });
        }
        _checkpoint = replay; _generation++; Process.Wire.Identity.Generation = _generation;
        _publishedReady = false; _publishedHash = ""; _publishedEvents = -1;
        Process.LastIdle = false; Process.LastHash = ""; Process.DisplayReady = false; Verified = false; _timer = 0;
        _sentEvents = MirrorJournal.Operations.Count;
        _ = Process.Wire.Send(new MirrorMessage { Kind = "load", Generation = _generation, ViewSource = _viewSource,
            PresentationProgress = MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.ToJson(MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.ToSerializable()),
            NormalOpening = normalOpening, FastRestore = fastRestore || _fastRestoreNextLoad, Events = _sentEvents, Payload = MirrorState.Pack(replay), Operations = new(MirrorJournal.Operations) });
        _fastRestoreNextLoad = false;
    }
    internal void Invalidate()
    {
        _checkpoint = null; _generation++; Process.Wire.Identity.Generation = _generation;
        _resetDirty = true; _control = false; _controlDirty = false;
        _controlEpoch = 0;
        Verified = false; SourceHash = ""; Process.LastIdle = false; Process.LastHash = ""; Process.DisplayReady = false;
        Window?.Hide();
    }
    internal static bool SourceIdle => MegaCrit.Sts2.Core.Nodes.NRun.Instance != null && (RunManager.Instance.DebugOnlyGetState()?.IsGameOver == true || !CombatManager.Instance.IsInProgress || LocalSpectatorSource.PendingNativeChoice || !CombatManager.Instance.IsStarting &&
        !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo &&
        RunManager.Instance.DebugOnlyGetState()?.Players[0].PlayerCombatState?.Phase == PlayerTurnPhase.Play &&
        !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused);
    internal bool Authorize(RunState state) => Process.Authenticated && Error.Length == 0 && SourceIdle && Process.LastIdle && Process.LastHash.Length > 0 && MirrorJournal.Operations.Count == Process.LastEvents && MirrorState.Hash(state) == Process.LastHash;
    internal bool AuthorizeIntent(RunState state, MirrorMessage message) => Process.Wire.Identity.Current(message) && _control && message.Epoch == _controlEpoch && message.Events == Process.LastEvents && message.Hash == Process.LastHash && Authorize(state);
    internal void Control(bool enabled, long epoch)
    {
        if (!enabled) MapDrawing042.Stop();
        if (Multiplayer && enabled) throw new InvalidOperationException("Multiplayer control requires source-owned permission");
        _control = enabled; _controlEpoch = epoch; _controlDirty = true;
        if (Process.Authenticated && !_resetDirty && Process.Wire.Pending == 0)
        {
            _controlDirty = false;
            _ = Process.Wire.Send(new MirrorMessage { Kind = "control", Generation = _generation, Epoch = epoch, Control = enabled });
        }
    }
    internal void AttachWindow()
    {
        if (Window != null || DisplayServer.GetName() == "headless" || !Process.Authenticated) return;
        Window = new MirrorWindowHost((nint)Process.Window, Process.Child.Id);
        Window.SendClip = message => { _ = Process.Wire.Send(message); };
        GD.Print("[RMP:Mirror:Window] ownership requested source=" + (long)Window.OwnerWindow);
        _ = Process.Wire.Send(new MirrorMessage { Kind = "attach", Window = (long)Window.OwnerWindow, Process = System.Environment.ProcessId });
    }
    public void Dispose() { Process.Dispose(); Window?.Dispose(); Window = null; }
}
