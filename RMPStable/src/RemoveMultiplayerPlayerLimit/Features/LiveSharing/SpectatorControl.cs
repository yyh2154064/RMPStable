using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private readonly Func<bool, long>? _setControl;
	private readonly Func<SpectatorCommand, SpectatorCommandResult>? _executeCommand;
	private Button _controlToggle = null!;
	private Control _controlLayer = null!;
	private bool _controlEnabled;
	private long _controlEpoch, _requestCounter;
	private ControlSnapshot? _renderedControl;
	private string _pendingControlContext = "", _controlMessage = "";
	private double _pendingControlTime;
	private SpectatorCommand? _lastControlCommand;
	private SpectatorCommandResult? _lastControlResult;
	private CardSnapshot? _dragCard;
	private ControlActionSnapshot? _dragAction;
	private string _dragContext = "";
	private Node2D? _dragVisual;
	private Node2D? _dragArrow;
	private readonly List<Sprite2D> _dragSegments = new();
	private Sprite2D? _dragHead;
	private string _dragTarget = "";
	private Tween? _dragHeadTween;
	private Vector2 _dragStart;
	private Panel _modeThumb = null!;
	private Tween? _modeTween;
	private bool _outsideLeftDown, _escapeDown;
	private string _captureError = "";
	private readonly Dictionary<string, Button> _actionButtons = new();
	// Use viewport input coordinates; headless GetMousePosition can report an unrelated OS position.
	private Vector2? _viewerPointer;

	private sealed class ControlInput : Node
	{
		private static readonly StringName InputMethod = new("_Input");
		internal Action<InputEvent>? Receive;
		public override void _Input(InputEvent input) => Receive?.Invoke(input);
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList() => new()
		{
			new(InputMethod, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal,
				new List<PropertyInfo> { new(Variant.Type.Object, "input", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false) }, null)
		};
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == InputMethod && args.Count == 1) { _Input(VariantUtils.ConvertTo<InputEvent>(in args[0])); ret = default; return true; }
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}
		protected override bool HasGodotClassMethod(in godot_string_name method) => method == InputMethod || base.HasGodotClassMethod(in method);
	}
	private Button CreateControlToggle()
	{
		var button = new Button { Name = "SpectatorControlToggle", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, Size = new Vector2(116, 36), TooltipText = T("观战 / 控制（记住选择）", "Watch / Control (remember selection)") };
		button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("152126"), CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16 });
		_modeThumb = new Panel { Position = new Vector2(2, 2), Size = new Vector2(56, 32), MouseFilter = Control.MouseFilterEnum.Ignore };
		_modeThumb.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("42696f"), CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14 });
		button.AddChild(_modeThumb);
		Text(button, T("观战", "Watch"), new Rect2(2, 2, 56, 32), 14, HorizontalAlignment.Center);
		Text(button, T("控制", "Control"), new Rect2(58, 2, 56, 32), 14, HorizontalAlignment.Center);
		button.Toggled += SetPreferredControl;
		return button;
	}
	private void SetPreferredControl(bool enabled)
	{
		SpectatorPreferences.Current.ControlMode = enabled; SpectatorPreferences.Save(); SetControlEnabled(enabled);
	}
	private void CreateControlLayer()
	{
		_controlLayer = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080)); _controlLayer.Name = "SpectatorControlLayer"; _controlLayer.ZIndex = 200;
		var input = new ControlInput { Name = "SpectatorControlInput", Receive = ControlInputEvent };
		_viewport.AddChild(input); input.SetProcessInput(true);
		var localInput = new ControlInput { Name = "SpectatorLocalInput", Receive = LocalControlInputEvent };
		_overlay.AddChild(localInput); localInput.SetProcessInput(true);
	}
	private void LocalControlInputEvent(InputEvent input)
	{
		if (input is InputEventMouse outsideMouse && !_panel.GetNode<Control>("SpectatorContent").GetGlobalRect().HasPoint(outsideMouse.Position))
		{ _viewerPointer = null; ProcessCardHover(new Vector2(-1, -1)); ProcessScreenHover(new Vector2(-1, -1)); }
		if (input is InputEventMouse mouse) LiveSharingController.RouteMapInput(_panel.GetGlobalRect().HasPoint(mouse.Position));
		if (_controlEnabled && _panel.GetGlobalRect().HasPoint(_panel.GetViewport().GetMousePosition()) && input is InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false })
		{
			_escapeDown = true;
			if (_dragCard != null) CancelControlDrag();
			else if (CanControlPage && _snapshot!.Control.Actions.FirstOrDefault(a => a.Kind == "cancel" && a.Enabled) is { } cancel) SubmitControl(cancel.Id);
			else SetPreferredControl(false);
			_overlay.GetViewport().SetInputAsHandled(); return;
		}
		if (!_controlEnabled || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button || _panel.GetGlobalRect().HasPoint(button.Position)) return;
		if (_mapStroke) EndMapStroke();
		if (_dragCard != null) CancelControlDrag();
	}
	private void SetControlEnabled(bool enabled)
	{
		enabled &= _setControl != null && _executeCommand != null && _snapshot != null;
		if (_controlEnabled == enabled) { _controlToggle.SetPressedNoSignal(enabled); return; }
		EndMapStroke(); _controlEnabled = enabled; _controlEpoch = _setControl?.Invoke(enabled) ?? 0;
		_controlToggle.SetPressedNoSignal(enabled);
		_modeTween?.Kill(); _modeTween = _modeThumb.CreateTween();
		_modeTween.TweenProperty(_modeThumb, "position:x", enabled ? 58f : 2f, 0.16).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		CancelControlDrag(); _pendingControlContext = ""; _controlMessage = ""; _renderedControl = null;
		if (_snapshot != null) { CloseInspect(); UpdateControl(_snapshot); }
	}
	private bool CanControlPage => _controlEnabled && !_showDeck && BrowsePile.Length == 0 && _inspectIndex < 0 && _relicIndex < 0;
	private void UpdateControl(SpectatorSnapshot snapshot)
	{
		if (_pendingControlContext.Length > 0 && snapshot.Control.Context != _pendingControlContext) { _pendingControlContext = ""; _controlMessage = ""; }
		if (_dragCard != null && (snapshot.Control.Context != _dragContext || !snapshot.Control.Actions.Any(a => a.Id == _dragAction?.Id && a.Enabled))) CancelControlDrag();
		_controlLayer.Visible = CanControlPage;
		if (ReferenceEquals(snapshot.Control, _renderedControl)) return;
		_renderedControl = snapshot.Control;
		var present = new HashSet<string>();
		foreach (var action in snapshot.Control.Actions.Where(a => a.CardId.Length == 0 && a.Kind is not "scroll" and not "mapInput"))
		{
			var rect = Rectangle(action.Rect);
			if (rect.Size.X <= 0 || rect.Size.Y <= 0) continue;
			present.Add(action.Id);
			if (!_actionButtons.TryGetValue(action.Id, out var button))
			{
				button = new Button { Name = "SpectatorCommand_" + action.Id.Replace(':', '_'), Flat = true, FocusMode = Control.FocusModeEnum.None };
				foreach (var style in new[] { "normal", "hover", "pressed", "focus", "disabled" }) button.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
				string id = action.Id; button.Pressed += () => SubmitControl(id);
				_controlLayer.AddChild(button); _actionButtons[id] = button;
			}
			button.Position = rect.Position * _page.Scale; button.Size = rect.Size * _page.Scale;
			button.Disabled = !action.Enabled;
			button.TooltipText = snapshot.Hovers.Any(h => Rectangle(h.Rect).HasPoint(rect.GetCenter())) ? "" : action.Label;
		}
		foreach (var old in _actionButtons.Keys.Where(id => !present.Contains(id)).ToArray())
		{ var button = _actionButtons[old]; _controlLayer.RemoveChild(button); button.QueueFree(); _actionButtons.Remove(old); }
	}
	private void SubmitControl(string actionId, string targetId = "", string? context = null)
	{
		if (!CanControlPage || _snapshot == null || _pendingControlContext.Length > 0) return;
		var request = new SpectatorCommand { Session = _snapshot.Session, SourceId = _snapshot.SourceId, Context = context ?? _snapshot.Control.Context, Epoch = _controlEpoch, RequestId = ++_requestCounter, ActionId = actionId, TargetId = targetId };
		_lastControlCommand = request;
		var result = _executeCommand!(request);
		_lastControlResult = result;
		_controlMessage = result.Message;
		bool continuous = _snapshot.Control.Actions.Any(a => a.Id == actionId && a.Kind is "scroll" or "mapInput");
		if (result.Accepted && !continuous)
		{
			_pendingControlContext = request.Context; _pendingControlTime = 0;
			foreach (var button in _actionButtons.Values) button.Disabled = true;
		}
	}
	private bool HandleControlCard(Control slot, CardSnapshot card, InputEvent input)
	{
		if (!CanControlPage || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return false;
		var action = _snapshot!.Control.Actions.FirstOrDefault(a => a.CardId == card.ControlId);
		if (action == null)
		{
			// A left click in a decision scene must never fall back to inspection.
			// This also protects disabled/sold items and stale scene transitions.
			if (_snapshot.Page is "deck" or "run") return false;
			slot.AcceptEvent(); return true;
		}
		slot.AcceptEvent();
		if (!action.Enabled || _pendingControlContext.Length > 0) { _controlMessage = T("当前无法执行，请等待游戏更新", "Unavailable; wait for game update"); return true; }
		if (action.Kind != "play") { SubmitControl(action.Id); return true; }
		CancelControlDrag(); Clear(_preview);
		_dragCard = card; _dragAction = action; _dragContext = _snapshot.Control.Context; _dragStart = _viewerPointer ?? _viewport.GetMousePosition();
		_dragVisual = new Node2D { Position = _dragStart, ZIndex = 10 }; _controlLayer.AddChild(_dragVisual);
		DrawCard(_dragVisual, card, Vector2.Zero, 0.8f, centered: true);
		_dragArrow = new Node2D { Name = "SpectatorTargetingArrow", ZIndex = 11 }; _controlLayer.AddChild(_dragArrow);
        for (int i = 0; i < 19; i++)
        {
            var segment = new Sprite2D { Texture = Asset<Texture2D>("res://images/ui/combat/targeting_arrow_segment.png") };
            _dragArrow.AddChild(segment); _dragSegments.Add(segment);
        }
        _dragHead = new Sprite2D { Texture = Asset<Texture2D>("res://images/ui/combat/targeting_arrow_head.png"), Scale = Vector2.One * 0.95f };
        _dragArrow.AddChild(_dragHead); UpdateDragArrow(_dragStart);
		return true;
	}
	private void ControlInputEvent(InputEvent input)
	{
		if (input is InputEventMouse mouse) _viewerPointer = mouse.Position;
		if (HandleMapInput(input)) return;
		if (CanControlPage && _dragCard == null && input is InputEventMouseButton { Pressed: true } click)
		{
			if (click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
			{
				var scroll = _snapshot!.Control.Actions.LastOrDefault(a => a.Kind == "scroll" && a.Enabled && Rectangle(a.Rect).HasPoint(click.Position / _page.Scale));
				if (scroll != null) { SubmitControl(scroll.Id, (click.ButtonIndex == MouseButton.WheelUp ? "up:" : "down:") + Math.Clamp(click.Factor, 0.01f, 64).ToString(System.Globalization.CultureInfo.InvariantCulture)); _viewport.SetInputAsHandled(); }
			}
			else if (click.ButtonIndex == MouseButton.Right && _snapshot!.Control.Actions.FirstOrDefault(a => a.Kind == "cancel") is { } cancel)
			{ SubmitControl(cancel.Id); _viewport.SetInputAsHandled(); }
			else if (click.ButtonIndex == MouseButton.Left && _snapshot!.Control.Actions.FirstOrDefault(a => a.Id.StartsWith("cancel-popup:")) is { } popupCancel &&
				!_snapshot.Control.Actions.Any(a => a.Enabled && a.Kind == "native" && Rectangle(a.Rect).HasPoint(click.Position / _page.Scale)))
			{ SubmitControl(popupCancel.Id); _viewport.SetInputAsHandled(); }
		}
		if (_dragCard == null) return;
		if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } || input is InputEventKey { Keycode: Key.Escape, Pressed: true })
		{ CancelControlDrag(); _viewport.SetInputAsHandled(); }
		else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release)
		{
			var point = release.Position; var action = _dragAction!; string context = _dragContext;
			var target = _snapshot!.Control.Targets.Where(t => action.TargetIds.Contains(t.Id) && Rectangle(t.Rect).HasPoint(point / _page.Scale))
				.OrderBy(t => Rectangle(t.Rect).GetCenter().DistanceSquaredTo(point / _page.Scale)).FirstOrDefault();
			bool inside = new Rect2(Vector2.Zero, new Vector2(1920, 1080)).HasPoint(point);
			bool play = inside && point.DistanceTo(_dragStart) >= 20 && (action.RequiresTarget ? target != null : point.Y < 1080 * 0.8f);
			CancelControlDrag(); _viewport.SetInputAsHandled();
			if (play) SubmitControl(action.Id, target?.Id ?? "", context);
		}
	}
	private void CancelControlDrag()
	{
		_dragHeadTween?.Kill(); _dragHeadTween = null;
		if (_dragVisual != null) { _dragVisual.GetParent()?.RemoveChild(_dragVisual); _dragVisual.QueueFree(); }
		if (_dragArrow != null) { _dragArrow.GetParent()?.RemoveChild(_dragArrow); _dragArrow.QueueFree(); }
		_dragCard = null; _dragAction = null; _dragVisual = null; _dragArrow = null;
		_dragHead = null; _dragSegments.Clear(); _dragTarget = "";
	}
	private void UpdateDragArrow(Vector2 point)
    {
        if (_dragArrow == null || _dragHead == null) return;
        _dragArrow.Visible = _dragAction?.RequiresTarget == true;
        if (!_dragArrow.Visible) return;
        var target = _snapshot!.Control.Targets.Where(t => _dragAction!.TargetIds.Contains(t.Id) && Rectangle(t.Rect).HasPoint(point / _page.Scale))
            .OrderBy(t => Rectangle(t.Rect).GetCenter().DistanceSquaredTo(point / _page.Scale)).FirstOrDefault();
        string id = target?.Id ?? "";
        if (_dragTarget != id)
        {
            _dragTarget = id;
            _dragArrow.Modulate = target == null ? Colors.White : target.Enemy ? MegaCrit.Sts2.Core.Helpers.StsColors.targetingArrowEnemy : MegaCrit.Sts2.Core.Helpers.StsColors.targetingArrowAlly;
            _dragHeadTween?.Kill();
            if (target == null) _dragHead.Scale = Vector2.One * 0.95f;
            else { _dragHeadTween = _dragHead.CreateTween(); _dragHeadTween.TweenProperty(_dragHead, "scale", Vector2.One * 1.05f, 1).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Elastic); }
        }
        _dragHead.Position = point + new Vector2(0, 88).Rotated(_dragHead.Rotation);
        var final = point + new Vector2(0, 40).Rotated(_dragHead.Rotation);
        var bend = new Vector2(_dragStart.X - (_dragHead.Position.X - _dragStart.X) * 0.25f,
            _dragStart.Y > 540 ? _dragHead.Position.Y + (_dragHead.Position.Y - _dragStart.Y) * 0.5f : _dragHead.Position.Y * 0.75f + _dragStart.Y * 0.25f);
        _dragHead.Rotation = (point - bend).Angle() + Mathf.Pi / 2;
        for (int i = 0; i < 19; i++)
        {
            var segment = _dragSegments[i]; segment.Scale = Vector2.One * Mathf.Lerp(0.28f, 0.42f, i * 2f / 19);
            segment.Position = MegaCrit.Sts2.Core.Helpers.MathHelper.BezierCurve(_dragStart, final, bend, i / 20f);
            if (i > 0) segment.Rotation = (segment.Position - _dragSegments[i - 1].Position).Angle() + Mathf.Pi / 2;
        }
        _dragSegments[0].Rotation = (_dragSegments[0].Position - _dragSegments[1].Position).Angle() - Mathf.Pi / 2;
    }
	private void ProcessControl()
	{
		if (_controlLayer == null) return;
		ProcessCardHover(_viewerPointer ?? _viewport.GetMousePosition());
		ProcessScreenHover(_viewerPointer ?? _viewport.GetMousePosition());
		bool escape = Input.IsKeyPressed(Key.Escape);
		bool outside = Input.IsMouseButtonPressed(MouseButton.Left) && !_panel.GetGlobalRect().HasPoint(_panel.GetViewport().GetMousePosition());
		bool inside = _panel.GetGlobalRect().HasPoint(_panel.GetViewport().GetMousePosition());
		LiveSharingController.RouteMapInput(inside);
		if (!inside && _mapStroke) EndMapStroke();
		if (_controlEnabled && outside && !_outsideLeftDown) CancelControlDrag();
		_outsideLeftDown = outside; _escapeDown = escape;
		_controlLayer.Visible = CanControlPage;
		if (_dragVisual != null)
		{
			var point = _viewerPointer ?? _viewport.GetMousePosition(); _dragVisual.Position = point;
			UpdateDragArrow(point);
		}
		if (_pendingControlContext.Length > 0)
		{
			_pendingControlTime += _panel.GetProcessDeltaTime();
			if (_pendingControlTime > 5) { _pendingControlContext = ""; _controlMessage = T("尚未确认结果，请检查游戏状态", "Result not confirmed; check game state"); }
		}
		RefreshStatus();
	}
	private void RefreshStatus()
	{
		string text = _captureError.Length > 0 ? T("状态暂不可用：", "State unavailable: ") + _captureError
			: _controlEnabled ? T("本机控制 · 拖牌出牌 · 点击决策 · 地图右键绘画", "Local control · drag to play · click to decide · right-drag to draw on map") + (_controlMessage.Length > 0 ? " · " + _controlMessage : "")
			: T("单人本地测试 · 悬停放大卡牌 · 在游戏窗口按观战快捷键开关", "Local test · hover to enlarge cards · toggle the hotkey in the game window");
		if (_status.Text != text) _status.Text = text;
	}
}
