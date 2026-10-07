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
	private Line2D? _dragArrow;
	private Vector2 _dragStart;
	private readonly List<Panel> _targetHighlights = new();
	private bool _outsideLeftDown, _escapeDown;
	private string _captureError = "";
	private readonly Dictionary<string, Button> _actionButtons = new();

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
		var button = new Button { Name = "SpectatorControlToggle", Text = T("观战", "Watch"), ToggleMode = true, FocusMode = Control.FocusModeEnum.None, Size = new Vector2(60, 36), TooltipText = T("切换本机控制：拖动出牌、点击选牌；右键查看详情", "Local control: drag to play, click to choose; right-click to inspect") };
		button.AddThemeFontSizeOverride("font_size", 16);
		button.Toggled += SetControlEnabled;
		return button;
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
		if (_controlEnabled && input is InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false })
		{
			_escapeDown = true;
			if (_dragCard != null) CancelControlDrag();
			else if (CanControlPage && _snapshot!.Control.Actions.FirstOrDefault(a => a.Kind == "cancel" && a.Enabled) is { } cancel) SubmitControl(cancel.Id);
			else SetControlEnabled(false);
			_overlay.GetViewport().SetInputAsHandled(); return;
		}
		if (!_controlEnabled || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button || _panel.GetGlobalRect().HasPoint(button.Position)) return;
		if (button.Pressed && _dragCard == null) SetControlEnabled(false);
		else if (!button.Pressed && _dragCard != null) CancelControlDrag();
	}
	private void SetControlEnabled(bool enabled)
	{
		enabled &= _setControl != null && _executeCommand != null && _snapshot != null;
		if (_controlEnabled == enabled) { _controlToggle.SetPressedNoSignal(enabled); return; }
		_controlEnabled = enabled; _controlEpoch = _setControl?.Invoke(enabled) ?? 0;
		_controlToggle.SetPressedNoSignal(enabled); _controlToggle.Text = enabled ? T("控制", "Control") : T("观战", "Watch");
		_controlToggle.Modulate = enabled ? new Color("efc851") : Colors.White;
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
		foreach (var action in snapshot.Control.Actions.Where(a => a.CardId.Length == 0 && a.Kind != "scroll"))
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
			button.Disabled = !action.Enabled; button.TooltipText = action.Label;
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
		if (result.Accepted)
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
		_dragCard = card; _dragAction = action; _dragContext = _snapshot.Control.Context; _dragStart = _viewport.GetMousePosition();
		_dragVisual = new Node2D { Position = _dragStart, ZIndex = 10 }; _controlLayer.AddChild(_dragVisual);
		DrawCard(_dragVisual, card, Vector2.Zero, 0.8f, centered: true);
		_dragArrow = new Line2D { Width = 5, DefaultColor = new Color("efc851"), Antialiased = true, ZIndex = 11 }; _controlLayer.AddChild(_dragArrow);
		foreach (var target in _snapshot.Control.Targets.Where(t => action.TargetIds.Contains(t.Id)))
		{
			var rect = Rectangle(target.Rect); var highlight = new Panel { Position = rect.Position * _page.Scale, Size = rect.Size * _page.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
			highlight.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(1, 0.8f, 0.2f, 0.08f), BorderColor = new Color("efc851"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 });
			_controlLayer.AddChild(highlight); _targetHighlights.Add(highlight);
		}
		return true;
	}
	private void ControlInputEvent(InputEvent input)
	{
		if (CanControlPage && _dragCard == null && input is InputEventMouseButton { Pressed: true } click)
		{
			if (click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
			{
				var scroll = _snapshot!.Control.Actions.LastOrDefault(a => a.Kind == "scroll" && a.Enabled && Rectangle(a.Rect).HasPoint(click.Position / _page.Scale));
				if (scroll != null) { SubmitControl(scroll.Id, click.ButtonIndex == MouseButton.WheelUp ? "up" : "down"); _viewport.SetInputAsHandled(); }
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
		if (_dragVisual != null) { _dragVisual.GetParent()?.RemoveChild(_dragVisual); _dragVisual.QueueFree(); }
		if (_dragArrow != null) { _dragArrow.GetParent()?.RemoveChild(_dragArrow); _dragArrow.QueueFree(); }
		foreach (var panel in _targetHighlights) { panel.GetParent()?.RemoveChild(panel); panel.QueueFree(); }
		_targetHighlights.Clear(); _dragCard = null; _dragAction = null; _dragVisual = null; _dragArrow = null;
	}
	private void ProcessControl()
	{
		if (_controlLayer == null) return;
		bool escape = Input.IsKeyPressed(Key.Escape);
		bool outside = Input.IsMouseButtonPressed(MouseButton.Left) && !_panel.GetGlobalRect().HasPoint(_panel.GetViewport().GetMousePosition());
		if (_controlEnabled && ((outside && !_outsideLeftDown && _dragCard == null) || (escape && !_escapeDown && _dragCard == null) || MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance?.InCardPlay == true)) SetControlEnabled(false);
		_outsideLeftDown = outside; _escapeDown = escape;
		_controlLayer.Visible = CanControlPage;
		if (_dragVisual != null)
		{
			var point = _viewport.GetMousePosition(); _dragVisual.Position = point;
			_dragArrow!.Points = new[] { _dragStart, point };
			if (_dragAction?.RequiresTarget != true) _dragArrow.Hide();
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
			: _controlEnabled ? T("本机控制 · 拖牌出牌 · 点击决策 · 右键查看 · Esc 退出", "Local control · drag to play · click to decide · right-click to inspect · Esc exits") + (_controlMessage.Length > 0 ? " · " + _controlMessage : "")
			: T("单人本地测试 · 悬停放大卡牌 · 在游戏窗口按观战快捷键开关", "Local test · hover to enlarge cards · toggle the hotkey in the game window");
		if (_status.Text != text) _status.Text = text;
	}
}
