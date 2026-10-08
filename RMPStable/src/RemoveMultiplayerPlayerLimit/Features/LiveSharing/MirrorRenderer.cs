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
    private bool _drawing;
    private int _uiDiagnostics;
    private long _generation;
    private RunState? _state;
    private CombatReplay? _replay;
    private int _events;
    private double _statusTimer, _frameTimer, _parentTimer;
    private string _error = "";
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
        Engine.MaxFps = 30;
    }
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
        if (!_hello) { _hello = true; _ = _wire.Send(new MirrorMessage { Kind = "hello", Build = MirrorState.Build, Process = Environment.ProcessId }); }
        // Loads/events mutate native state only on the Godot thread. A queue
        // remains ordered while native asynchronous actions are running.
        if (!_busy && (_state != null || NGame.Instance.MainMenu != null) && SaveManager.Instance.IsProfileInitialized)
        {
            while (_wire.Incoming.TryDequeue(out var message))
            {
                if (message.Kind == "load" && message.Generation > _generation)
                { _busy = true; TaskHelper.RunSafely(Load(message)); break; }
                if (message.Generation != _generation) continue;
                if (message.Kind == "events") { _busy = true; TaskHelper.RunSafely(Apply(message)); break; }
                if (message.Kind == "pointer") Pointer(message);
                if (message.Kind == "arrow") Arrow(message);
            }
        }
        _statusTimer -= delta; _frameTimer -= delta;
        if (_statusTimer <= 0)
        {
            _statusTimer = .25;
            bool idle = !_busy && _error.Length == 0 && _state != null && !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused;
            _ = _wire.Send(new MirrorMessage { Kind = "status", Generation = _generation, Events = _events, Idle = idle,
                Hash = idle && _replay != null ? MirrorState.Hash(_state!) : "", Hits = idle ? CaptureHits() : new(), Room = _error });
            if (idle && _events > 0 && _uiDiagnostics++ < 8)
                GD.Print("[RMP:Mirror:UI] hand=" + _state!.Players[0].PlayerCombatState?.Hand.Cards.Count + " holders=" + NPlayerHand.Instance?.ActiveHolders.Count +
                    " visible=" + NPlayerHand.Instance?.IsVisibleInTree() + " hits=" + string.Join(";", CaptureHits().Select(h => h.Kind + ":" + h.Index + ":" + string.Join(",",h.Rect))));
        }
        if (_frameTimer <= 0 && !_busy && !_drawing && _state != null && _wire.Pending == 0 && DisplayServer.GetName() != "headless")
        {
            _frameTimer = 1d / 15; _drawing = true; TaskHelper.RunSafely(CaptureFrame());
        }
    }
    private async Task CaptureFrame()
    {
        try
        {
            await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var viewport = NGame.Instance.GetViewport();
            using var picture = viewport.GetTexture().GetImage();
            bool idle = !_busy && _error.Length == 0 && _replay != null && !RunManager.Instance.ActionExecutor.IsRunning && !RunManager.Instance.ActionExecutor.IsPaused;
            if (picture != null && !picture.IsEmpty() && _wire.Connected && _wire.Pending < 2)
                _ = _wire.Send(new MirrorMessage { Kind = "frame", Generation = _generation, Width = picture.GetWidth(), Height = picture.GetHeight(),
                    Events = _events, Idle = idle, Hash = idle ? MirrorState.Hash(_state!) : "", Hits = CaptureHits(), Payload = picture.SavePngToBuffer() });
        }
        finally { _drawing = false; }
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
            _generation = message.Generation; _wire.Identity.Generation = _generation; _events = 0; _error = "";
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
            await PreloadManager.LoadRunAssets(_state.Players.Select(p => p.Character));
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
            await manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, _state));
            while (manager.ActionExecutor.IsPaused) await Frame();
            await ReplayEvents(replay);
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
    private static void Pointer(MirrorMessage message)
    {
        var viewport = NGame.Instance.GetViewport();
        var point = new Vector2(message.X, message.Y) * viewport.GetVisibleRect().Size;
        viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
    }
    private static void Arrow(MirrorMessage message)
    {
        var arrow = LocalSpectatorSource.Descendants<NTargetingArrow>(NCombatRoom.Instance).FirstOrDefault();
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
    public void Dispose() => _wire.Dispose();
}
