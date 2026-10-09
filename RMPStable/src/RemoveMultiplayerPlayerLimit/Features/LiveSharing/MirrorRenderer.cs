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
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// A separate OS process owns all native models, scenes, singletons and saves.
// Its pipe accepts checkpoints/events and presentation input, never game input.
internal sealed partial class MirrorRenderer : IDisposable
{
    private readonly MirrorWire _wire;
    private readonly int _parent;
    private bool _hello, _busy, _sceneReady;
    private int _uiDiagnostics;
    private long _generation;
    private long _desiredGeneration;
    private string _assetCharacters = "", _assetAct = "";
    private RunState? _state;
    private CombatReplay? _replay;
    private readonly LocalSpectatorSource _commands = new();
    private List<MirrorOperation> _operations = new();
    private ControlActionSnapshot? _pressedAction;
    private int _events;
    private double _statusTimer, _parentTimer;
    private readonly Queue<MirrorMessage> _stateMessages = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(uint Message, Vector2 Point, long Generation, long Epoch, float Wheel)> _buttons = new();
    private MirrorWindowHook? _input;
    private bool _control, _attached, _lastIdle, _waitingForAuthority;
    private long _epoch, _request, _dragGeneration;
    private int _submittedEvents;
    private int _dragTurn;
    private CardModel? _dragCard;
    private NCardPlay? _nativePlay;
    private bool _dragReleased;
    private bool _dragDiagnostic;
    internal static MirrorRenderer? Active;
    private Vector2? _eventPointer;
    internal Vector2 NativePointer => ViewportPoint(_eventPointer ?? _pointer);
    private Control? _pressedUi;
    private bool _uiButton;
    private Vector2 _lastUiPointer = new(-1, -1);
    private Vector2 _pointer = new(-1,-1), _dragStart;
    private CanvasLayer? _noticeLayer;
    private Label? _notice;
    private string _error = "";
    private string _phase = "等待主进程战斗数据";
    private readonly Callable _drawCallback;
    private long _drawFrames;
    private double _diagnosticTimer;
    internal static bool IsRenderer => Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    internal static bool Diagnostic => Environment.GetEnvironmentVariable("RMP_MULTI_TEST") == "1" || Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST") == "1";
    private static string Env(string name) => Environment.GetEnvironmentVariable("RMP_MULTI_" + name) ?? throw new InvalidDataException("Missing renderer identity");
    internal MirrorRenderer()
    {
        Active = this;
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
        if (!_attached || _state == null || NRun.Instance?.IsVisibleInTree() != true || !MirrorWin32.IsWindowVisible(MirrorWin32.OwnWindow)) return;
        if (++_drawFrames == 1) { _statusTimer = 0; GD.Print("[RMP:Mirror:Window] first visible battle draw callback"); }
    }
    private bool _lastWindowVisible;
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
                    _control = message.Control; _epoch = message.Epoch; _statusTimer = 0; CancelDrag(); _pressedUi = null;
                    _pendingCards.Clear(); _submittedCard = null; _waitingForAuthority = false; break;
                case "ready":
                    if (message.Generation == _desiredGeneration)
                    { _authorityReady = message.Idle; _authorityEvents = message.Events; _authorityHash = message.Hash; }
                    break;
                case "result":
                    if (Diagnostic) GD.Print("[RMP:Mirror:Input] result accepted=" + message.Accepted + " reason=" + message.Room);
                    if (message.Generation == _generation && message.Epoch == _epoch && message.Request == _request && (!message.Accepted || message.Model == "menu")) _waitingForAuthority = false;
                    if (message.Generation == _generation && message.Epoch == _epoch && message.Request == _request && !message.Accepted) _submittedCard = null;
                    break;
                case "reset":
                    if (message.Generation <= _desiredGeneration) break;
                    _desiredGeneration = message.Generation; _sceneReady = false; _control = false; CancelDrag(); _pressedUi = null;
                    _waitingForAuthority = false; _stateMessages.Clear(); Stage("等待读档后的权威检查点");
                    _authorityReady = false; _pendingCards.Clear();
                    break;
                case "load":
                    if (message.Generation <= _desiredGeneration) break;
                    _desiredGeneration = message.Generation; _sceneReady = false; CancelDrag(); _pressedUi = null;
                    _stateMessages.Clear(); _stateMessages.Enqueue(message); break;
                case "events":
                    if (message.Generation != _desiredGeneration) break;
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
                if (stateMessage.Kind == "events") { _busy = true; TaskHelper.RunSafely(Apply(stateMessage)); break; }
            }
        if (_waitingForAuthority && _events > _submittedEvents) _waitingForAuthority = false;
        ProcessLocalInput();
        ProcessBufferedCards();
        bool idle = NativeIdle;
        bool windowVisible = DisplayServer.GetName() != "headless" && MirrorWin32.IsWindowVisible(MirrorWin32.OwnWindow);
        if (windowVisible != _lastWindowVisible) { _lastWindowVisible = windowVisible; _statusTimer = 0; }
        _statusTimer -= delta;
        if (_statusTimer <= 0 || idle != _lastIdle)
        {
            _statusTimer = .05; _lastIdle = idle;
            _ = _wire.Send(new MirrorMessage { Kind = "status", Generation = _generation, Events = _events, Idle = idle,
                Epoch = _epoch, Control = _control,
                Hash = idle && _replay != null ? MirrorState.Hash(_state!) : "", Hits = idle ? CaptureHits() : new(), Room = _error,
                DisplayReady = _sceneReady && _generation == _desiredGeneration && _state != null && NRun.Instance != null, Attached = _attached,
                Phase = _phase, DrawFrames = _drawFrames,
                WindowVisible = windowVisible,
                TargetArrowVisible = _dragCard != null && TargetingArrow?.IsVisibleInTree() == true,
                Presentation = UiOnlyOpen ? "settings" : PresentationOpen ? "menu" : "combat",
                DrawingHash = Environment.GetEnvironmentVariable("RMP_MULTI_RENDERER_TEST") == "1" && idle ? MirrorState.DrawingHash() : "",
                Fps = Performance.GetMonitor(Performance.Monitor.TimeFps), ProcessMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000 });
            if (Diagnostic && idle && _events > 0 && _uiDiagnostics++ < 8)
                GD.Print("[RMP:Mirror:UI] hand=" + _state!.Players[0].PlayerCombatState?.Hand.Cards.Count + " holders=" + NPlayerHand.Instance?.ActiveHolders.Count +
                    " visible=" + NPlayerHand.Instance?.IsVisibleInTree() + " hits=" + string.Join(";", CaptureHits().Select(h => h.Kind + ":" + h.Index + ":" + string.Join(",",h.Rect))));
        }
        _diagnosticTimer -= delta;
        if (_diagnosticTimer <= 0)
        {
            _diagnosticTimer = 5;
            if (Diagnostic || !_sceneReady) GD.Print("[RMP:Mirror:Progress] phase=" + _phase + " attached=" + _attached + " busy=" + _busy + " events=" + _events + " drawFrames=" + _drawFrames);
        }
        UpdateNotice();
    }
    private bool NativeIdle => !_busy && _generation == _desiredGeneration && _error.Length == 0 && _state != null && _replay != null && MirrorHost.SourceIdle;
    private bool InterceptInput(uint message, nuint key, nint data)
    {
        if (message == MirrorWin32.Move || MirrorWin32.Button(message) && message is not 0x20A and not 0x20E)
        {
            MirrorWin32.GetClientRect(MirrorWin32.OwnWindow,out var size);
            _pointer = new Vector2((short)((long)data & 0xFFFF) / (float)Math.Max(1,size.Right), (short)(((long)data >> 16) & 0xFFFF) / (float)Math.Max(1,size.Bottom));
        }
        if (message is MirrorWin32.LeftDown or MirrorWin32.LeftUp or MirrorWin32.CaptureChanged)
        {
            if (message == MirrorWin32.LeftDown) _uiButton = LocalUiAt(_pointer) != null;
            if (_buttons.Count < 32) _buttons.Enqueue((message,_pointer,_desiredGeneration,_epoch,0));
            if (message == MirrorWin32.LeftDown && (_control || _uiButton)) MirrorWin32.SetCapture(MirrorWin32.OwnWindow);
            if (message == MirrorWin32.LeftUp && MirrorWin32.GetCapture() == MirrorWin32.OwnWindow) MirrorWin32.ReleaseCapture();
        }
        // The original engine receives motion for native hover/tips, but never
        // buttons/keys that could play a card, change rooms or alter the replica.
        // Only the explicitly allowed presentation UI receives engine buttons.
        // Gameplay buttons never reach the replica's original game handlers.
        if (message == MirrorWin32.LeftUp) _uiButton = false;
        if (message == 0x20A && _buttons.Count < 128)
        {
            var point = new MirrorWin32.Point { X = (short)((long)data & 0xFFFF), Y = (short)(((long)data >> 16) & 0xFFFF) };
            if (MirrorWin32.ScreenToClient(MirrorWin32.OwnWindow, ref point) && MirrorWin32.GetClientRect(MirrorWin32.OwnWindow, out var size))
                _pointer = new Vector2(point.X / (float)Math.Max(1, size.Right), point.Y / (float)Math.Max(1, size.Bottom));
            _buttons.Enqueue((message,_pointer,_desiredGeneration,_epoch,(short)((ulong)key >> 16) / 120f));
        }
        if ((message is 0x204 or 0x205 or 0x207 or 0x208 || message == MirrorWin32.KeyDown && key == 27) && _buttons.Count < 32)
        {
            _buttons.Enqueue((message,_pointer,_desiredGeneration,_epoch,0));
            if (_control && message is 0x204 or 0x207) MirrorWin32.SetCapture(MirrorWin32.OwnWindow);
            if (message is 0x205 or 0x208 && MirrorWin32.GetCapture() == MirrorWin32.OwnWindow) MirrorWin32.ReleaseCapture();
        }
        return MirrorWin32.Button(message) || MirrorWin32.Keyboard(message);
    }
    private Vector2 ViewportPoint(Vector2 point)
    {
        var viewport = NGame.Instance.GetViewport();
        MirrorWin32.GetClientRect(MirrorWin32.OwnWindow, out var rect);
        return viewport.GetFinalTransform().AffineInverse() * (point * new Vector2(rect.Right, rect.Bottom));
    }
    private bool Contains(Control node, Vector2 point) => GodotObject.IsInstanceValid(node) && node.IsVisibleInTree() &&
        new Rect2(Vector2.Zero, node.Size).HasPoint(node.GetGlobalTransformWithCanvas().AffineInverse() * ViewportPoint(point));
    private static Control InteractiveArea(Control node) => node is NCardHolder holder ? holder.Hitbox :
        node is NMerchantSlot slot ? slot.Hitbox : node is NCreature creature ? creature.Hitbox : node;
    private MirrorHit? Hit(string kind, Vector2 point)
    {
        if (kind == "play" && NPlayerHand.Instance is { } hand)
        {
            var cards = _state?.Players[0].PlayerCombatState?.Hand.Cards.ToList();
            var holder = hand.ActiveHolders.OrderBy(h => h.ZIndex).LastOrDefault(h => !IsCardReserved(h.CardModel) && Contains(h.Hitbox, point));
            int index = holder == null ? -1 : cards?.IndexOf(holder.CardModel!) ?? -1;
            return index < 0 ? null : new MirrorHit { Kind = kind, Index = index };
        }
        if (CombatManager.Instance.IsInProgress && NCombatRoom.Instance is { } combat && combat.Ui != null)
        {
            if (kind == "endTurn" && Contains(combat.Ui.EndTurnButton, point)) return new MirrorHit { Kind = kind };
            if (kind == "target")
            {
                var target = combat.CreatureNodes.LastOrDefault(n => n.Entity.IsAlive && Contains(n.Hitbox, point));
                if (target != null) return new MirrorHit { Kind = kind, Index = target.Entity.CombatState.Creatures.ToList().IndexOf(target.Entity) };
            }
        }
        return null;
    }
    private void ProcessLocalInput()
    {
        while (_buttons.TryDequeue(out var input))
        {
            if (Diagnostic && input.Message == MirrorWin32.LeftDown) GD.Print("[RMP:Mirror:Input] down point=" + input.Point + " control=" + _control + " idle=" + NativeIdle + " waiting=" + _waitingForAuthority + " localUi=" + LocalUiAt(input.Point)?.GetType().Name + " generation=" + input.Generation + "/" + _desiredGeneration + " epoch=" + input.Epoch + "/" + _epoch);
            if (input.Generation != _desiredGeneration || input.Epoch != _epoch) continue;
            if (input.Message == MirrorWin32.KeyDown && _mapGestureKey.Length > 0) FinishMapGesture(input.Point);
            if (input.Message is 0x204 or 0x207 && NMapScreen.Instance?.IsOpen == true && _control && NativeIdle)
            {
                if (NMapScreen.Instance.Drawings.GetLocalDrawingMode(false) != DrawingMode.None) CancelMapDrawing(input.Point);
                else StartMapGesture(input.Point, input.Message == 0x204 ? "draw" : "erase");
                continue;
            }
            if (_mapGestureKey.Length > 0 && input.Message is MirrorWin32.LeftUp or 0x205 or 0x208)
            { FinishMapGesture(input.Point); continue; }
            if (input.Message is 0x204 or MirrorWin32.KeyDown)
            {
                if (_dragCard != null) { CancelDrag(); continue; }
                var cancel = _state == null ? null : _commands.CaptureCommands(_state).Control.Actions.LastOrDefault(a => a.Enabled && a.Kind == "cancel");
                if (cancel != null) SendIntent("cancel", -1, -1, MirrorState.Hash(_state!), _events, cancel.NativePath);
                continue;
            }
            if (input.Message == MirrorWin32.CaptureChanged)
            {
                if (_mapGestureKey.Length > 0 && MirrorWin32.GetCapture() != MirrorWin32.OwnWindow) FinishMapGesture(input.Point);
                // The native mouse arrow changes cursor mode, which can release
                // Godot's Win32 capture. Keep the in-progress native drag alive.
                if (_dragCard != null && !_dragReleased && _control && _nativePlay != null)
                { if (MirrorWin32.GetCapture() != MirrorWin32.OwnWindow) MirrorWin32.SetCapture(MirrorWin32.OwnWindow); }
                else if (!_dragReleased) CancelDrag();
                continue;
            }
            if (input.Message == MirrorWin32.LeftDown && LocalUiAt(input.Point) is { } ui)
            {
                CancelDrag(); _pressedUi = ui;
                if (ui is NSettingsScreen) PushSettingsButton(input.Point, true);
                continue;
            }
            if (input.Message == MirrorWin32.LeftUp && _pressedUi != null)
            {
                var releasedUi = _pressedUi; _pressedUi = null;
                // Settings controls receive engine input; top bar / pause buttons
                // use the original signal once, with no replica game decision.
                if (releasedUi is NSettingsScreen && GodotObject.IsInstanceValid(releasedUi)) PushSettingsButton(input.Point, false);
                else if (releasedUi is NPauseMenuButton menu && GodotObject.IsInstanceValid(menu) && Contains(menu, input.Point) &&
                    menu.Name.ToString() is "GiveUp" or "SaveAndQuit" or "Compendium" or "RmpQuickSl")
                {
                    if (_control && NativeIdle && !_waitingForAuthority)
                    {
                        string menuName = menu.Name.ToString();
                        NCapstoneContainer.Instance.Close();
                        SendIntent("menu", -1, -1, MirrorState.Hash(_state!), _events, value: menuName);
                    }
                }
                else if (GodotObject.IsInstanceValid(releasedUi) && Contains(releasedUi, input.Point) && releasedUi is NClickableControl clickable)
                    clickable.ForceClick();
                else if (GodotObject.IsInstanceValid(releasedUi) && releasedUi is NCardHolder holder && Contains(holder.Hitbox, input.Point))
                    holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                continue;
            }
            if (input.Message == 0x20A)
            {
                if (PresentationScrollRoot is { } local)
                {
                    var container = LocalSpectatorSource.Descendants<Control>(local).LastOrDefault(n => n is NCardGrid or NScrollableContainer && Contains(n, input.Point));
                    if (container != null && input.Wheel != 0) LocalSpectatorSource.ScrollNative(container, WheelValue(input.Wheel));
                    continue;
                }
                if (!_control || _state == null || !_sceneReady) continue;
                var scroll = _commands.CaptureCommands(_state!).Control.Actions.LastOrDefault(a => a.Enabled && a.Kind == "scroll" &&
                    MirrorNativeUi.Resolve(a.NativePath) is Control node && Contains(node, input.Point));
                if (scroll != null && input.Wheel != 0) QueueScroll(scroll.NativePath, WheelValue(input.Wheel * (NMapScreen.Instance?.IsOpen == true ? 4 : 1)));
                continue;
            }
            if (UiOnlyOpen || NCapstoneContainer.Instance?.CurrentCapstoneScreen is NPauseMenu) { CancelDrag(); continue; }
            if (!_control || (!NativeIdle || _waitingForAuthority) && !(CanBufferCard && input.Message is MirrorWin32.LeftDown or MirrorWin32.LeftUp)) { CancelDrag(); continue; }
            if (input.Message == MirrorWin32.LeftUp && _pressedAction != null)
            {
                var selected = _pressedAction; _pressedAction = null;
                if (MirrorNativeUi.Resolve(selected.NativePath) is Control selectedNode && Contains(InteractiveArea(selectedNode), input.Point))
                    SendIntent(selected.Kind, -1, -1, MirrorState.Hash(_state!), _events, selected.NativePath);
                continue;
            }
            if (input.Message == MirrorWin32.LeftDown)
            {
                CancelDrag();
                var available = NativeIdle && !_waitingForAuthority ? _commands.CaptureCommands(_state!).Control.Actions : new List<ControlActionSnapshot>();
                var selected = available.LastOrDefault(a => a.Enabled && a.Kind is not "play" and not "endTurn" and not "scroll" and not "mapInput" &&
                    a.NativePath.Length > 0 && MirrorNativeUi.Resolve(a.NativePath) is Control n && Contains(InteractiveArea(n), input.Point));
                if (selected != null) { _pressedAction = selected; if (Diagnostic) GD.Print("[RMP:Mirror:Input] native press " + selected.Kind + ":" + selected.NativePath); continue; }
                if (NMapScreen.Instance?.IsOpen == true)
                { StartMapGesture(input.Point, NMapScreen.Instance.Drawings.GetLocalDrawingMode(false) == DrawingMode.None ? "pan" : "pen"); continue; }
                if (!CanBufferCard) continue;
                if (Hit("endTurn",input.Point) != null) { SendIntent("endTurn",-1,-1,MirrorState.Hash(_state!),_events); continue; }
                var hit = Hit("play",input.Point);
                var hand = _state!.Players[0].PlayerCombatState?.Hand.Cards;
                if (hit == null || hand == null || hit.Index < 0 || hit.Index >= hand.Count) continue;
                _dragCard = hand[hit.Index]; _dragStart = input.Point; _dragGeneration = _generation;
                _dragTurn = _state.Players[0].PlayerCombatState!.TurnNumber;
                if (Diagnostic) GD.Print("[RMP:Mirror:Input] drag card=" + _dragCard.Id + " handIndex=" + hit.Index + " events=" + _events);
                var holder = NPlayerHand.Instance!.GetCardHolder(_dragCard);
                if (holder == null) { CancelDrag(); continue; }
                _dragReleased = false;
                _dragDiagnostic = false;
                _eventPointer = input.Point;
                try { typeof(NPlayerHand).GetMethod("StartCardPlay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(NPlayerHand.Instance, new object[] { holder, false }); }
                finally { _eventPointer = null; }
                _nativePlay = typeof(NPlayerHand).GetField("_currentCardPlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(NPlayerHand.Instance) as NCardPlay;
            }
            else if (input.Message == MirrorWin32.LeftUp && _dragCard != null)
            {
                _dragReleased = true;
                using var released = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = NativePointer, GlobalPosition = NativePointer };
                _nativePlay?._Input(released);
                if (NTargetManager.Instance?.IsInSelection == true) NTargetManager.Instance._Input(released);
            }
        }
        if (_pointer != _lastUiPointer) PushSettingsMotion(_pointer);
        ProcessMapGesture();
        if (_dragCard != null)
        {
            if (_nativePlay == null || !GodotObject.IsInstanceValid(_nativePlay) || _nativePlay.IsQueuedForDeletion()) { CancelDrag(); return; }
            if (Diagnostic && !_dragDiagnostic && _nativePlay != null && _pointer.DistanceTo(_dragStart) > .05f)
            {
                _dragDiagnostic = true;
                GD.Print("[RMP:Mirror:Input] native drag pointer=" + NativePointer + " startY=" + typeof(NMouseCardPlay).GetField("_dragStartYPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_nativePlay) + " threshold=" + typeof(NMouseCardPlay).GetProperty("PlayZoneThreshold", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_nativePlay) + " targeting=" + NTargetManager.Instance?.IsInSelection);
            }
            if (!CanBufferCard || _generation != _dragGeneration || _state!.Players[0].PlayerCombatState!.TurnNumber != _dragTurn) { CancelDrag(); return; }
            int target = Hit("target",_pointer)?.Index ?? -1;
            var creature = target >= 0 ? _state!.Players[0].Creature.CombatState?.Creatures.ElementAtOrDefault(target) : null;
            bool legal = creature != null && _dragCard.CanPlayTargeting(creature);
            if (NTargetManager.Instance?.IsInSelection == true)
            {
                var hovered = typeof(NTargetManager).GetProperty("HoveredNode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(NTargetManager.Instance) as Node;
                var next = legal ? NCombatRoom.Instance?.GetCreatureNode(creature!) : null;
                if (hovered != null && hovered != next) NTargetManager.Instance.OnNodeUnhovered(hovered);
                if (next != null) NTargetManager.Instance.OnNodeHovered(next);
            }
        }
    }
    private void PushSettingsMotion(Vector2 point)
    {
        var position = ViewportPoint(point);
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position,
            Relative = ViewportPoint(point) - ViewportPoint(_lastUiPointer) };
        _lastUiPointer = point;
        NGame.Instance.GetViewport().PushInput(motion, true);
    }
    private void PushSettingsButton(Vector2 point, bool pressed)
    {
        PushSettingsMotion(point);
        var position = ViewportPoint(point);
        using var button = new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.Left, Pressed = pressed };
        NGame.Instance.GetViewport().PushInput(button, true);
    }
    private bool UiOnlyOpen => SceneMonitor.FindSettingsScreen()?.IsVisibleInTree() == true;
    private bool PresentationOpen => UiOnlyOpen || NCapstoneContainer.Instance?.CurrentCapstoneScreen != null ||
        NMapScreen.Instance?.IsOpen == true || NOverlayStack.Instance?.Peek() != null || NModalContainer.Instance?.OpenModal != null;
    private Control? PresentationScrollRoot => NCapstoneContainer.Instance?.CurrentCapstoneScreen is NDeckViewScreen deck ? deck :
        NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen pile ? pile : null;
    private static string WheelValue(float wheel) => (wheel > 0 ? "up:" : "down:") + Math.Min(64, Math.Abs(wheel)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private Control? LocalUiAt(Vector2 point)
    {
        if (_state == null || _generation != _desiredGeneration) return null;
        var inspector = NGame.Instance?.InspectCardScreen?.IsVisibleInTree() == true ? (Control)NGame.Instance.InspectCardScreen :
            NGame.Instance?.InspectRelicScreen?.IsVisibleInTree() == true ? NGame.Instance.InspectRelicScreen : null;
        if (inspector != null)
            return LocalSpectatorSource.Descendants<NClickableControl>(inspector).LastOrDefault(n => n.IsEnabled && Contains(n, point));
        if (SceneMonitor.FindSettingsScreen() is { } settings && settings.IsVisibleInTree())
        {
            // Settings are isolated preferences. Do not expose external feedback
            // or mod reload flows through the replica.
            if (LocalSpectatorSource.Descendants<Control>(settings).Any(n =>
                n.GetType().Name is "NOpenFeedbackScreenButton" or "NOpenModdingScreenButton" && Contains(n, point))) return null;
            // Submenu roots need not cover their back button. Resolve native
            // clickable controls directly, including controls outside the root's
            // layout rectangle; sliders still receive viewport mouse events.
            var clickable = LocalSpectatorSource.Descendants<NClickableControl>(settings)
                .LastOrDefault(n => n.IsEnabled && Contains(n, point));
            if (clickable != null && !clickable.GetType().Name.Contains("Slider")) return clickable;
            return LocalSpectatorSource.Descendants<Control>(settings).Any(n => Contains(n, point)) ? settings : null;
        }
        var screen = NCapstoneContainer.Instance?.CurrentCapstoneScreen;
        var pause = screen is Node root ? LocalSpectatorSource.Descendants<NPauseMenu>(root).FirstOrDefault(n => n.IsVisibleInTree()) : null;
        if (pause != null)
            return LocalSpectatorSource.Descendants<NClickableControl>(pause).LastOrDefault(n =>
                n.Name.ToString() is "Resume" or "Settings" or "Compendium" or "GiveUp" or "SaveAndQuit" or "RmpQuickSl" && n.IsEnabled && Contains(n, point));
        if (PresentationScrollRoot is { } screenNode)
            return LocalSpectatorSource.Descendants<NClickableControl>(screenNode).LastOrDefault(n => n.IsEnabled && MirrorNativeUi.Parent<NCardHolder>(n) == null && Contains(n, point)) ??
                (Control?)LocalSpectatorSource.Descendants<NCardHolder>(screenNode).LastOrDefault(n => n.Hitbox.IsEnabled && Contains(n.Hitbox, point));
        var top = NRun.Instance?.GlobalUi.TopBar;
        if (top == null) return null;
        return new NClickableControl[] { top.Pause, top.Deck }.LastOrDefault(n => n.IsEnabled && Contains(n, point));
    }
    private void SendIntent(string kind, int index, int target, string hash, int events, string nodeKey = "", string value = "")
    {
        if (!_control || !NativeIdle || _waitingForAuthority) return;
        _waitingForAuthority = true; _submittedEvents = events;
        _ = _wire.Send(new MirrorMessage { Kind = "intent", Generation = _generation, Epoch = _epoch, Request = ++_request,
            Model = kind, NodeKey = nodeKey, Value = value, Index = index, TargetIndex = target, Hash = hash, Events = events });
    }
    private void CancelDrag()
    {
        _dragCard = null; _dragReleased = false;
        var play = _nativePlay; _nativePlay = null;
        if (play != null && GodotObject.IsInstanceValid(play) && !play.IsQueuedForDeletion()) play.CancelPlayCard();
    }
    private void UpdateNotice()
    {
        if (DisplayServer.GetName() == "headless") return;
        if (_notice == null)
        {
            _noticeLayer = new CanvasLayer { Layer = 110 }; NGame.Instance.AddChild(_noticeLayer);
            _notice = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _notice.AddThemeColorOverride("font_color", Colors.White);
            _notice.AddThemeColorOverride("font_outline_color", Colors.Black);
            _notice.AddThemeConstantOverride("outline_size", 6);
            _notice.AddThemeFontSizeOverride("font_size", 26);
            _noticeLayer.AddChild(_notice);
        }
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        _notice.Position = new Vector2(size.X * .08f, size.Y * .78f);
        _notice.Size = new Vector2(size.X * .84f, size.Y * .14f);
        _notice.Text = _error.Length > 0 ? "同步失败，控制已暂停：" + _error.Split('\n')[0] : !_sceneReady ? _phase : "";
    }
    private List<MirrorHit> CaptureHits()
    {
        var result = new List<MirrorHit>();
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        void Add(string kind, int index, Control? node, string nodeKey = "")
        {
            if (node == null || !node.IsVisibleInTree()) return;
            var viewport = NGame.Instance.GetViewport();
            var rect = (viewport.GetFinalTransform() * node.GetGlobalTransformWithCanvas()) * new Rect2(Vector2.Zero, node.Size);
            MirrorWin32.GetClientRect(MirrorWin32.OwnWindow, out var client);
            var pixels = DisplayServer.GetName() == "headless" ? size : new Vector2(Math.Max(1, client.Right), Math.Max(1, client.Bottom));
            rect = rect.Intersection(new Rect2(Vector2.Zero, pixels));
            if (!rect.HasArea()) return;
            result.Add(new MirrorHit { Kind = kind, Index = index, NodeKey = nodeKey, Rect = new[] { rect.Position.X / pixels.X, rect.Position.Y / pixels.Y, rect.Size.X / pixels.X, rect.Size.Y / pixels.Y } });
        }
        var hand = _state?.Players[0].PlayerCombatState?.Hand.Cards.ToList();
        if (hand != null && NPlayerHand.Instance is { } playerHand)
            foreach (var holder in playerHand.ActiveHolders.OrderBy(h => h.ZIndex)) Add("play", hand.IndexOf(holder.CardModel!), holder.Hitbox);
        if (CombatManager.Instance.IsInProgress && NCombatRoom.Instance is { } combat && combat.Ui != null)
        {
            foreach (var node in combat.CreatureNodes.Where(n => n.Entity.CombatState != null)) Add("target", node.Entity.CombatState.Creatures.ToList().IndexOf(node.Entity), node.Hitbox);
            Add("endTurn", -1, combat.Ui.EndTurnButton);
        }
        Add("ui.pause", -1, NRun.Instance?.GlobalUi.TopBar.Pause);
        Add("ui.deck", -1, NRun.Instance?.GlobalUi.TopBar.Deck);
        if (PresentationScrollRoot is { } presentation)
            Add("ui.return", -1, LocalSpectatorSource.Descendants<NClickableControl>(presentation).FirstOrDefault(n => n.IsEnabled && n.IsVisibleInTree() && (n is NBackButton || n.Name.ToString() == "BackButton")));
        if (NCapstoneContainer.Instance?.CurrentCapstoneScreen is Node screen)
        {
            foreach (var button in LocalSpectatorSource.Descendants<NClickableControl>(screen).Where(n => n.IsEnabled && n.IsVisibleInTree()))
                if (button.Name.ToString() is "Settings" or "Resume") Add("ui." + button.Name.ToString().ToLowerInvariant(), -1, button);
        }
        if (SceneMonitor.FindSettingsScreen() is { } settings)
            Add("ui.back", -1, LocalSpectatorSource.Descendants<NBackButton>(settings).FirstOrDefault(n => n.IsEnabled && n.IsVisibleInTree()));
        if (NCapstoneContainer.Instance?.CurrentCapstoneScreen is Node pauseRoot)
            foreach (var button in LocalSpectatorSource.Descendants<NPauseMenuButton>(pauseRoot).Where(b => b.IsEnabled && b.IsVisibleInTree()))
                Add("ui.menu." + button.Name, -1, button);
        if (_state != null)
            foreach (var action in _commands.CaptureCommands(_state).Control.Actions.Where(a => a.Enabled && a.NativePath.Length > 0))
                if (MirrorNativeUi.Resolve(action.NativePath) is Control node)
                    Add(action.Kind, action.NativeIndex, InteractiveArea(node), action.NativePath);
        return result;
    }
    private async Task Load(MirrorMessage message)
    {
        try
        {
            _generation = message.Generation; _wire.Identity.Generation = _generation; _events = 0; _error = ""; _waitingForAuthority = false; _drawFrames = 0;
            _authorityReady = false; _pendingCards.Clear(); _submittedCard = null;
            Stage("重建原版对局");
            MirrorFastRestore.Replica = message.FastRestore;
            CheckGeneration(message.Generation);
            RunManager.Instance.CleanUp(); _state = null; _replay = null;
            MirrorJournal.ReplicaActive = true; MirrorJournal.ResetReplicaRewards(); _operations = new();
            var replay = MirrorState.Unpack<CombatReplay>(message.Payload);
            _state = RunState.FromSerializable(replay.serializableRun); _replay = replay;
            #if STS2_0111
            RunManager.Instance.SetUpReplay(_state, replay, _wire.Identity.Source);
#else
            RunManager.Instance.SetUpReplay(_state, replay);
#endif
            RunManager.Instance.CombatStateSynchronizer.IsDisabled = true;
            Stage("加载角色资源");
            string characters = string.Join(",", _state.Players.Select(p => p.Character.Id.ToString()));
            if (_assetCharacters != characters)
            {
                await PreloadManager.LoadRunAssets(_state.Players.Select(p => p.Character));
                _assetCharacters = characters; _assetAct = "";
            }
            CheckGeneration(message.Generation);
            Stage("加载地图资源");
            string act = _state.Act.Id.ToString();
            if (_assetAct != act) { await PreloadManager.LoadActAssets(_state.Act); _assetAct = act; }
            CheckGeneration(message.Generation);
            RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(_state));
            await RunManager.Instance.GenerateMap();
            CheckGeneration(message.Generation);
            var manager = RunManager.Instance;
            manager.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            manager.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            // Live batches can arrive before a future enemy checksum exists.
            // The host compares complete native state hashes at idle points;
            // disable the offline replay's premature missing-checksum reports.
            manager.ChecksumTracker.LoadReplayChecksums(null!, replay.nextChecksumId);
            manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            manager.RewardsSetSynchronizer.FastForwardRewardIds(replay.rewardIds);
            Stage("创建原版房间");
            await manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, _state));
            if (manager.MapDrawingsToLoad != null)
            {
                NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings(manager.MapDrawingsToLoad);
                manager.MapDrawingsToLoad = null;
            }
            if (message.FastRestore && NCombatRoom.Instance != null)
                while (!LocalSpectatorSource.PendingNativeChoice && (CombatManager.Instance.IsStarting || CombatManager.Instance.IsInProgress && _state.Players[0].PlayerCombatState?.Phase != PlayerTurnPhase.Play))
                    await Frame(message.Generation);
            MirrorFastRestore.Replica = false;
            CheckGeneration(message.Generation);
            Stage("等待原版界面初始化");
            // Native room entry may remain paused while a real selection is awaited.
            Stage("同步主进程操作");
            await ReplayOperations(message.Operations);
            CheckGeneration(message.Generation);
            _sceneReady = true;
            Stage("原版界面已同步");
        }
        catch (OperationCanceledException) { Stage("旧读档任务已撤销"); }
        catch (Exception e) { _error = e.ToString(); GD.PrintErr("[RMP:Mirror] " + _error); }
        finally { MirrorFastRestore.Replica = false; _busy = false; _statusTimer = 0; }
    }
    private async Task Apply(MirrorMessage message)
    {
        try
        {
            if (_state == null || _replay == null || _error.Length > 0) return;
            await ReplayOperations(message.Operations);
        }
        catch (OperationCanceledException) { Stage("旧回放任务已撤销"); }
        catch (Exception e) { _error = e.ToString(); GD.PrintErr("[RMP:Mirror] " + _error); }
        finally { _busy = false; _statusTimer = 0; }
    }
    private void CheckGeneration(long generation)
    { if (generation != _desiredGeneration) throw new OperationCanceledException("Superseded mirror checkpoint"); }
    private async Task Frame(long generation)
    { await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame); CheckGeneration(generation); }
    private async Task ReplayOperations(List<MirrorOperation> operations)
    {
        if (operations.Count < _events) throw new InvalidDataException("Native operation prefix shortened");
        for (int i = 0; i < _events; i++)
            if (_operations[i].Kind != operations[i].Kind || _operations[i].NodeKey != operations[i].NodeKey || _operations[i].Value != operations[i].Value || !_operations[i].Payload.SequenceEqual(operations[i].Payload))
                throw new InvalidDataException("Native operation prefix changed");
        _operations = operations;
        long generation = _generation;
        while (_events < operations.Count)
        {
            CheckGeneration(generation);
            var operation = operations[_events];
            if (operation.Kind == "game")
            {
                var item = MirrorState.Unpack<CombatReplayEvent>(operation.Payload);
                var action = item.action!.ToGameAction(_state!.GetPlayer(item.playerId!.Value));
                if (action.ActionType == GameActionType.CombatPlayPhaseOnly)
                    while (!MirrorHost.SourceIdle) await Frame(generation);
                RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(action);
            }
            else if (operation.Kind == "rewardCards")
            {
                var deadline = Stopwatch.StartNew();
                while (MirrorJournal.ReplicaCardRewards.Count == 0)
                {
                    if (deadline.Elapsed.TotalSeconds > 20) throw new InvalidDataException("Native reward generation unavailable");
                    await Frame(generation);
                }
                var reward = MirrorJournal.ReplicaCardRewards.Dequeue();
                var cards = (List<CardCreationResult>)typeof(MegaCrit.Sts2.Core.Rewards.CardReward).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reward)!;
                var authoritative = MirrorState.Unpack<MirrorCardBatch>(operation.Payload).Cards;
                GD.Print("[RMP:Mirror:Rewards] native options=" + string.Join(",", cards.Select(c=>c.Card.Id)) + " authoritative=" + string.Join(",", authoritative.Select(c=>c.Id)));
                if (cards.Count != authoritative.Count) throw new InvalidDataException("Native reward option count differs");
                for (int i = 0; i < cards.Count; i++)
                    if (!cards[i].Card.ToSerializable().Equals(authoritative[i]))
                    {
                        var old = cards[i].Card;
                        var restored = _state!.LoadCard(authoritative[i], _state.Players[0]);
                        _state.RemoveCard(old); cards[i].ModifyCard(restored);
                    }
                if (typeof(MegaCrit.Sts2.Core.Rewards.CardReward).GetField("_currentlyShownScreen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reward) is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen screen)
                    screen.RefreshOptions(cards, MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative.Generate(reward));
            }
            else
            {
                var deadline = Stopwatch.StartNew();
                Node? node;
                while ((node = MirrorNativeUi.Resolve(operation.NodeKey)) == null || node is CanvasItem canvas && !canvas.IsVisibleInTree() || node is NClickableControl button && !button.IsEnabled)
                {
                    if (deadline.Elapsed.TotalSeconds > 20) throw new InvalidDataException("Native UI unavailable: " + operation.Kind + ":" + operation.NodeKey);
                    await Frame(generation);
                }
                // Map travel is rebased from the authoritative next-room
                // checkpoint. Starting a second asynchronous room entry here
                // would race that rebase and keep a freed map scene alive.
                if (operation.Kind == "button" && node is NMapPoint point) ShowMapSelection(point);
                else if (operation.Kind == "button" && node is NProceedButton && _state?.CurrentRoom?.RoomType == RoomType.Boss) { }
                else if (operation.Kind == "button" && node is NClickableControl clickable) clickable.ForceClick();
                else if (operation.Kind == "card" && node is NCardHolder holder) holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                else if (operation.Kind == "buy" && node is NMerchantSlot slot)
                {
                    if (typeof(NMerchantSlot).GetMethod("OnSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(slot, null) is Task task) _ = TaskHelper.RunSafely(task);
                }
                else if (operation.Kind == "scroll" && node is Control scroll) LocalSpectatorSource.ScrollNative(scroll, operation.Value);
                else if (operation.Kind == "potion" && node is MegaCrit.Sts2.Core.Nodes.Potions.NPotionHolder potion) _ = TaskHelper.RunSafely(potion.UsePotion());
                else if (operation.Kind == "popupCancel" && node is MegaCrit.Sts2.Core.Nodes.Potions.NPotionPopup popup) popup.Remove();
                else if (node is NMapDrawings drawings && operation.Kind.StartsWith("draw", StringComparison.Ordinal)) ReplayDrawing(drawings, operation);
                else if (operation.Kind is "target" or "cancelTarget")
                {
                    while (NTargetManager.Instance?.IsInSelection != true) await Frame(generation);
                    if (operation.Kind == "cancelTarget") NTargetManager.Instance.CancelTargeting();
                    else
                    {
                        NTargetManager.Instance.OnNodeHovered(node);
                        NTargetManager.Instance._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
                    }
                }
                else throw new InvalidDataException("Unknown native operation: " + operation.Kind);
            }
            _events++;
            await Frame(generation);
        }
    }
    private static NTargetingArrow? TargetingArrow => NRun.Instance?.GlobalUi?.TargetManager?.GetNodeOrNull<NTargetingArrow>("TargetingArrow");
    internal void SubmitNativeCard(NCardPlay play, Creature? target)
    {
        var card = _dragCard;
        bool valid = card != null && card == play.Holder.CardModel && _generation == _dragGeneration &&
            CanBufferCard && _state!.Players[0].PlayerCombatState!.TurnNumber == _dragTurn && _state.Players[0].PlayerCombatState.Hand.Cards.Contains(card);
        int turn = _dragTurn;
        CancelDrag();
        if (valid && _pendingCards.Count < 8 && !_pendingCards.Any(p => ReferenceEquals(p.Card, card)))
        {
            _pendingCards.Enqueue(new BufferedCard(card!, target, _generation, _epoch, turn, Time.GetTicksMsec()));
            if (Diagnostic) GD.Print("[RMP:Mirror:Input] buffered card=" + card!.Id + " waiting=" + _waitingForAuthority + " busy=" + _busy);
        }
    }
    private void Quit() { Dispose(); ((SceneTree)Engine.GetMainLoop()).Quit(); }
    public void Dispose()
    {
        if (DisplayServer.GetName() != "headless" && RenderingServer.Singleton.IsConnected(RenderingServer.SignalName.FramePostDraw, _drawCallback))
            RenderingServer.Singleton.Disconnect(RenderingServer.SignalName.FramePostDraw, _drawCallback);
        _input?.Dispose(); _input = null; _wire.Dispose();
    }
}
