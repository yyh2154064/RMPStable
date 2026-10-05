using System;
using System.Linq;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private (CanvasLayer Overlay, Control Panel, SubViewport Viewport) CreatePanel(Action close)
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
			var titlebar = new Control { Name = "SpectatorTitlebar", MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move };
			panel.AddChild(titlebar);
			var caption = Text(titlebar, T("实况观战 · 单人本地测试 · 拖动标题栏移动", "Live spectator · local test · drag title to move"), new Rect2(16, 3, 900, 38), 23);
			var dismiss = new Button { Name = "SpectatorClose", Text = "×", FocusMode = Control.FocusModeEnum.None, Size = new Vector2(48, 38) };
			panel.AddChild(dismiss); dismiss.Pressed += close;
			var container = new SubViewportContainer { Name = "SpectatorSurface", Position = new Vector2(8, 44), Size = new Vector2(1920, 1080), MouseFilter = Control.MouseFilterEnum.Stop };
			panel.AddChild(container);
			var viewport = new SubViewport { Name = "SpectatorViewport", Size = new Vector2I(1920, 1080), Disable3D = true, HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
			container.AddChild(viewport);
			// The surface consumes pointer events throughout the read-only view,
			// including empty spaces, so they cannot click the game underneath it.
			var resize = new Control { Name = "SpectatorResize", Size = new Vector2(30, 30), MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Fdiagsize };
			panel.AddChild(resize); Text(resize, "◢", new Rect2(0, 0, 30, 30), 24);
			Vector2 Bounds() => root.GetVisibleRect().Size;
			void Layout(float width)
			{
				var bounds = Bounds();
				float maximum = Math.Max(100, Math.Min(bounds.X - 16, (bounds.Y - 60) * 16 / 9 + 16));
				width = Math.Clamp(width, Math.Min(640, maximum), maximum);
				panel.Size = new Vector2(width, (width - 16) * 9 / 16 + 52);
				container.Scale = Vector2.One * ((width - 16) / 1920);
				titlebar.Size = new Vector2(width - 64, 44); caption.Size = new Vector2(width - 88, 38); dismiss.Position = new Vector2(width - 56, 3); resize.Position = panel.Size - resize.Size;
				panel.Position = new Vector2(Math.Clamp(panel.Position.X, 0, Math.Max(0, bounds.X - panel.Size.X)), Math.Clamp(panel.Position.Y, 0, Math.Max(0, bounds.Y - panel.Size.Y)));
			}
			Layout(Bounds().X * 0.65f * 3 / 5); panel.Position = (Bounds() - panel.Size) / 2;
			bool dragging = false, resizing = false;
			Vector2 dragStart = default, initialPosition = default; float initialWidth = 0;
			titlebar.GuiInput += input =>
			{
				if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click) { dragging = click.Pressed; dragStart = click.GlobalPosition; initialPosition = panel.Position; titlebar.AcceptEvent(); }
				else if (input is InputEventMouseMotion motion && dragging) { panel.Position = initialPosition + motion.GlobalPosition - dragStart; Layout(panel.Size.X); titlebar.AcceptEvent(); }
			};
			resize.GuiInput += input =>
			{
				if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click) { resizing = click.Pressed; dragStart = click.GlobalPosition; initialWidth = panel.Size.X; resize.AcceptEvent(); }
				else if (input is InputEventMouseMotion motion && resizing) { Layout(initialWidth + motion.GlobalPosition.X - dragStart.X); resize.AcceptEvent(); }
			};
			void Resized() => Layout(panel.Size.X);
			root.SizeChanged += Resized; overlay.TreeExiting += () => root.SizeChanged -= Resized;
			return (overlay, panel, viewport);
		}
		catch { overlay.QueueFree(); throw; }
	}
}
