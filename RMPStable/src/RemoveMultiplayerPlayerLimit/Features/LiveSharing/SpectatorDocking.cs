using System;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	// Coordinates always describe the fully visible panel; animation positions
	// must never enter the profile file or become the next drag's starting point.
	private Vector2 _expandedPosition;
	private int _dockEdge; // 0 floating, 1 left, 2 right, 3 top, 4 bottom
	private bool _pinned, _dockHidden, _panelDragging, _panelResizing;
	private double _dockLeaveTime;
	private Tween? _dockTween;
	private Button _pin = null!;
	private const float DockStrip = 10, DockDistance = 18;

	private Button CreatePinButton()
	{
		var button = new Button { Name = "SpectatorPin", Flat = true, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop, Size = new Vector2(36, 36) };
		foreach (var style in new[] { "normal", "hover", "pressed", "focus" }) button.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
		// A compact conventional pushpin silhouette, independent of font glyphs.
		var icon = new Node2D { Name = "Icon", Position = new Vector2(18, 18) };
		button.AddChild(icon);
		icon.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-7, -11), new Vector2(7, -11), new Vector2(7, -7), new Vector2(4, -7), new Vector2(4, 1), new Vector2(9, 5), new Vector2(-9, 5), new Vector2(-4, 1), new Vector2(-4, -7), new Vector2(-7, -7) } });
		icon.AddChild(new Line2D { Points = new[] { new Vector2(0, 4), new Vector2(0, 14) }, Width = 2.5f, Antialiased = true });
		void Refresh()
		{
			icon.Rotation = _pinned ? 0 : -Mathf.Pi / 4;
			icon.Modulate = new Color(_pinned ? "efc851" : "b7c7ce");
			button.TooltipText = _pinned ? T("取消固定：移开鼠标后自动收纳", "Unpin: hide at edge when pointer leaves") : T("固定实况：保持窗口展开", "Pin spectator: keep expanded");
		}
		button.Toggled += value =>
		{
			_pinned = value; SpectatorPreferences.Current.Pinned = value; Refresh();
			_dockLeaveTime = 0; SlidePanel(false); RememberPanelLayout();
		};
		button.SetPressedNoSignal(_pinned); Refresh();
		return button;
	}
	private void RememberPanelLayout()
	{
		SpectatorPreferences.Current.DockEdge = _dockEdge;
		SpectatorPreferences.Current.Pinned = _pinned;
		SpectatorPreferences.RememberLayout(_expandedPosition, _panel.Size.X);
		SpectatorPreferences.Save();
	}
	private Vector2 DockPosition(bool hidden)
	{
		var bounds = _panel.GetViewport().GetVisibleRect().Size;
		var position = _expandedPosition;
		return _dockEdge switch
		{
			1 => new Vector2(hidden ? DockStrip - _panel.Size.X : 0, position.Y),
			2 => new Vector2(hidden ? bounds.X - DockStrip : bounds.X - _panel.Size.X, position.Y),
			3 => new Vector2(position.X, hidden ? DockStrip - _panel.Size.Y : 0),
			4 => new Vector2(position.X, hidden ? bounds.Y - DockStrip : bounds.Y - _panel.Size.Y),
			_ => position
		};
	}
	private void SlidePanel(bool hidden, bool immediate = false)
	{
		hidden &= _dockEdge != 0 && !_pinned;
		var target = DockPosition(hidden);
		if (_dockHidden == hidden && !immediate && _panel.Position.DistanceTo(target) < 0.1f) return;
		_dockHidden = hidden; _dockTween?.Kill(); _dockTween = null;
		if (immediate) { _panel.Position = target; return; }
		_dockTween = _panel.CreateTween();
		_dockTween.TweenProperty(_panel, "position", target, 0.24).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
	}
	private void FinishPanelDrag()
	{
		var bounds = _panel.GetViewport().GetVisibleRect().Size;
		float[] distances = { _panel.Position.X, bounds.X - _panel.Position.X - _panel.Size.X, _panel.Position.Y, bounds.Y - _panel.Position.Y - _panel.Size.Y };
		_dockEdge = 0; float closest = DockDistance;
		for (int i = 0; i < distances.Length; i++) if (distances[i] < closest) { closest = distances[i]; _dockEdge = i + 1; }
		_expandedPosition = _panel.Position; _expandedPosition = DockPosition(false);
		_panel.Position = _expandedPosition; _dockLeaveTime = 0; RememberPanelLayout();
	}
	private void ProcessDocking()
	{
		ProcessPointerMotion();
		ProcessControl();
		if (_dockEdge == 0 || _pinned || _panelDragging || _panelResizing || _dragCard != null || _pendingControlContext.Length > 0) return;
		var viewport = _panel.GetViewport();
		var pointer = viewport.GetMousePosition();
		bool inside = viewport.GetVisibleRect().HasPoint(pointer) && _panel.GetGlobalRect().HasPoint(pointer);
		if (inside)
		{
			_dockLeaveTime = 0;
			if (_dockHidden) SlidePanel(false);
		}
		else if (!_dockHidden)
		{
			_dockLeaveTime += _panel.GetProcessDeltaTime();
			if (_dockLeaveTime >= 0.45) SlidePanel(true);
		}
	}
}
