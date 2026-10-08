using Environment = System.Environment;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// A separate OS process owns all native models, scenes, singletons and saves.
// Its pipe accepts checkpoints/events and presentation input, never game input.
internal sealed class MirrorRenderer : IDisposable
{
    private readonly MirrorWire _wire;
    private readonly int _parent;
    private bool _hello, _busy;
    private int _uiDiagnostics;
    private long _generation;
    private RunState? _state;
    private CombatReplay? _replay;
    private int _events;
    private double _statusTimer, _parentTimer;
    private readonly Queue<MirrorMessage> _stateMessages = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(uint Message, Vector2 Point)> _buttons = new();
    private MirrorWindowHook? _input;
    private bool _control, _attached, _lastIdle, _waitingForAuthority;
    private long _epoch, _request, _dragGeneration;
    private int _dragIndex = -1, _dragEvents, _submittedEvents;
    private string _dragHash = "";
    private CardModel? _dragCard;
    private Vector2 _pointer = new(-1,-1), _dragStart;
    private CanvasLayer? _noticeLayer;
    private Label? _notice;
    private string _error = "";
    private string _phase = "等待主进程战斗数据";
    private readonly Callable _drawCallback;
    private long _drawFrames;
    private double _diagnosticTimer;
    internal static bool IsRenderer => Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    private static string Env(string name) => Environment.GetEnvironmentVariable("RMP_MULTI_" + name) ?? throw new InvalidDataException("Missing renderer identity");
    internal MirrorRenderer()
    {
        string root = Path.GetFullPath(Env("PROFILE_ROOT"));
        string roaming = Path.GetFullPath(Environment.GetEnvironmentVariable("APPDATA") ?? "");
        string local = Path.GetFullPath(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "");
        if (roaming != Path.Combine(root, "Roaming") || local != Path.Combine(root, "Local") ||
            !OS.GetUserDataDir().Replace('/', '\\').StartsWith(roaming.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Renderer profile is not isolated");
        _parent = int.Parse(Env("PARENT"));
        if (_parent == Environment.ProcessId || Process.GetProcessById(_parent).HasExited) throw new InvalidDataException("Invalid renderer owner");
        _wire = new MirrorWire(new NamedPipeClientStream(".", Env("PIPE"), PipeDirection.InOut, PipeOptions.Asynchronous),
            new MirrorIdentity(Env("SESSION"), Env("SECRET"), ulong.Parse(Env("SOURCE"))));
        _ = _wire.ConnectAsync();
        AudioServer.SetBusMute(0, true);
        if (DisplayServer.GetName() != "headless") DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        Engine.MaxFps = 120;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _input = new MirrorWindowHook(MirrorWin32.OwnWindow,InterceptInput,noActivate: true);
            _drawCallback = Callable.From(OnFrameDrawn);
            RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw, _drawCallback);
        }
    }
    private void OnFrameDrawn()
    {
        if (!_attached || _state == null || NCombatRoom.Instance?.IsVisibleInTree() != true || !MirrorWin32.IsWindowVisible(MirrorWin32.OwnWindow)) return;
        if (++_drawFrames == 1) GD.Print("[RMP:Mirror:Window] first visible battle draw callback");
    }
    private void Stage(string phase) { _phase = phase; GD.Print("[RMP:Mirror:Load] " + phase); }
    internal void ProcessFrame(double delta)
    {
        _parentTimer -= delta;
        if (_parentTimer <= 0)
        {
            _parentTimer = 1;
            try { using var parent = Process.GetProcessById(_parent); if (parent.HasExited) Quit(); }
            catch { Quit(); }
        }
        if (_wire.Error.Length > 0) { Quit(); return; }
        if (!_wire.Connected || NGame.Instance == null) return;
        if (!_hello) { _hello = true; _ = _wire.Send(new MirrorMessage { Kind = "hello", Build = MirrorState.Build, Process = Environment.ProcessId, Window = (long)MirrorWin32.OwnWindow }); }
        // Window/mode/result traffic and native hover remain live while ordered
        // replay actions await animations. Only gameplay replay waits on _busy.
        while (_wire.Incoming.TryDequeue(out var message))
        {
            switch (message.Kind)
            {
                case "attach":
                    if (_attached || message.Process != _parent) throw new InvalidDataException("Invalid native attachment");
                    MirrorWin32.AttachOwn((nint)message.Window,_parent); _attached = true;
                    GD.Print("[RMP:Mirror:Window] owned popup source=" + message.Window); break;
                case "clip":
                    if (!_attached) throw new InvalidDataException("Window ownership required before clipping");
                    MirrorWin32.ClipOwn(message); break;
                case "control":
                    if (message.Epoch <= _epoch) break;
                    _control = message.Control; _epoch = message.Epoch; CancelDrag(); break;
                case "result":
                    if (message.Generation == _generation && message.Epoch == _epoch && message.Request == _request && !message.Accepted) _waitingForAuthority = false;
                    break;
                case "load": case "events":
                    if (_stateMessages.Count >= 128) throw new InvalidDataException("Mirror state backlog exceeded");
                    _stateMessages.Enqueue(message); break;
                default: throw new InvalidDataException("Unsupported renderer message");
            }
        }
        if (!_busy && (_state != null || NGame.Instance.MainMenu != null) && SaveManager.Instance.IsProfileInitialized)
            while (_stateMessages.TryDequeue(out var stateMessage))
            {
                if (stateMessage.Kind == "load" && stateMessage.Generation > _generation)
                { _busy = true; CancelDrag(); TaskHelper.RunSafely(Load(stateMessage)); break; }
                if (stateMessage.Generation != _generation) continue;
                if (stateMessage.Kind == "events") { _busy = true; CancelDrag(); TaskHelper.RunSafely(Apply(stateMessage)); break; }
            }
        if (_waitingForAuthority && _events > _submittedEvents) _waitingForAuthority = false;
        ProcessLocalInput();
        bool idle = NativeIdle;
        _statusTimer -= delta;
        if (_statusTimer <= 0 || idle != _lastIdle)
        {
            _statusTimer = .05; _lastIdle = idle;
            _ = _wire.Send(new MirrorMessage { Kind = "status", Generation = _generation, Events = _events, Idle = idle,
                Hash = idle && _replay != null ? MirrorState.Hash(_state!) : "", Hits = idle ? CaptureHits() : new(), Room = _error,
                DisplayReady = _state != null && NCombatRoom.Instance != null, Attached = _attached,
                Phase = _phase, DrawFrames = _drawFrames,
                WindowVisible = DisplayServer.GetName() != "headless" && MirrorWin32.IsWindowVisible(MirrorWin32.OwnWindow),
                Fps = Performance.GetMonitor(Performance.Monitor.TimeFps), ProcessMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000 });
            if (idle && _events > 0 && _uiDiagnostics++ < 8)
                GD.Print("[RMP:Mirror:UI] hand=" + _state!.Players[0].PlayerCombatState?.Hand.Cards.Count + " holders=" + NPlayerHand.Instance?.ActiveHolders.Count +
                    " visible=" + NPlayerHand.Instance?.IsVisibleInTree() + " hits=" + string.Join(";", CaptureHits().Select(h => h.Kind + ":" + h.Index + ":" + string.Join(",",h.Rect))));
        }
        _diagnosticTimer -= delta;
        if (_diagnosticTimer <= 0)
        {
            _diagnosticTimer = 5;
            GD.Print("[RMP:Mirror:Progress] phase=" + _phase + " attached=" + _attached + " busy=" + _busy + " events=" + _events + " drawFrames=" + _drawFrames);
        }
        UpdateNotice();
    }
    private bool NativeIdle => !_busy && _error.Length == 0 && _state != null && _replay != null && !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused;
    private bool InterceptInput(uint message, nuint key, nint data)
    {
        if (message == MirrorWin32.Move || MirrorWin32.Button(message) && message is not 0x20A and not 0x20E)
        {
            MirrorWin32.GetClientRect(MirrorWin32.OwnWindow,out var size);
            _pointer = new Vector2((short)((long)data & 0xFFFF) / (float)Math.Max(1,size.Right), (short)(((long)data >> 16) & 0xFFFF) / (float)Math.Max(1,size.Bottom));
        }
        if (message is MirrorWin32.LeftDown or MirrorWin32.LeftUp or MirrorWin32.CaptureChanged)
        {
            if (_buttons.Count < 32) _buttons.Enqueue((message,_pointer));
            if (message == MirrorWin32.LeftDown && _control) MirrorWin32.SetCapture(MirrorWin32.OwnWindow);
            if (message == MirrorWin32.LeftUp && MirrorWin32.GetCapture() == MirrorWin32.OwnWindow) MirrorWin32.ReleaseCapture();
        }
        // The original engine receives motion for native hover/tips, but never
        // buttons/keys that could play a card, change rooms or alter the replica.
        return MirrorWin32.Button(message) || MirrorWin32.Keyboard(message);
    }
    private MirrorHit? Hit(string kind, Vector2 point) => CaptureHits().LastOrDefault(h => h.Kind == kind && h.Rect.Length == 4 && new Rect2(h.Rect[0],h.Rect[1],h.Rect[2],h.Rect[3]).HasPoint(point));
    private void ProcessLocalInput()
    {
        while (_buttons.TryDequeue(out var input))
        {
            if (input.Message == MirrorWin32.CaptureChanged) { CancelDrag(); continue; }
            if (!_control || !NativeIdle || _waitingForAuthority) { CancelDrag(); continue; }
            if (input.Message == MirrorWin32.LeftDown)
            {
                CancelDrag();
                if (Hit("endTurn",input.Point) != null) { SendIntent("endTurn",-1,-1,MirrorState.Hash(_state!),_events); continue; }
                var hit = Hit("play",input.Point);
                var hand = _state!.Players[0].PlayerCombatState?.Hand.Cards;
                if (hit == null || hand == null || hit.Index < 0 || hit.Index >= hand.Count) continue;
                _dragCard = hand[hit.Index]; _dragIndex = hit.Index; _dragStart = input.Point;
                _dragHash = MirrorState.Hash(_state); _dragEvents = _events; _dragGeneration = _generation;
            }
            else if (input.Message == MirrorWin32.LeftUp && _dragCard != null)
            {
                int index = _dragIndex, events = _dragEvents; string hash = _dragHash;
                bool targeted = _dragCard.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly;
                int target = Hit("target",input.Point)?.Index ?? -1;
                bool valid = _generation == _dragGeneration && _events == events && MirrorState.Hash(_state!) == hash &&
                    _state!.Players[0].PlayerCombatState?.Hand.Cards.ElementAtOrDefault(index) == _dragCard;
                CancelDrag();
                if (valid && (!targeted || target >= 0)) SendIntent("play",index,target,hash,events);
            }
        }
        if (_dragCard != null)
        {
            if (!NativeIdle || !_control || _events != _dragEvents) { CancelDrag(); return; }
            int target = Hit("target",_pointer)?.Index ?? -1;
            var creature = target >= 0 ? _state!.Players[0].Creature.CombatState?.Creatures.ElementAtOrDefault(target) : null;
            bool legal = creature != null && _dragCard.CanPlayTargeting(creature);
            Arrow(new MirrorMessage { X = _pointer.X, Y = _pointer.Y, StartX = _dragStart.X, StartY = _dragStart.Y,
                Highlight = legal ? creature!.IsEnemy ? 2 : 1 : 0 });
        }
    }
    private void SendIntent(string kind, int index, int target, string hash, int events)
    {
        if (!_control || !NativeIdle || _waitingForAuthority) return;
        _waitingForAuthority = true; _submittedEvents = events;
        _ = _wire.Send(new MirrorMessage { Kind = "intent", Generation = _generation, Epoch = _epoch, Request = ++_request,
            Model = kind, Index = index, TargetIndex = target, Hash = hash, Events = events });
    }
    private void CancelDrag()
    {
        _dragCard = null; _dragIndex = -1;
        Arrow(new MirrorMessage { Highlight = -1 });
    }
    private void UpdateNotice()
    {
        if (DisplayServer.GetName() == "headless") return;
        if (_notice == null)
        {
            _noticeLayer = new CanvasLayer { Layer = 110 }; NGame.Instance.AddChild(_noticeLayer);
            _notice = new Label { Position = new Vector2(16,16), MouseFilter = Control.MouseFilterEnum.Ignore };
            _noticeLayer.AddChild(_notice);
        }
        _notice.Text = _error.Length > 0 ? "同步失败，控制已暂停：" + _error.Split('\n')[0] : !NativeIdle ? _phase : _waitingForAuthority ? "等待主进程确认" : "";
    }
    private List<MirrorHit> CaptureHits()
    {
        var result = new List<MirrorHit>();
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        void Add(string kind, int index, Control? node)
        {
            if (node == null || !node.IsVisibleInTree()) return;
            var rect = node.GetGlobalRect();
            result.Add(new MirrorHit { Kind = kind, Index = index, Rect = new[] { rect.Position.X / size.X, rect.Position.Y / size.Y, rect.Size.X / size.X, rect.Size.Y / size.Y } });
        }
        var hand = _state?.Players[0].PlayerCombatState?.Hand.Cards.ToList();
        if (hand != null && NPlayerHand.Instance is { } playerHand)
            foreach (var holder in playerHand.ActiveHolders.OrderBy(h => h.ZIndex)) Add("play", hand.IndexOf(holder.CardModel!), holder.Hitbox);
        if (NCombatRoom.Instance is { } combat)
        {
            foreach (var node in combat.CreatureNodes) Add("target", node.Entity.CombatState.Creatures.ToList().IndexOf(node.Entity), node.Hitbox);
            Add("endTurn", -1, combat.Ui.EndTurnButton);
        }
        return result;
    }
    private async Task Load(MirrorMessage message)
    {
        try
        {
            _generation = message.Generation; _wire.Identity.Generation = _generation; _events = 0; _error = ""; _waitingForAuthority = false; _drawFrames = 0;
            Stage("重建战斗数据");
            RunManager.Instance.CleanUp(); _state = null; _replay = null;
            if (message.Room != "combat") throw new InvalidDataException("This exploration currently supports native combat replay only");
            var replay = MirrorState.Unpack<CombatReplay>(message.Payload);
            _state = RunState.FromSerializable(replay.serializableRun); _replay = replay;
            #if STS2_0111
            RunManager.Instance.SetUpReplay(_state, replay, _wire.Identity.Source);
#else
            RunManager.Instance.SetUpReplay(_state, replay);
#endif
            RunManager.Instance.CombatStateSynchronizer.IsDisabled = true;
            Stage("加载角色资源");
            await PreloadManager.LoadRunAssets(_state.Players.Select(p => p.Character));
            Stage("加载地图资源");
            await PreloadManager.LoadActAssets(_state.Act);
            RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(_state));
            await RunManager.Instance.GenerateMap();
            var manager = RunManager.Instance;
            manager.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            manager.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            // Live batches can arrive before a future enemy checksum exists.
            // The host compares complete native state hashes at idle points;
            // disable the offline replay's premature missing-checksum reports.
            manager.ChecksumTracker.LoadReplayChecksums(null!, replay.nextChecksumId);
            manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            manager.RewardsSetSynchronizer.FastForwardRewardIds(replay.rewardIds);
            Stage("创建原版战斗场景");
            await manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, _state));
            Stage("等待原版战斗初始化");
            while (manager.ActionExecutor.IsPaused) await Frame();
            Stage("回放主进程战斗事件");
            await ReplayEvents(replay);
            Stage("战斗数据已同步");
        }
        catch (Exception e) { _error = e.ToString(); GD.PrintErr("[RMP:Mirror] " + _error); }
        finally { _busy = false; }
    }
    private async Task Apply(MirrorMessage message)
    {
        try
        {
            if (_state == null || _replay == null || _error.Length > 0) return;
            var replay = MirrorState.Unpack<CombatReplay>(message.Payload);
            if (replay.events.Count < _events) throw new InvalidDataException("Replay event prefix shortened");
            for (int i = 0; i < _events; i++)
                if (!MirrorState.Pack(replay.events[i]).SequenceEqual(MirrorState.Pack(_replay.events[i])))
                    throw new InvalidDataException("Replay event prefix changed");
            _replay = replay; await ReplayEvents(replay);
        }
        catch (Exception e) { _error = e.ToString(); GD.PrintErr("[RMP:Mirror] " + _error); }
        finally { _busy = false; }
    }
    private static async Task Frame() => await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task ReplayEvents(CombatReplay replay)
    {
        var manager = RunManager.Instance;
        while (_events < replay.events.Count)
        {
            var item = replay.events[_events];
            switch (item.eventType)
            {
                case CombatReplayEventType.GameAction:
                    while (CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo) await Frame();
                    var action = item.action!.ToGameAction(_state!.GetPlayer(item.playerId!.Value));
                    if (action.ActionType == GameActionType.CombatPlayPhaseOnly)
                        while (CombatManager.Instance.DebugOnlyGetState().CurrentSide == CombatSide.Enemy) await Frame();
                    manager.ActionQueueSet.EnqueueWithoutSynchronizing(action);
                    if (action is EndPlayerTurnAction or ReadyToBeginEnemyTurnAction) await manager.ActionExecutor.FinishedExecutingActions();
                    break;
                case CombatReplayEventType.HookAction:
                    manager.ActionQueueSet.EnqueueWithoutSynchronizing(manager.ActionQueueSynchronizer.GetHookActionForId(item.hookId!.Value, item.playerId!.Value, item.gameActionType!.Value)); break;
                case CombatReplayEventType.ResumeAction: manager.ActionQueueSet.ResumeActionWithoutSynchronizing(item.actionId!.Value); break;
                case CombatReplayEventType.PlayerChoice: manager.PlayerChoiceSynchronizer.ReceiveReplayChoice(_state!.GetPlayer(item.playerId!.Value), item.choiceId!.Value, item.playerChoiceResult!.Value); break;
                default: throw new InvalidDataException("Unknown native replay event");
            }
            _events++;
        }
    }
    private static void Arrow(MirrorMessage message)
    {
        var room = NCombatRoom.Instance;
        if (room == null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return;
        var arrow = LocalSpectatorSource.Descendants<NTargetingArrow>(room).FirstOrDefault();
        if (arrow == null) return;
        if (message.Highlight < 0) { arrow.Visible = false; arrow.SetHighlightingOff(); return; }
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(NTargetingArrow).GetField("_fromControl", flags)?.SetValue(arrow, null);
        typeof(NTargetingArrow).GetField("_fromPos", flags)?.SetValue(arrow, new Vector2(message.StartX, message.StartY) * size);
        typeof(NTargetingArrow).GetField("_followMouse", flags)?.SetValue(arrow, false);
        arrow.Visible = true;
        typeof(NTargetingArrow).GetMethod("UpdateArrowPosition", flags)?.Invoke(arrow, new object[] { new Vector2(message.X, message.Y) * size });
        if (message.Highlight > 0) arrow.SetHighlightingOn(message.Highlight == 2); else arrow.SetHighlightingOff();
    }
    private void Quit() { Dispose(); ((SceneTree)Engine.GetMainLoop()).Quit(); }
    public void Dispose()
    {
        if (DisplayServer.GetName() != "headless" && RenderingServer.Singleton.IsConnected(RenderingServer.SignalName.FramePostDraw, _drawCallback))
            RenderingServer.Singleton.Disconnect(RenderingServer.SignalName.FramePostDraw, _drawCallback);
        _input?.Dispose(); _input = null; _wire.Dispose();
    }
}
