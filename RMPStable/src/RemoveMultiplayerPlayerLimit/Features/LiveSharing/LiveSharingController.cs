using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
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
    internal static uint NativeHotkey { get; private set; } = 0x77;
    internal static void Initialize() { Suspend(); }
    internal static void RouteMapInput(bool insidePanel) => _source?.RouteNativeMapInput(insidePanel);
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
        var service = RunManager.Instance.NetService;
        bool local = RunManager.Instance.IsInProgress && state?.Players.Count == 1 && NRun.Instance?.IsNodeReady() == true && service != null && service.Type != NetGameType.Host && service.Type != NetGameType.Client;
        if (!local) { if (_view != null) Suspend(); return; }
        ulong session = NRun.Instance.GetInstanceId(); if (_view != null && session != _session) Suspend();
        bool settings = SceneMonitor.FindSettingsScreen()?.IsVisibleInTree() == true;
        if (pressed && !settings && _view != null) { Close(); return; }
        if (pressed) _openFailed = false;
        if (_view == null && !settings && !_openFailed && (pressed || SpectatorPreferences.Current.Open)) Open(state!);
        if (_source == null || _mirror == null || _view == null) return;
        _mirror.Tick(state!, delta); _captureTimer -= delta;
        if (_captureTimer <= 0)
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
        _session = NRun.Instance.GetInstanceId(); _source = new LocalSpectatorSource(); _mirror = new MirrorHost(state.Players[0].NetId);
        var relicRow = LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder>(NRun.Instance.GlobalUi.RelicInventory).Where(n => n.IsVisibleInTree()).Select(n => n.GetGlobalRect()).OrderBy(r => r.Position.Y).FirstOrDefault();
        SpectatorPreferences.DefaultTop = relicRow.Size.Y > 0 ? relicRow.Position.Y + relicRow.Size.Y * 3 / 5 : 116;
        _view = new SpectatorView(Close, _mirror, enabled => _source?.SetControlMode(enabled) ?? 0);
        _mirror.Intent = message => ReceiveIntent(state,message);
        _captureTimer = 0; SpectatorPreferences.Current.Open = true; SpectatorPreferences.Save();
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
            _ = _mirror.Process.Wire.Send(new MirrorMessage { Kind = "result", Generation = message.Generation, Epoch = message.Epoch, Request = message.Request, Accepted = result.Accepted, Room = result.Message });
    }
    internal static SpectatorCommandResult ResolveIntent(RunState state, MirrorMessage message)
    {
        SpectatorCommandResult Reject(string text) => new() { RequestId = message.Request, Message = text };
        if (message.Kind != "intent" || message.Request <= 0 || _mirror?.AuthorizeIntent(state,message) != true || _source == null || _view?.ControlEnabled != true || _view.Epoch != message.Epoch)
            return Reject("状态或控制权限已变化，请重新操作");
        var snapshot = _source.CaptureCommands(state);
        if (snapshot.Page != "combat") return Reject("当前页面不支持控制");
        var action = snapshot.Control.Actions.FirstOrDefault(a => a.Enabled && a.Kind == message.Model &&
            (a.Kind == "endTurn" && message.Index == -1 || a.Kind == "play" && a.NativeIndex == message.Index));
        if (action == null) return Reject("当前无法执行此操作");
        var target = snapshot.Control.Targets.FirstOrDefault(t => t.NativeIndex == message.TargetIndex && action.TargetIds.Contains(t.Id));
        if (action.RequiresTarget && target == null) return Reject("目标已失效");
        return Execute(state,new SpectatorCommand { Session = snapshot.Session, SourceId = snapshot.SourceId, Context = snapshot.Control.Context,
            Epoch = message.Epoch, RequestId = message.Request, ActionId = action.Id, TargetId = action.RequiresTarget ? target!.Id : "" });
    }
    internal static void Close() { SpectatorPreferences.Current.Open = false; SpectatorPreferences.Save(); Suspend(); }
    internal static void Suspend()
    {
        _view?.Dispose(); _view = null; _mirror?.Dispose(); _mirror = null; _source?.Dispose(); _source = null; _session = 0;
    }
}
