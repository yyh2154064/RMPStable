using System;
using System.Collections.Generic;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private int _relicIndex = -1;
	private List<RelicSnapshot> _detailRelics = new();
	private List<HoverSnapshot>? _interactionHovers;
	private List<RelicSnapshot>? _interactionRelics;
	private List<PileSnapshot>? _interactionPiles;
	private void RetainHudInteractions(SpectatorSnapshot snapshot)
	{
		if (ReferenceEquals(_interactionHovers, snapshot.HudHovers) && ReferenceEquals(_interactionRelics, snapshot.Relics) && ReferenceEquals(_interactionPiles, snapshot.Piles)) return;
		_interactionHovers = snapshot.HudHovers; _interactionRelics = snapshot.Relics; _interactionPiles = snapshot.Piles;
		Clear(_hudHovers); Clear(_pileTargets); DrawHudInteractions(snapshot);
	}
	private void TipTarget(Control parent, float[] bounds, List<TipSnapshot> tips, Action? click = null)
	{
		var rect = Rectangle(bounds);
		var hitbox = Area(parent, rect.Position, rect.Size); hitbox.MouseFilter = Control.MouseFilterEnum.Stop;
		hitbox.MouseEntered += () =>
		{
			if (_inspectIndex >= 0 || _relicIndex >= 0) return;
			_activeScreenHover = null; _hoverCardSlot = null; Clear(_preview); var target = hitbox.GetGlobalRect();
			DrawTips(_preview, tips, new Vector2(Math.Clamp(target.Position.X, 20, 1540), Math.Clamp(target.End.Y + 20, 80, 600)));
		};
		hitbox.MouseExited += () => { if (_inspectIndex < 0 && _relicIndex < 0 && !HasCardPreview) Clear(_preview); };
		if (click != null) hitbox.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { click(); hitbox.AcceptEvent(); } };
	}
	private void DrawHudInteractions(SpectatorSnapshot snapshot)
	{
		foreach (var target in snapshot.HudHovers) TipTarget(_hudHovers, target.Rect, target.Tips);
		for (int i = 0; i < snapshot.Relics.Count; i++)
		{
			int index = i; var relic = snapshot.Relics[i];
			TipTarget(_hudHovers, relic.Rect, relic.Tips, () => { CloseInspect(); _detailRelics = snapshot.Relics; _relicIndex = index; DrawRelicInspect(); });
		}
		foreach (var pile in snapshot.Piles)
			TipTarget(_pileTargets, pile.Rect, new(), () => { CloseInspect(); _showDeck = false; BrowsePile = pile.Kind; _deckScroll = 0; _browseKey = ""; if (_snapshot != null) Update(_snapshot); });
	}
	private List<CardSnapshot>? _browseCards;
	private void UpdatePileBrowser(SpectatorSnapshot snapshot)
	{
		var pile = snapshot.Piles.Find(p => p.Kind == BrowsePile);
		if (pile == null) { BrowsePile = ""; _browseKey = ""; _browseCards = null; Clear(_browse); return; }
		if (_browseKey == BrowsePile && ReferenceEquals(_browseCards, pile.Cards)) return;
		_browseKey = BrowsePile; _browseCards = pile.Cards; Clear(_browse); Clear(_preview);
		DrawGrid(snapshot, pile.Cards, _browse, BrowsePile);
	}
	private void NavigateRelic(int direction)
	{
		int next = _relicIndex + direction;
		if (next < 0 || next >= _detailRelics.Count) return;
		_relicIndex = next; DrawRelicInspect();
	}
	private void DrawRelicInspect()
	{
		Clear(_preview); Clear(_inspect);
		if (_relicIndex < 0 || _relicIndex >= _detailRelics.Count) return;
		var relic = _detailRelics[_relicIndex];
		var shell = DecorativeScene("res://scenes/screens/inspect_relic_screen/inspect_relic_screen.tscn", _inspect, new Vector2(1920, 1080));
		if (shell == null) return;
		if (shell.GetNodeOrNull<Control>("%Backstop") is { } backstop) { backstop.Visible = true; backstop.Modulate = new Color(0, 0, 0, 0.9f); LocalButton(backstop, "SpectatorRelicClose", CloseInspect); }
		if (shell.GetNodeOrNull<CanvasItem>("%Popup") is { } popup) { popup.Visible = true; popup.Modulate = Colors.White; }
		SetLabel(shell, "%RelicName", relic.Title); SetLabel(shell, "%Rarity", relic.Rarity); SetLabel(shell, "%RelicDescription", relic.Description); SetLabel(shell, "%FlavorText", relic.Flavor);
		if (shell.GetNodeOrNull<TextureRect>("%RelicImage") is { } icon) { icon.Texture = Asset<Texture2D>(relic.Icon); icon.SelfModulate = Colors.White; }
		if (shell.GetNodeOrNull<CanvasItem>("%Rarity") is { } rarity) rarity.Modulate = ColorOf(relic.RarityColor);
		if (shell.GetNodeOrNull<CanvasItem>("%Frame") is { Material: ShaderMaterial original } frame)
		{
			var material = (ShaderMaterial)original.Duplicate(); frame.Material = material;
			material.SetShaderParameter("h", relic.FrameHsv[0]); material.SetShaderParameter("s", relic.FrameHsv[1]); material.SetShaderParameter("v", relic.FrameHsv[2]);
		}
		foreach (bool left in new[] { true, false })
		{
			var arrow = shell.GetNode<Control>(left ? "%LeftArrow" : "%RightArrow"); arrow.Modulate = Colors.White;
			arrow.Visible = left ? _relicIndex > 0 : _relicIndex < _detailRelics.Count - 1;
			var button = LocalButton(arrow, left ? "SpectatorRelicPrevious" : "SpectatorRelicNext", () => NavigateRelic(left ? -1 : 1)); button.Disabled = !arrow.Visible;
		}
		if (shell.GetNodeOrNull<Control>("HoverTipRect") is { } tipAnchor)
			Callable.From(() => { if (GodotObject.IsInstanceValid(shell) && shell.IsInsideTree()) DrawTips(shell, relic.ExtraTips, tipAnchor.Position + new Vector2(tipAnchor.Size.X, 0)); }).CallDeferred();
	}
}
