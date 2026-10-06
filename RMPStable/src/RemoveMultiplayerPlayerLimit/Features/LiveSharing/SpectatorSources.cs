using System;
using System.Collections.Generic;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private readonly Action<string>? _selectSource;
	private Control _sourceSelector = null!;
	private Button _sourcePrevious = null!, _sourceNext = null!;
	private TextureRect _sourcePreviousIcon = null!, _sourceNextIcon = null!;
	private readonly List<(Button Button, TextureRect Background, Label Label)> _sourceButtons = new();
	private List<ParticipantSnapshot> _sourceOptions = new();
	private string _selectedSourceId = "";
	private int _sourcePageIndex;

	private ShaderMaterial? SourceMaterial(float saturation = 1, float value = 0.9f)
	{
		if (Asset<Shader>("res://shaders/hsv.gdshader") is not { } shader) return null;
		var material = new ShaderMaterial { Shader = shader };
		material.SetShaderParameter("h", 1f); material.SetShaderParameter("s", saturation); material.SetShaderParameter("v", value);
		return material;
	}
	private void CreateSourceSelector(Control titlebar)
	{
		_sourceSelector = new Control { Name = "SpectatorSourceSelector", Position = new Vector2(8, 7), Size = new Vector2(600, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
		titlebar.AddChild(_sourceSelector);
		// Reuse the exact inspect-card arrow textures, stripping its gameplay
		// scripts before instantiation into the tree, as for the card detail UI.
		var template = DecorativeScene("res://scenes/screens/inspect_card_screen.tscn", _sourceSelector, new Vector2(1920, 1080));
		TextureRect? Arrow(bool left) => template?.GetNodeOrNull<TextureRect>((left ? "LeftArrow" : "RightArrow") + "/TextureRect");
		(Button Button, TextureRect Icon) MakeArrow(bool left)
		{
			var native = Arrow(left);
			var button = new Button { Name = left ? "SpectatorSourcePrevious" : "SpectatorSourceNext", Flat = true, FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop, Size = new Vector2(24, 26), TooltipText = left ? T("上一页玩家", "Previous players") : T("下一页玩家", "Next players") };
			_sourceSelector.AddChild(button);
			var icon = new TextureRect { Name = "Icon", Texture = native?.Texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore, Material = SourceMaterial(), FlipH = native != null && (native.FlipH ^ (native.GetGlobalTransform().X.X < 0)), FlipV = native?.FlipV ?? false };
			button.AddChild(icon); icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			if (icon.Texture == null) { var label = Text(button, left ? "◀" : "▶", new Rect2(0, 0, 24, 26), 20, HorizontalAlignment.Center); label.AddThemeColorOverride("font_color", new Color("efc851")); }
			button.Pressed += () => NavigateSources(left ? -1 : 1);
			return (button, icon);
		}
		(_sourcePrevious, _sourcePreviousIcon) = MakeArrow(true);
		(_sourceNext, _sourceNextIcon) = MakeArrow(false);
		if (template != null) { _sourceSelector.RemoveChild(template); template.QueueFree(); }
		for (int i = 0; i < 3; i++)
		{
			int slot = i;
			var button = new Button { Name = "SpectatorSource" + i, Flat = true, FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
			_sourceSelector.AddChild(button);
			// Native loot/gold reward frame, without its purchase/reward callbacks.
			var background = new TextureRect { Name = "Background", Texture = Asset<Texture2D>("res://images/ui/reward_screen/reward_item_button.png"), SelfModulate = new Color(0.72f, 0.46f, 0.54f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Material = SourceMaterial(), MouseFilter = Control.MouseFilterEnum.Ignore };
			button.AddChild(background); background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			var border = new Panel { Name = "FrameBorder", MouseFilter = Control.MouseFilterEnum.Ignore };
			border.AddThemeStyleboxOverride("panel", new StyleBoxFlat { DrawCenter = false, BorderColor = new Color("142a35"), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
			button.AddChild(border); border.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			var label = Text(button, "", new Rect2(8, 0, 134, 26), 16, HorizontalAlignment.Center);
			label.AddThemeColorOverride("font_color", Colors.White); label.AddThemeConstantOverride("outline_size", 2);
			label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			_sourceButtons.Add((button, background, label));
			button.Pressed += () => SelectSource(slot);
			button.MouseEntered += () => SetSourceBrightness(background, 1.08f);
			button.MouseExited += () => RefreshSources();
		}
		RefreshSources();
	}
	private static void SetSourceBrightness(TextureRect image, float value)
	{ if (image.Material is ShaderMaterial material) material.SetShaderParameter("v", value); }
	private void LayoutSources(float width)
	{
		_sourceSelector.Size = new Vector2(Math.Max(80, width), 26);
		// Reserve at least 96 logical pixels for dragging even with three slots.
		float buttonWidth = Math.Clamp((width - 96 - 24 * 2 - 8 * 4) / 3, 0, 150);
		_sourcePrevious.Position = Vector2.Zero;
		int count = Math.Clamp(_sourceOptions.Count - _sourcePageIndex * 3, 0, 3);
		for (int i = 0; i < _sourceButtons.Count; i++)
		{
			var slot = _sourceButtons[i]; slot.Button.Position = new Vector2(32 + i * (buttonWidth + 8), 0); slot.Button.Size = new Vector2(buttonWidth, 26); slot.Label.Size = new Vector2(Math.Max(0, buttonWidth - 16), 26);
		}
		_sourceNext.Position = new Vector2(32 + count * (buttonWidth + 8), 0);
	}
	internal void UpdateSources(List<ParticipantSnapshot> sources, string currentId)
	{
		if (currentId == _selectedSourceId && SnapshotEquality.List(_sourceOptions, sources, SnapshotEquality.Equal)) return;
		bool changedSource = _selectedSourceId != currentId;
		_sourceOptions = sources; _selectedSourceId = currentId;
		if (changedSource) { int index = sources.FindIndex(p => p.Id == currentId); if (index >= 0) _sourcePageIndex = index / 3; }
		_sourcePageIndex = Math.Clamp(_sourcePageIndex, 0, Math.Max(0, (sources.Count - 1) / 3));
		RefreshSources();
	}
	private void NavigateSources(int direction)
	{
		int next = _sourcePageIndex + direction;
		if (next < 0 || next * 3 >= _sourceOptions.Count) return;
		_sourcePageIndex = next; RefreshSources();
	}
	private void SelectSource(int slot)
	{
		int index = _sourcePageIndex * 3 + slot;
		if (index >= _sourceOptions.Count || _sourceOptions[index].Id == _selectedSourceId) return;
		SetControlEnabled(false);
		// A future transport/provider supplies the new source snapshot. Selection
		// is confirmed by its SourceId, never by relabeling the previous picture.
		_selectSource?.Invoke(_sourceOptions[index].Id);
	}
	private void RefreshSources()
	{
		_sourcePrevious.Disabled = _sourcePageIndex == 0;
		_sourceNext.Disabled = (_sourcePageIndex + 1) * 3 >= _sourceOptions.Count;
		void ArrowState(Button button, TextureRect image)
		{
			if (image.Material is ShaderMaterial material) { material.SetShaderParameter("s", button.Disabled ? 0f : 1f); material.SetShaderParameter("v", button.Disabled ? 0.55f : 0.9f); }
		}
		ArrowState(_sourcePrevious, _sourcePreviousIcon); ArrowState(_sourceNext, _sourceNextIcon);
		for (int i = 0; i < _sourceButtons.Count; i++)
		{
			int index = _sourcePageIndex * 3 + i; var slot = _sourceButtons[i]; slot.Button.Visible = index < _sourceOptions.Count;
			if (!slot.Button.Visible) continue;
			var participant = _sourceOptions[index]; slot.Label.Text = string.IsNullOrEmpty(participant.Name) ? participant.Id : participant.Name;
			slot.Button.TooltipText = slot.Label.Text + "\nID: " + participant.Id;
			if (participant.Simulated) slot.Button.TooltipText += "\n" + T("模拟来源 · 展示本机状态", "Preview source · displays local state");
			bool selected = participant.Id == _selectedSourceId;
			// The selected source uses the native loot/gold frame's blue unchanged.
			// Other sources keep the darker HUD-blue tint and deeper outer border.
			slot.Background.SelfModulate = selected ? Colors.White : new Color(0.72f, 0.46f, 0.54f);
			SetSourceBrightness(slot.Background, selected ? 1f : 0.95f);
		}
		LayoutSources(_sourceSelector.Size.X);
	}
}
