using System;
using Godot;
using RemoveMultiplayerPlayerLimit.Infrastructure;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView : IDisposable
{
    private readonly CanvasLayer _overlay;
    private readonly Control _panel, _content;
    private readonly Label _status;
    private readonly MirrorHost _mirror;
    private readonly Func<bool, long> _setControl;
    private SpectatorSnapshot? _snapshot;
    private Button _controlToggle = null!;
    private Panel _modeThumb = null!;
    private Tween? _modeTween;
    private bool _controlEnabled;
    private long _epoch;
    private readonly int _previousFps;
    private readonly DisplayServer.VSyncMode _previousVsync;
    internal bool ControlEnabled => _controlEnabled;
    internal long Epoch => _epoch;
    internal SpectatorView(Action close, MirrorHost mirror, Func<bool, long> setControl)
    {
        _mirror = mirror; _setControl = setControl; _selectSource = _ => { };
        (_overlay, _panel, _content) = CreatePanel(close);
        var input = new ControlInput { Name = "SpectatorLocalInput", Receive = ReceiveLocal };
        _overlay.AddChild(input); input.SetProcessInput(true);
        _status = Text(_content, T("正在启动独立游戏…", "Starting independent game…"), new Rect2(14,14,480,80), 20);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.AddThemeColorOverride("font_color", Colors.White);
        _status.AddThemeColorOverride("font_outline_color", Colors.Black);
        _status.AddThemeConstantOverride("outline_size", 5);
        _previousFps = Engine.MaxFps; _previousVsync = DisplayServer.WindowGetVsyncMode();
        if (DisplayServer.GetName() != "headless") { Engine.MaxFps = 120; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); }
        SetControlEnabled(SpectatorPreferences.Current.ControlMode);
    }
    internal void Update(SpectatorSnapshot snapshot, bool verified, string error)
    {
        _snapshot = snapshot; UpdateSources(snapshot.Sources, snapshot.SourceId);
        _status.Text = error.Length > 0 ? T("独立游戏同步失败，控制已暂停：", "Independent game synchronization failed; control paused: ") + error :
            T("独立窗口：", "Independent window: ") + _mirror.Process.Phase;
    }
    private void SetControlEnabled(bool enabled)
    {
        if (_controlEnabled != enabled || _epoch == 0 || _mirror.ControlEpoch != _epoch)
        {
            _controlEnabled = enabled; _epoch = _setControl(enabled); _mirror.Control(enabled,_epoch);
            _modeTween?.Kill(); _modeTween = _modeThumb.CreateTween();
            _modeTween.TweenProperty(_modeThumb, "position:x", enabled ? 58f : 2f, .16).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        _controlToggle.SetPressedNoSignal(enabled);
    }
    internal Vector2 ShellPointer()
    {
        if (DisplayServer.GetName() == "headless") return _panel.GetViewport().GetMousePosition();
        if (!MirrorWin32.GetCursorPos(out var point) || !MirrorWin32.ScreenToClient(MirrorWin32.OwnWindow,ref point)) return new Vector2(-1,-1);
        return _panel.GetViewport().GetFinalTransform().AffineInverse() * new Vector2(point.X,point.Y);
    }
    private void ProcessControl()
    {
        _mirror.AttachWindow();
        var host = _mirror.Window;
        bool sourceDialog = QuickSl.QuickSlController.ConfirmationOpen || LiveSharingController.SourceMenuOpen;
        _overlay.Visible = !sourceDialog;
        bool combat = _snapshot != null && _mirror.Error.Length == 0;
        if (combat && _mirror.ControlEpoch != _epoch) SetControlEnabled(_controlEnabled);
        // Scene construction runs while hidden; expose only the current scene
        // so SL cannot briefly display the previous run as ready for input.
        host?.Layout(_content,combat && _mirror.Process.DisplayReady && host.Attached && SceneMonitor.FindSettingsScreen()?.IsVisibleInTree() != true && !QuickSl.QuickSlController.ConfirmationOpen && !LiveSharingController.SourceMenuOpen);
        _status.Visible = host?.Visible != true;
        if (host?.TakeEscape() == true) SetPreferredControl(false);
        _status.Position = new Vector2(_content.Size.X * .08f, _content.Size.Y * .78f);
        _status.Size = new Vector2(_content.Size.X * .84f, Math.Max(50,_content.Size.Y * .14f));
        LiveSharingController.RouteMapInput(!sourceDialog && _panel.GetGlobalRect().HasPoint(ShellPointer()));
    }
    private TResource? Asset<TResource>(string path) where TResource : Resource => ResourceLoader.Exists(path) ? ResourceLoader.Load<TResource>(path) : null;
    public void Dispose()
    {
        SetControlEnabled(false);
        if (GodotObject.IsInstanceValid(_overlay)) { RememberPanelLayout(); _overlay.Hide(); _overlay.GetParent()?.RemoveChild(_overlay); _overlay.QueueFree(); }
        if (Engine.MaxFps == 120) Engine.MaxFps = _previousFps;
        if (DisplayServer.GetName() != "headless" && DisplayServer.WindowGetVsyncMode() == DisplayServer.VSyncMode.Disabled) DisplayServer.WindowSetVsyncMode(_previousVsync);
    }
}
