using System;
using System.Linq;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private (CanvasLayer Overlay, Control Panel, Control Content) CreatePanel(Action close)
	{
		var root = ((SceneTree)Engine.GetMainLoop()).Root;
		var overlay = new CanvasLayer { Name = "RmpLocalSpectator", Layer = 90 };
		root.AddChild(overlay);
		try
		{
			var panel = new Panel { Name = "SpectatorPanel", MouseFilter = Control.MouseFilterEnum.Stop };
			overlay.AddChild(panel);
			panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("202c31"), BorderColor = new Color("a79c78"), BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 });
			// Borrow the F5 popup's decorative frame without registering a modal,
			// pausing input outside this panel, or running the popup's game scripts.
			var template = DecorativeScene("res://scenes/ui/generic_popup.tscn", panel, new Vector2(1920, 1080));
			if (template != null)
			{
				var native = LocalNodes(template).OfType<NinePatchRect>().FirstOrDefault(n => n.Texture != null);
				if (native != null)
				{
					var frame = new NinePatchRect { Texture = native.Texture, PatchMarginLeft = native.PatchMarginLeft, PatchMarginRight = native.PatchMarginRight, PatchMarginTop = native.PatchMarginTop, PatchMarginBottom = native.PatchMarginBottom, MouseFilter = Control.MouseFilterEnum.Ignore };
					panel.AddChild(frame); frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
				}
				panel.RemoveChild(template); template.QueueFree();
			}
			const float inset = 3, titleHeight = 40;
			var titleFill = new ColorRect { Name = "SpectatorTitleFill", Position = new Vector2(inset, inset), Color = new Color("202c31"), MouseFilter = Control.MouseFilterEnum.Ignore };
			panel.AddChild(titleFill);
			var titlebar = new Control { Name = "SpectatorTitlebar", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move };
			panel.AddChild(titlebar);
			titlebar.TooltipText = T("拖动空白区域移动实况面板", "Drag empty space to move the spectator panel");
			CreateSourceSelector(titlebar);
			var dismiss = CreateCloseButton(close);
			panel.AddChild(dismiss);
			_pinned = SpectatorPreferences.Current.Pinned;
			_pin = CreatePinButton(); panel.AddChild(_pin);
			_controlToggle = CreateControlToggle(); panel.AddChild(_controlToggle);
			_refresh = CreateRefreshButton(); panel.AddChild(_refresh);
			// Godot tooltips below the title are occluded by the native child HWND.
			var refreshHint = new Label { Name = "RebuildMirrorHint", Text = T("重新同步 · 保留本体对局", "Resync · keep source run"),
				Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			refreshHint.AddThemeFontSizeOverride("font_size", 16);
			refreshHint.AddThemeColorOverride("font_shadow_color", Colors.Black); refreshHint.AddThemeConstantOverride("shadow_outline_size", 5);
			panel.AddChild(refreshHint);
			void HintPosition()
			{
				refreshHint.Size = new Vector2(Math.Min(panel.Size.X, 300), 28);
				refreshHint.Position = new Vector2(Math.Clamp(_refresh.Position.X - 130, 0, Math.Max(0, panel.Size.X - refreshHint.Size.X)), panel.Position.Y >= 30 ? -30 : 5);
				// At the top screen edge, use empty title space to the left of controls.
				if (panel.Position.Y < 30) refreshHint.Position = new Vector2(Math.Max(0, _refresh.Position.X - refreshHint.Size.X), 5);
			}
			_refresh.MouseEntered += () => { HintPosition(); refreshHint.Show(); };
			_refresh.MouseExited += refreshHint.Hide;
			// Clip at the displayed content rectangle, not at the unscaled 1920x1080
			// surface. A single layout calculation owns the frame and picture edges.
			var content = new Control { Name = "SpectatorContent", Position = new Vector2(inset, titleHeight), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Stop };
			panel.AddChild(content);
			// A native child HWND occupies this rectangle; no screenshot surface.
			// The surface consumes pointer events throughout the read-only view,
			// including empty spaces, so they cannot click the game underneath it.
			var resize = new Control { Name = "SpectatorResize", Size = new Vector2(16, 16), MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Fdiagsize };
			panel.AddChild(resize); Text(resize, "◢", new Rect2(0, 0, 16, 16), 14);
			Vector2 Bounds() => root.GetVisibleRect().Size;
			const float referenceWidth = 640;
			void Layout(float width)
			{
				var bounds = Bounds();
				// The navigation row shares one scale with the resized panel. Account
				// for its changing height when limiting the whole panel to the screen.
				float maximum = Math.Max(100, Math.Min(bounds.X - 16, (bounds.Y - inset - 16 + inset * 2 * 9 / 16) / (titleHeight / referenceWidth + 9f / 16)));
				width = Math.Clamp(width, Math.Min(320, maximum), maximum);
				float navigationScale = width / referenceWidth, navigationHeight = titleHeight * navigationScale;
				content.Size = new Vector2(width - inset * 2, (width - inset * 2) * 9 / 16);
				content.Position = new Vector2(inset, navigationHeight);
				panel.Size = new Vector2(width, navigationHeight + content.Size.Y + inset);
				titleFill.Size = new Vector2(content.Size.X, navigationHeight - inset);
				titlebar.Scale = dismiss.Scale = _pin.Scale = _controlToggle.Scale = _refresh.Scale = Vector2.One * navigationScale;
				titlebar.Size = new Vector2(width / navigationScale - 260, titleHeight); LayoutSources(width / navigationScale - 292);
				_controlToggle.Position = new Vector2(width - 254 * navigationScale, 6 * navigationScale);
				_refresh.Position = new Vector2(width - 132 * navigationScale, 2 * navigationScale);
				_pin.Position = new Vector2(width - 90 * navigationScale, 2 * navigationScale);
				dismiss.Position = new Vector2(width - 48 * navigationScale, 2 * navigationScale); resize.Position = panel.Size - resize.Size;
				panel.Position = new Vector2(Math.Clamp(panel.Position.X, 0, Math.Max(0, bounds.X - panel.Size.X)), Math.Clamp(panel.Position.Y, 0, Math.Max(0, bounds.Y - panel.Size.Y)));
				HintPosition();
			}
			var preferences = SpectatorPreferences.Current;
			float defaultWidth = (Bounds().Y / 3 - inset + inset * 2 * 9 / 16) / (titleHeight / referenceWidth + 9f / 16);
			Layout(preferences.HasLayout ? preferences.Width : defaultWidth);
			panel.Position = preferences.HasLayout ? new Vector2(preferences.X, preferences.Y) : new Vector2(Bounds().X - panel.Size.X, SpectatorPreferences.DefaultTop);
			Layout(panel.Size.X);
			_expandedPosition = panel.Position; _dockEdge = preferences.DockEdge;
			Vector2 dragStart = default, initialPosition = default; float initialWidth = 0;
			titlebar.GuiInput += input =>
			{
				if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
				{
					_panelDragging = click.Pressed;
					if (click.Pressed) { _dockTween?.Kill(); _dockEdge = 0; _dockHidden = false; dragStart = click.GlobalPosition; initialPosition = panel.Position; }
					else FinishPanelDrag();
					titlebar.AcceptEvent();
				}
				else if (input is InputEventMouseMotion motion && _panelDragging) { panel.Position = initialPosition + motion.GlobalPosition - dragStart; Layout(panel.Size.X); _expandedPosition = panel.Position; titlebar.AcceptEvent(); }
			};
			resize.GuiInput += input =>
			{
				if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click) { _panelResizing = click.Pressed; dragStart = click.GlobalPosition; initialWidth = panel.Size.X; _dockTween?.Kill(); _dockHidden = false; if (!click.Pressed) RememberPanelLayout(); resize.AcceptEvent(); }
				else if (input is InputEventMouseMotion motion && _panelResizing) { Layout(initialWidth + motion.GlobalPosition.X - dragStart.X); _expandedPosition = panel.Position; panel.Position = _expandedPosition = DockPosition(false); resize.AcceptEvent(); }
			};
			void Resized() { bool hidden = _dockHidden; _dockTween?.Kill(); panel.Position = _expandedPosition; Layout(panel.Size.X); _expandedPosition = panel.Position; _expandedPosition = DockPosition(false); SlidePanel(hidden, true); RememberPanelLayout(); }
			var tree = root.GetTree();
			root.SizeChanged += Resized; tree.ProcessFrame += ProcessDocking;
			overlay.TreeExiting += () => { root.SizeChanged -= Resized; tree.ProcessFrame -= ProcessDocking; _dockTween?.Kill(); };
			// _panel is assigned by the constructor after this factory returns.
			Callable.From(() => { if (GodotObject.IsInstanceValid(panel) && panel.IsInsideTree()) { _expandedPosition = DockPosition(false); SlidePanel(false, true); } }).CallDeferred();
			return (overlay, panel, content);
		}
		catch { overlay.QueueFree(); throw; }
	}
	private Button CreateCloseButton(Action close)
	{
		var button = new Button { Name = "SpectatorClose", TooltipText = T("关闭实况", "Close spectator") };
		var icon = new Node2D { Name = "Icon", Position = new Vector2(18,18) }; button.AddChild(icon);
		icon.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-8,-11),new Vector2(0,-3),new Vector2(8,-11),new Vector2(11,-8),new Vector2(3,0),new Vector2(11,8),new Vector2(8,11),new Vector2(0,3),new Vector2(-8,11),new Vector2(-11,8),new Vector2(-3,0),new Vector2(-11,-8) } });
		StyleToolbarButton(button, icon, () => new Color("e05643"));
		button.Pressed += close;
		return button;
	}
}
