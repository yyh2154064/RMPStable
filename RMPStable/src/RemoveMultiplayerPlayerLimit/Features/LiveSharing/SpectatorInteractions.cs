using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.addons.mega_text;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private readonly List<int> _sortPriority = new() { 0, 1, 2, 3 };
	private readonly bool[] _descending = new bool[4];
	private bool _deckUpgraded, _inspectUpgraded;
	private List<CardSnapshot> _detailCards = new();
	private int _inspectIndex = -1;
	private float _deckScroll;
	private Button? _previous, _next;

	// Instantiate off-tree, remove every gameplay script BEFORE entering the
	// SceneTree. Only the two model-free text renderers remain. No native screen
	// lifecycle, save changes, or global hotkeys are used. Engine layout signals
	// remain intact; game callbacks are never registered because _Ready never runs.
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
	private static void SetLabel(Node root, string path, string text)
	{
		var label = root.GetNodeOrNull<Node>(path);
		if (label is MegaLabel plain) plain.SetTextAutoSize(text);
		else if (label is MegaRichTextLabel rich) rich.SetTextAutoSize(text);
		else if (label is Label native) native.Text = text;
		else if (label is RichTextLabel nativeRich) nativeRich.Text = text;
	}
	private static Button LocalButton(Control anchor, string name, Action pressed)
	{
		var button = new Button { Name = name, Flat = true, FocusMode = Control.FocusModeEnum.None };
		anchor.AddChild(button); button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		button.Pressed += pressed; return button;
	}
	private static void Tick(Control root, bool value)
	{
		if (root.GetNodeOrNull<CanvasItem>("%TickboxVisuals/Ticked") is { } yes) yes.Visible = value;
		if (root.GetNodeOrNull<CanvasItem>("%TickboxVisuals/NotTicked") is { } no) no.Visible = !value;
	}
	private List<CardSnapshot> SortedCards(List<CardSnapshot> cards)
	{
		var comparer = CultureInfo.GetCultureInfo(_snapshot?.Culture ?? "en-US").CompareInfo;
		var indices = Enumerable.Range(0, cards.Count).ToList();
		indices.Sort((a, b) =>
		{
			// Match NCardGrid: acquisition order preserves/reverses the pile;
			// other modes use the last chosen sorts as deterministic tie breakers.
			foreach (int mode in _sortPriority)
			{
				int diff = mode switch { 0 => a.CompareTo(b), 1 => cards[a].SortType.CompareTo(cards[b].SortType), 2 => cards[a].SortCost.CompareTo(cards[b].SortCost), _ => comparer.Compare(cards[a].Title, cards[b].Title, CompareOptions.None) };
				if (diff != 0) return _descending[mode] ? -diff : diff;
			}
			return a.CompareTo(b);
		});
		return indices.Select(i => cards[i]).ToList();
	}
	private void SortDeck(int mode)
	{
		_descending[mode] = !_descending[mode]; _sortPriority.Remove(mode); _sortPriority.Insert(0, mode);
		_deckScroll = 0; _cardKey = ""; CloseInspect(); if (_snapshot != null) Update(_snapshot);
	}
	private void DrawDeck(SpectatorSnapshot snapshot, List<CardSnapshot> cards, Control parent)
	{
		var shell = DecorativeScene("res://scenes/screens/deck_view_screen.tscn", parent, new Vector2(snapshot.Width, snapshot.Height));
		if (shell == null) return;
		Solid(parent, new Rect2(0, 80, snapshot.Width, snapshot.Height - 80), new Color(0, 0, 0, 0.65f));
		parent.MoveChild(shell, parent.GetChildCount() - 1);
		var grid = shell.GetNode<Control>("CardGrid"); grid.SetAnchorAndOffset(Side.Top, 0, 80);
		var content = grid.GetNode<Control>("%ScrollContainer");
		var cardLayer = Area(content, Vector2.Zero, new Vector2(content.Size.X, 1));
		var material = Asset<Material>(cards.FirstOrDefault()?.FrameMaterial ?? "");
		if (shell.GetNodeOrNull<CanvasItem>("%SortingBg") is { } bg) bg.Material = material;
		string[] paths = { "%ObtainedSorter", "%CardTypeSorter", "%CostSorter", "%AlphabeticalSorter" };
		string[] labels = { T("获得顺序", "Obtained"), T("类型", "Type"), T("费用", "Cost"), T("拼音顺序", "Alphabetical") };
		for (int i = 0; i < 4; i++)
		{
			int mode = i; var anchor = shell.GetNode<Control>(paths[i]);
			SetLabel(anchor, "%Label", labels[i]);
			if (anchor.GetNodeOrNull<TextureRect>("%Image") is { } arrow) arrow.FlipV = !_descending[i];
			if (anchor.GetNodeOrNull<CanvasItem>("%ButtonImage") is { } image) image.Material = material;
			LocalButton(anchor, "SpectatorSort" + i, () => SortDeck(mode));
		}
		SetLabel(shell, "%ViewUpgradesLabel", T("查看升级", "View upgrades"));
		if (shell.GetNodeOrNull<Control>("%Upgrades") is { } upgrades)
		{ Tick(upgrades, _deckUpgraded); LocalButton(upgrades, "SpectatorDeckUpgrade", () => { _deckUpgraded = !_deckUpgraded; _cardKey = ""; if (_snapshot != null) Update(_snapshot); }); }
		if (shell.GetNodeOrNull<Control>("%BackButton") is { } back) LocalButton(back, "SpectatorDeckBack", () => { _showDeck = false; _cardKey = ""; CloseInspect(); if (_snapshot != null) Update(_snapshot); });
		SetLabel(shell, "%BottomLabel", T("你在战斗中将会使用这里的所有卡牌。", "These are the cards in your deck."));
		// NCardGrid uses 0.8 scale, 40 px padding, 80 px top margin and
		// NDeckViewScreen adds YOffset=100. The native shell sets the insets.
		const float width = 240, height = 337.6f, padding = 40;
		int columns = Math.Max(1, (int)((content.Size.X + padding) / (width + padding)));
		var sorted = SortedCards(cards);
		float left = (content.Size.X - (columns * width + (columns - 1) * padding)) * 0.5f;
		float totalHeight = MathF.Ceiling(sorted.Count / (float)columns) * (height + padding) + 80 + 100 + 320 - padding;
		float maxScroll = Math.Max(0, totalHeight - grid.Size.Y - 320);
		var scrollbar = grid.GetNodeOrNull<Control>("%Scrollbar");
		void ScrollTo(float value)
		{
			_deckScroll = Math.Clamp(value, 0, maxScroll); content.Position = new Vector2(content.Position.X, -_deckScroll); Clear(_preview);
			if (scrollbar?.GetNodeOrNull<Control>("Handle") is { } handle) handle.Position = new Vector2((scrollbar.Size.X - handle.Size.X) / 2, (maxScroll > 0 ? _deckScroll / maxScroll : 0) * scrollbar.Size.Y - handle.Size.Y / 2);
		}
		_deckScroll = Math.Clamp(_deckScroll, 0, maxScroll);
		content.Position = new Vector2(content.Position.X, -_deckScroll); content.Size = new Vector2(content.Size.X, totalHeight);
		grid.ClipContents = true;
		grid.MouseFilter = Control.MouseFilterEnum.Stop;
		grid.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true } wheel && wheel.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp)
			{ ScrollTo(_deckScroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 120 : -120)); grid.AcceptEvent(); }
		};
		for (int i = 0; i < sorted.Count; i++)
		{
			var card = _deckUpgraded ? sorted[i].Upgrade ?? sorted[i] : sorted[i];
			var slot = Area(cardLayer, new Vector2(left + i % columns * (width + padding), 180 + i / columns * (height + padding)), new Vector2(width, height));
			slot.MouseFilter = Control.MouseFilterEnum.Pass; DrawCard(slot, card, Vector2.Zero, 0.8f); Hover(slot, card, sorted, i, _deckUpgraded);
		}
		if (scrollbar != null)
		{
			scrollbar.Visible = maxScroll > 0; scrollbar.MouseFilter = Control.MouseFilterEnum.Stop;
			bool dragging = false;
			scrollbar.GuiInput += input =>
			{
				if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click) { dragging = click.Pressed; if (dragging) ScrollTo(click.Position.Y / scrollbar.Size.Y * maxScroll); scrollbar.AcceptEvent(); }
				else if (input is InputEventMouseMotion motion && dragging) { ScrollTo(motion.Position.Y / scrollbar.Size.Y * maxScroll); scrollbar.AcceptEvent(); }
			};
			ScrollTo(_deckScroll);
		}
	}
	private void DrawTips(Node parent, List<TipSnapshot> tips, Vector2 position)
	{
		var flow = new VBoxContainer { Name = "SpectatorTips", Position = position, Size = new Vector2(360, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		flow.AddThemeConstantOverride("separation", 5); parent.AddChild(flow);
		foreach (var tip in tips)
		{
			if (tip.Card != null) { var slot = new Control { CustomMinimumSize = new Vector2(300, 422) }; flow.AddChild(slot); DrawCard(slot, tip.Card, Vector2.Zero, 1); continue; }
			var panel = DecorativeScene("res://scenes/ui/hover_tip.tscn", flow, new Vector2(360, 0));
			if (panel == null) continue;
			SetLabel(panel, "%Title", tip.Title); SetLabel(panel, "%Description", tip.Description);
			if (panel.GetNodeOrNull<CanvasItem>("%Title") is { } title) title.Visible = tip.Title.Length > 0;
			if (panel.GetNodeOrNull<TextureRect>("%Icon") is { } icon) { icon.Texture = Asset<Texture2D>(tip.Icon); icon.Visible = icon.Texture != null; }
			if (tip.Debuff && panel.GetNodeOrNull<CanvasItem>("%Bg") is { } bg) bg.Material = Asset<Material>("res://materials/ui/hover_tip_debuff.tres");
			if (panel.GetNodeOrNull<RichTextLabel>("%Description") is { } description) { description.FitContent = true; description.AutowrapMode = TextServer.AutowrapMode.WordSmart; }
			panel.CustomMinimumSize = new Vector2(360, 0); panel.ResetSize();
		}
	}
	private void OpenInspect(List<CardSnapshot> cards, int index, bool upgraded = false)
	{ if (index < 0 || index >= cards.Count) return; _detailCards = cards.ToList(); _inspectIndex = index; _inspectAllUpgraded = upgraded; _inspectUpgraded = upgraded || cards[index].Upgraded; DrawInspect(); }
	private bool _inspectAllUpgraded;
	private void CloseInspect() { _inspectIndex = -1; _detailCards.Clear(); Clear(_inspect); Clear(_preview); }
	private void NavigateInspect(int direction)
	{ int next = _inspectIndex + direction; if (next < 0 || next >= _detailCards.Count) return; _inspectIndex = next; _inspectUpgraded = _inspectAllUpgraded || _detailCards[next].Upgraded; DrawInspect(); }
	private void DrawInspect()
	{
		Clear(_inspect); Clear(_preview);
		if (_inspectIndex < 0) return;
		var shell = DecorativeScene("res://scenes/screens/inspect_card_screen.tscn", _inspect, new Vector2(1920, 1080));
		if (shell == null) return;
		if (shell.GetNodeOrNull<Control>("Backstop") is { } backstop) { backstop.Modulate = new Color(0, 0, 0, 0.9f); LocalButton(backstop, "SpectatorInspectClose", CloseInspect); }
		var card = _detailCards[_inspectIndex]; card = _inspectUpgraded ? card.Upgrade ?? card : card.Base ?? card;
		var anchor = shell.GetNode<Control>("Card"); anchor.Scale = Vector2.One; Clear(anchor); DrawCard(anchor, card, Vector2.Zero, 1.75f, centered: true);
		var previous = shell.GetNode<Control>("LeftArrow"); previous.Visible = _inspectIndex > 0;
		_previous = LocalButton(previous, "SpectatorPrevious", () => NavigateInspect(-1)); _previous.Disabled = _inspectIndex == 0;
		var next = shell.GetNode<Control>("RightArrow"); next.Visible = _inspectIndex < _detailCards.Count - 1;
		_next = LocalButton(next, "SpectatorNext", () => NavigateInspect(1)); _next.Disabled = _inspectIndex == _detailCards.Count - 1;
		var upgrades = shell.GetNode<Control>("%Upgrade"); upgrades.Visible = _detailCards[_inspectIndex].Upgrade != null || card.Upgraded;
		Tick(upgrades, _inspectUpgraded); SetLabel(shell, "%ShowUpgradeLabel", T("查看升级", "View upgrades"));
		LocalButton(upgrades, "SpectatorInspectUpgrade", () => { _inspectAllUpgraded = false; _inspectUpgraded = !_inspectUpgraded; DrawInspect(); });
		var tipAnchor = shell.GetNode<Control>("HoverTipRect");
		Callable.From(() => { if (GodotObject.IsInstanceValid(shell) && shell.IsInsideTree()) DrawTips(shell, card.Tips, tipAnchor.Position + new Vector2(tipAnchor.Size.X, 0)); }).CallDeferred();
	}
}
