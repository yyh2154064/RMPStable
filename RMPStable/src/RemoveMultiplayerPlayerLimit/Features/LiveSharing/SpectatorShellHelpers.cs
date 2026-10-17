using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;
internal sealed partial class SpectatorView {
	private Control? DecorativeScene(string path, Node parent, Vector2 size)
	{
		var packed = Asset<PackedScene>(path);
		if (packed == null) return null;
		var node = packed.Instantiate<Control>();
		ulong rootId = node.GetInstanceId();
		var nodes = LocalNodes(node).ToList();
		foreach (var oldWrapper in nodes.AsEnumerable().Reverse())
		{
			if (oldWrapper is CanvasItem decoration && (oldWrapper.Name.ToString().Contains("SelectionReticle") || oldWrapper.GetType().Name == "NHotkeyIcon")) decoration.Visible = false;
			if (oldWrapper is MegaLabel or MegaRichTextLabel) continue;
			ulong id = oldWrapper.GetInstanceId();
			if (oldWrapper.GetScript().VariantType != Variant.Type.Nil) oldWrapper.SetScript(default);
			// Removing a C# script disposes its managed wrapper. Resolve the
			// native node again rather than calling through the disposed object.
			var child = (Node)GodotObject.InstanceFromId(id);
			child.SetProcess(false); child.SetProcessInput(false); child.SetProcessUnhandledInput(false);
			if (child is Control c) { c.MouseFilter = Control.MouseFilterEnum.Ignore; c.FocusMode = Control.FocusModeEnum.None; }
		}
		node = (Control)GodotObject.InstanceFromId(rootId);
		parent.AddChild(node); node.Size = size; node.Position = Vector2.Zero; node.Visible = true;
		return node;
	}
	private static IEnumerable<Node> LocalNodes(Node node)
	{ yield return node; foreach (Node child in node.GetChildren()) foreach (var nested in LocalNodes(child)) yield return nested; }

	private static Label Text(Node parent, string text, Rect2 rect, int size, HorizontalAlignment align = HorizontalAlignment.Left)
	{
		var label = new Label { Text = text, Position = rect.Position, Size = rect.Size, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true };
		label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", new Color("f4e7d0"));
		label.AddThemeColorOverride("font_outline_color", Colors.Black); label.AddThemeConstantOverride("outline_size", 4);
		parent.AddChild(label); return label;
	}

	private static string T(string zh, string en) => LocalSpectatorSource.T(zh, en);
	private Button CreateControlToggle()
	{
        var button = new Button { Name = "SpectatorControlToggle", FocusMode = Control.FocusModeEnum.None, Size = new Vector2(116, 28), TooltipText = T("观战 / 控制（记住选择）", "Watch / Control (remember selection)") };
		button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("152126"), CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16 });
		_modeThumb = new Panel { Position = new Vector2(2, 2), Size = new Vector2(56, 24), MouseFilter = Control.MouseFilterEnum.Ignore };
		_modeThumb.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("42696f"), CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14 });
		button.AddChild(_modeThumb);
		Text(button, T("观战", "Watch"), new Rect2(2, 2, 56, 24), 14, HorizontalAlignment.Center);
		Text(button, T("控制", "Control"), new Rect2(58, 2, 56, 24), 14, HorizontalAlignment.Center);
        // Authority updates arrive between mouse-down and mouse-up. Derive the
        // next request from the session, without rewriting a toggle mid-click.
        button.Pressed += () => SetPreferredControl(!(_mirror.Multiplayer ? SpectatorPreferences.Current.MultiplayerControlMode : _controlEnabled));
		return button;
	}
	private void SetPreferredControl(bool enabled)
	{
        if (_mirror.Multiplayer) { SpectatorPreferences.Current.MultiplayerControlMode = enabled; SpectatorPreferences.Save(); SetControlEnabled(enabled); return; }
		SpectatorPreferences.Current.ControlMode = enabled; SpectatorPreferences.Save(); SetControlEnabled(enabled);
	}

	private static void StyleToolbarButton(Button button, CanvasItem icon, Func<Color> tint)
	{
		button.Flat = true; button.FocusMode = Control.FocusModeEnum.None;
		button.MouseFilter = Control.MouseFilterEnum.Stop;
		button.Size = button.CustomMinimumSize = new Vector2(36, 36);
		foreach (var style in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
			button.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
		bool hovered = false, pressed = false;
		void Refresh()
		{
			var color = tint(); icon.Modulate = pressed ? color.Darkened(.18f) : hovered ? color.Lightened(.12f) : color;
			if (icon is Node2D drawing) drawing.Scale = Vector2.One * (pressed ? .95f : hovered ? 1.05f : 1);
			if (icon is Control control) control.Scale = Vector2.One * (pressed ? .95f : hovered ? 1.05f : 1);
		}
		button.MouseEntered += () => { hovered = true; Refresh(); };
		button.MouseExited += () => { hovered = false; pressed = false; Refresh(); };
		button.ButtonDown += () => { pressed = true; Refresh(); };
		button.ButtonUp += () => { pressed = false; Refresh(); };
		button.Toggled += _ => Refresh(); Refresh();
	}
	private Button CreateRefreshButton()
	{
		var button = new Button { Name = "RebuildMirror" };
		using var image = new Image();
		var error = image.LoadSvgFromString("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\" viewBox=\"0 0 24 24\"><path fill=\"none\" stroke=\"white\" stroke-width=\"3\" stroke-linecap=\"round\" stroke-linejoin=\"round\" d=\"M20 8 A8.5 8.5 0 1 0 20.3 14 M20 3 V8 H15\"/></svg>");
		if (error != Error.Ok) throw new InvalidOperationException("Reset icon: " + error);
		image.GenerateMipmaps();
		var icon = new TextureRect { Name = "Icon", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Texture = ImageTexture.CreateFromImage(image),
			Position = new Vector2(6,6), Size = new Vector2(24,24), PivotOffset = new Vector2(12,12), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps, MouseFilter = Control.MouseFilterEnum.Ignore };
		button.AddChild(icon);
		StyleToolbarButton(button, icon, () => new Color("b7c7ce"));
		button.Pressed += LiveSharingController.RecoverMirror; return button;
	}

}
