using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// The content is one texture from the separate native game. No model-free
// imitation of cards, actors, map layers or native tooltips lives in this view.
internal sealed partial class SpectatorView : IDisposable
{
    private readonly CanvasLayer _overlay;
    private readonly Control _panel;
    private readonly SubViewport _viewport;
    private readonly TextureRect _picture;
    private readonly Label _status;
    private ImageTexture? _texture;
    private List<MirrorHit> _hits = new();
    private string _frameHash = "";
    private readonly MirrorHost _mirror;
    private readonly Func<bool, long> _setControl;
    private readonly Func<SpectatorCommand, SpectatorCommandResult> _execute;
    private SpectatorSnapshot? _snapshot;
    private Button _controlToggle = null!;
    private Panel _modeThumb = null!;
    private Tween? _modeTween;
    private bool _controlEnabled, _verified;
    private long _epoch, _request;
    private ControlActionSnapshot? _dragAction;
    private Vector2 _dragStart;
    private string _dragContext = "";
    internal SpectatorView(Action close, MirrorHost mirror, Func<bool, long> setControl, Func<SpectatorCommand, SpectatorCommandResult> execute)
    {
        _mirror = mirror; _setControl = setControl; _execute = execute; _selectSource = _ => { };
        (_overlay, _panel, _viewport) = CreatePanel(close);
        _picture = new TextureRect { Name = "NativeGameFrame", Size = new Vector2(1920,1080), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Stop };
        _viewport.AddChild(_picture); _picture.GuiInput += Receive;
        var input = new ControlInput { Name = "SpectatorLocalInput", Receive = ReceiveLocal };
        _overlay.AddChild(input); input.SetProcessInput(true);
        _status = Text(_picture, T("正在启动独立游戏…", "Starting independent game…"), new Rect2(14,1028,1892,40), 24);
        mirror.Frame = NativeFrame;
        SetControlEnabled(SpectatorPreferences.Current.ControlMode);
    }
    private void NativeFrame(MirrorMessage message)
    {
        using var image = new Image();
        if (image.LoadPngFromBuffer(message.Payload) != Error.Ok || image.GetWidth() != message.Width || image.GetHeight() != message.Height)
            throw new InvalidOperationException("Invalid native frame");
        if (_texture == null || _texture.GetWidth() != image.GetWidth() || _texture.GetHeight() != image.GetHeight())
        { _texture?.Dispose(); _texture = ImageTexture.CreateFromImage(image); _picture.Texture = _texture; }
        else _texture.Update(image);
        _hits = message.Hits; _frameHash = message.Idle ? message.Hash : "";
    }
    internal void Update(SpectatorSnapshot snapshot, bool verified, string error)
    {
        if (_dragAction != null && (_dragContext != snapshot.Control.Context || !verified)) CancelDrag();
        _snapshot = snapshot; _verified = verified && _mirror.FrameEvents == _mirror.Process.LastEvents && _frameHash == _mirror.Process.LastHash;
        UpdateSources(snapshot.Sources, snapshot.SourceId);
        _status.Text = error.Length > 0 ? T("独立游戏同步失败，控制已暂停", "Independent game synchronization failed; control paused") : snapshot.Page != "combat" ?
            T("当前探索版本仅支持战斗，其他页面尚未接入", "This exploration supports combat; other pages are pending") : !verified ?
            T("正在同步，控制暂不可用", "Synchronizing; control temporarily unavailable") : "";
    }
    private void SetControlEnabled(bool enabled)
    {
        if (_controlEnabled != enabled)
        {
            _controlEnabled = enabled; _epoch = _setControl(enabled); CancelDrag();
            _modeTween?.Kill(); _modeTween = _modeThumb.CreateTween();
            _modeTween.TweenProperty(_modeThumb, "position:x", enabled ? 58f : 2f, .16).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        _controlToggle.SetPressedNoSignal(enabled);
    }
    private void ProcessControl()
    {
        LiveSharingController.RouteMapInput(_panel.GetGlobalRect().HasPoint(_panel.GetViewport().GetMousePosition()));
        if (_dragAction != null && (!Input.IsMouseButtonPressed(MouseButton.Left) || !_verified)) CancelDrag();
    }
    private static Rect2 Rect(float[] r) => r.Length == 4 ? new Rect2(r[0],r[1],r[2],r[3]) : default;
    private ControlTargetSnapshot? Target(Vector2 point, ControlActionSnapshot action)
    {
        var hit = _hits.LastOrDefault(h => h.Kind == "target" && Rect(h.Rect).HasPoint(point) && _snapshot!.Control.Targets.Any(t => t.NativeIndex == h.Index && action.TargetIds.Contains(t.Id)));
        return hit == null ? null : _snapshot!.Control.Targets.FirstOrDefault(t => t.NativeIndex == hit.Index);
    }
    private void Receive(InputEvent input)
    {
        if (_snapshot == null) return;
        if (input is InputEventKey { Keycode: Key.Escape, Pressed: true }) { if (_dragAction != null) CancelDrag(); else SetPreferredControl(false); _picture.AcceptEvent(); return; }
        if (input is not InputEventMouse mouse) return;
        Vector2 normalized = mouse.Position / new Vector2(1920,1080);
        var point = normalized * new Vector2(_snapshot.Width,_snapshot.Height);
        if (input is InputEventMouseMotion)
        {
            _mirror.Presentation(new MirrorMessage { Kind = "pointer", X = normalized.X, Y = normalized.Y });
            if (_dragAction != null)
            {
                var target = Target(normalized, _dragAction);
                _mirror.Presentation(new MirrorMessage { Kind = "arrow", X = normalized.X, Y = normalized.Y, StartX = _dragStart.X, StartY = _dragStart.Y, Highlight = target == null ? 0 : target.Enemy ? 2 : 1 });
            }
        }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        if (!_controlEnabled || !_verified || _snapshot.Page != "combat") { CancelDrag(); return; }
        if (click.Pressed)
        {
            var hit = _hits.LastOrDefault(h => h.Kind is "play" or "endTurn" && Rect(h.Rect).HasPoint(normalized));
            var action = hit == null ? null : _snapshot.Control.Actions.LastOrDefault(a => a.Enabled && a.Kind == hit.Kind && (hit.Kind == "endTurn" || a.NativeIndex == hit.Index));
            if (action == null) return;
            if (action.CardId.Length == 0) Submit(action.Id, "");
            else { _dragAction = action; _dragStart = normalized; _dragContext = _snapshot.Control.Context; }
        }
        else if (_dragAction != null)
        {
            var action = _dragAction;
            var target = Target(normalized, action);
            CancelDrag();
            if (!action.RequiresTarget || target != null) Submit(action.Id, target?.Id ?? "");
        }
        _picture.AcceptEvent();
    }
    private void Submit(string action, string target)
    {
        if (!_verified || _snapshot == null) return;
        var result = _execute(new SpectatorCommand { Session = _snapshot.Session, SourceId = _snapshot.SourceId, Context = _snapshot.Control.Context, Epoch = _epoch, RequestId = ++_request, ActionId = action, TargetId = target });
        _verified = false;
        if (!result.Accepted) _status.Text = result.Message;
    }
    private void CancelDrag() { if (_dragAction != null) _mirror.Presentation(new MirrorMessage { Kind = "arrow", Highlight = -1 }); _dragAction = null; }
    private TResource? Asset<TResource>(string path) where TResource : Resource => ResourceLoader.Exists(path) ? ResourceLoader.Load<TResource>(path) : null;
    public void Dispose()
    {
        SetControlEnabled(false); _mirror.Frame = null;
        if (GodotObject.IsInstanceValid(_overlay)) { RememberPanelLayout(); _overlay.Hide(); _overlay.GetParent()?.RemoveChild(_overlay); _overlay.QueueFree(); }
        _texture?.Dispose();
    }
}
