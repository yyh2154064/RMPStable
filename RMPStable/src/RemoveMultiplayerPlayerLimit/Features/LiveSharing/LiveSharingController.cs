using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Helpers;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using RemoveMultiplayerPlayerLimit.Infrastructure;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static partial class LiveSharingController
{
    private static SpectatorView? _view;
    private static LocalSpectatorSource? _source;
    private static MirrorHost? _mirror;
    private static MirrorRenderer? _renderer;
    private static ulong _session;
    private static double _settingsTimer, _captureTimer;
    private static bool _wasDown, _rendererFailed;
    private static bool _openFailed;
    private static string _lastError = "";
    private static bool _restoring, _restorePrepared;
    internal static bool SourceMenuOpen { get; private set; }
    private static bool _sourceMenuStarting;
    private static MegaCrit.Sts2.Core.Multiplayer.Replay.CombatReplay? _restoreOldCheckpoint;
    internal static uint NativeHotkey { get; private set; } = 0x77;
    internal static void Initialize() { Suspend(); MirrorJournal.Initialize(); }
    internal static void RouteMapInput(bool insidePanel) => _source?.RouteNativeMapInput(insidePanel);
    internal static bool MapInputInPanel => _source?.MapInputInPanel == true || _mirror?.Window?.PointerInside == true;
    internal static void ProcessFrame(double delta)
    {
        try { Process(delta); }
        catch (Exception e)
        {
            if (_lastError != e.Message) Log.Warn("[RMP:Mirror] " + e);
            _lastError = e.Message;
            if (MirrorRenderer.IsRenderer) { _rendererFailed = true; ((SceneTree)Engine.GetMainLoop()).Quit(1); }
            else { _openFailed = true; Suspend(); }
        }
    }
    private static void Process(double delta)
    {
        if (MirrorRenderer.IsRenderer) { if (!_rendererFailed) { _renderer ??= new MirrorRenderer(); _renderer.ProcessFrame(delta); } return; }
        _settingsTimer -= delta;
        if (_settingsTimer <= 0) { RegisterInput(); _settingsTimer = .5; }
        Key key = Key.None;
        if (NInputManager.Instance != null && Keys?.GetValue(NInputManager.Instance) is Dictionary<StringName,Key> map) map.TryGetValue(Action, out key);
        NativeHotkey = key >= Key.F1 && key <= Key.F35 ? (uint)(0x70 + (int)(key-Key.F1)) : (uint)key;
        bool down = key != Key.None && Input.IsKeyPressed(key), pressed = down && !_wasDown; _wasDown = down;
        SpectatorPreferences.LoadProfile(Suspend);
        var state = GameStateAccessor.GetRunState();
        if (SourceMenuOpen && !_sourceMenuStarting && (state == null || NCapstoneContainer.Instance?.CurrentCapstoneScreen == null && NModalContainer.Instance?.OpenModal == null)) SourceMenuOpen = false;
        if (SourceMenuOpen && state?.IsGameOver == true) { Close(); SourceMenuOpen = false; }
        var service = RunManager.Instance.NetService;
        if (!RunManager.Instance.IsInProgress && SaveManager.Instance.IsProfileInitialized && NGame.Instance?.MainMenu?.IsNodeReady() == true &&
            service?.Type is not NetGameType.Host and not NetGameType.Client) EnsureMirror(1);
        bool local = RunManager.Instance.IsInProgress && state?.Players.Count == 1 && NRun.Instance?.IsNodeReady() == true && service != null && service.Type != NetGameType.Host && service.Type != NetGameType.Client;
        if (service?.Type is NetGameType.Host or NetGameType.Client) { if (_mirror != null) Suspend(); return; }
        if (!local || _restoring)
        {
            if (_restoring && _restorePrepared) PublishRestoreCheckpoint();
            if (!_restoring && _session != 0) { HideView(); _mirror?.Invalidate(); _session = 0; }
            _mirror?.Tick(null, delta);
            return;
        }
        EnsureMirror(state!);
        ulong session = NRun.Instance.GetInstanceId();
        if (session != _session)
        {
            HideView(); _source?.Dispose(); _source = new LocalSpectatorSource(); _session = session;
        }
        bool settings = SceneMonitor.FindSettingsScreen()?.IsVisibleInTree() == true;
        if (pressed && !settings && _view != null) { Close(); return; }
        if (pressed) _openFailed = false;
        if (_view == null && !settings && !_openFailed && (pressed || SpectatorPreferences.Current.Open)) Open(state!);
        if (_source == null || _mirror == null) return;
        bool wasVerified = _mirror.Verified;
        _mirror.Tick(state!, delta); _captureTimer -= delta;
        if (_mirror.Verified != wasVerified) _captureTimer = 0;
        if (_view != null && _captureTimer <= 0)
        {
            _captureTimer = .05;
            var snapshot = _source.CaptureCommands(state!);
            if (SpectatorPreferences.Current.SourceId != snapshot.SourceId) { SpectatorPreferences.Current.SourceId = snapshot.SourceId; SpectatorPreferences.Save(); }
            _view.Update(snapshot, _mirror.Verified, _mirror.Error);
        }
    }
    internal static void Open(RunState state)
    {
        if (_view != null) return;
        if (_mirror?.Error.Length > 0) { _mirror.Dispose(); _mirror = null; _openFailed = false; }
        EnsureMirror(state);
        _session = NRun.Instance.GetInstanceId(); _source ??= new LocalSpectatorSource();
        var relicRow = LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder>(NRun.Instance.GlobalUi.RelicInventory).Where(n => n.IsVisibleInTree()).Select(n => n.GetGlobalRect()).OrderBy(r => r.Position.Y).FirstOrDefault();
        SpectatorPreferences.DefaultTop = relicRow.Size.Y > 0 ? relicRow.Position.Y + relicRow.Size.Y * 3 / 5 : 116;
        _view = new SpectatorView(Close, _mirror, enabled => _source?.SetControlMode(enabled) ?? 0);
        _captureTimer = 0; SpectatorPreferences.Current.Open = true; SpectatorPreferences.Save();
    }
    internal static void RecoverMirror()
    {
        var state = GameStateAccessor.GetRunState();
        if (MirrorRenderer.IsRenderer || _restoring || state?.Players.Count != 1 || RunManager.Instance.NetService?.Type != NetGameType.Singleplayer) return;
        // Reconstruct only our authenticated replica from the original journal.
        // The source run, save, RNG and combat checkpoint are never reloaded.
        HideView(); _mirror?.Dispose(); _mirror = null; _openFailed = false;
        GD.Print("[RMP:Mirror:Recovery] rebuilding replica from checkpoint and " + MirrorJournal.Operations.Count + " operations");
        Open(state);
    }
    private static void EnsureMirror(RunState state)
        => EnsureMirror(state.Players[0].NetId);
    private static void EnsureMirror(ulong source)
    {
        if (_openFailed) return;
        if (_mirror != null && _mirror.Process.Wire.Identity.Source != source) Suspend();
        if (_mirror != null) return;
        // Prewarm when a singleplayer run is ready, before the first F8.
        _mirror = new MirrorHost(source);
        _mirror.Intent = message =>
        {
            var current = GameStateAccessor.GetRunState();
            if (!_restoring && current != null) ReceiveIntent(current, message);
        };
    }
    internal static void BeginSingleplayerRestore()
    {
        if (MirrorRenderer.IsRenderer) return;
        _restoreOldCheckpoint = MirrorJournal.Checkpoint;
        _restoring = true; _restorePrepared = false; HideView(); _mirror?.Invalidate(); _session = 0;
    }
    internal static void PrepareSingleplayerRestore() { _restorePrepared = true; }
    internal static void EndSingleplayerRestore() { _restoring = false; _restorePrepared = false; }
    internal static void PublishRestoreCheckpoint()
    {
        if (!MirrorRenderer.IsRenderer && _restoring && _restorePrepared)
        {
            if (GameStateAccessor.GetRunState() is { } state) MirrorJournal.EnsureCheckpoint(state);
            if (MirrorJournal.Checkpoint is { } replay && !ReferenceEquals(replay, _restoreOldCheckpoint)) _mirror?.PublishCheckpoint(replay, true);
        }
    }
    private static SpectatorCommandResult Execute(RunState state, SpectatorCommand command)
    {
        if (_mirror?.Authorize(state) != true || _source == null) return new SpectatorCommandResult { RequestId = command.RequestId, Message = "等待双方状态一致" };
        var result = _source.ExecuteCommand(command); _captureTimer = 0; return result;
    }
    private static void ReceiveIntent(RunState state, MirrorMessage message)
    {
        var result = ResolveIntent(state,message);
        if (_mirror?.Process.Authenticated == true)
            _ = _mirror.Process.Wire.Send(new MirrorMessage { Kind = "result", Generation = message.Generation, Epoch = message.Epoch, Request = message.Request, Model = message.Model, Accepted = result.Accepted, Room = result.Message });
    }
    internal static SpectatorCommandResult ResolveIntent(RunState state, MirrorMessage message)
    {
        SpectatorCommandResult Reject(string text) => new() { RequestId = message.Request, Message = text };
        if (message.Kind != "intent" || message.Request <= 0 || _mirror?.AuthorizeIntent(state,message) != true || _source == null || _view?.ControlEnabled != true || _view.Epoch != message.Epoch)
            return Reject("状态或控制权限已变化，请重新操作");
        if (message.Model == "menu")
        {
            if (message.Value is not "GiveUp" and not "SaveAndQuit" and not "Compendium" and not "RmpQuickSl" || SourceMenuOpen ||
                NModalContainer.Instance?.OpenModal != null || NRun.Instance?.GlobalUi.TopBar.Pause.IsEnabled != true) return Reject("当前无法执行此操作");
            if (message.Value == "RmpQuickSl") QuickSl.QuickSlController.RequestFromMirror();
            else
            {
                SourceMenuOpen = _sourceMenuStarting = true;
                TaskHelper.RunSafely(ActivateSourceMenu(state, message));
            }
            return new() { RequestId = message.Request, Accepted = true, Message = "已提交" };
        }
        var snapshot = _source.CaptureCommands(state);
        var action = snapshot.Control.Actions.FirstOrDefault(a => a.Enabled && a.Kind == message.Model &&
            (message.NodeKey.Length > 0 ? a.NativePath == message.NodeKey :
            a.Kind == "endTurn" && message.Index == -1 || a.Kind == "play" && a.NativeIndex == message.Index));
        if (action == null) return Reject("当前无法执行此操作");
        var target = snapshot.Control.Targets.FirstOrDefault(t => t.NativeIndex == message.TargetIndex && action.TargetIds.Contains(t.Id));
        if (action.RequiresTarget && target == null) return Reject("目标已失效");
        return Execute(state,new SpectatorCommand { Session = snapshot.Session, SourceId = snapshot.SourceId, Context = snapshot.Control.Context,
            Epoch = message.Epoch, RequestId = message.Request, ActionId = action.Id, TargetId = action.RequiresTarget ? target!.Id : action.Kind is "scroll" or "mapInput" ? message.Value : "" });
    }
    private static async Task ActivateSourceMenu(RunState state, MirrorMessage message)
    {
        try
        {
            NRun.Instance.GlobalUi.TopBar.Pause.ForceClick();
            for (int frame = 0; frame < 30; frame++)
            {
                if (!ReferenceEquals(state, GameStateAccessor.GetRunState()) || _mirror?.Process.Wire.Identity.Current(message) != true || _view?.Epoch != message.Epoch) return;
                if (NCapstoneContainer.Instance?.CurrentCapstoneScreen is Node root && LocalSpectatorSource.Descendants<NPauseMenuButton>(root)
                    .FirstOrDefault(b => b.Name.ToString() == message.Value && b.IsNodeReady() && b.IsEnabled && b.IsVisibleInTree()) is { } button)
                { button.ForceClick(); return; }
                await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        finally
        {
            _sourceMenuStarting = false;
            if (NCapstoneContainer.Instance?.CurrentCapstoneScreen == null && NModalContainer.Instance?.OpenModal == null) SourceMenuOpen = false;
        }
    }
    internal static void Close() { SpectatorPreferences.Current.Open = false; SpectatorPreferences.Save(); HideView(); }
    private static void HideView()
    {
        _view?.Dispose(); _view = null; _mirror?.Window?.Hide(); RouteMapInput(false);
    }
    internal static void Suspend()
    {
        SourceMenuOpen = _sourceMenuStarting = false;
        HideView(); _mirror?.Dispose(); _mirror = null; _source?.Dispose(); _source = null; _session = 0;
    }
}
