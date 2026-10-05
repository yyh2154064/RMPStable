using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class LocalSpectatorSource
{
	private static readonly FieldInfo? RewardOptions = typeof(NCardRewardSelectionScreen).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? RewardAlternatives = typeof(NCardRewardSelectionScreen).GetField("_extraOptions", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? DisplayedIntent = typeof(NIntent).GetField("_intent", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? NativeFontSize = typeof(MegaLabel).GetField("_lastSetSize", BindingFlags.Instance | BindingFlags.NonPublic);
	private string _backgroundRoom = "";
	private List<ArtSnapshot> _background = new();

	internal SpectatorSnapshot Capture(RunState run)
	{
		var player = run.Players[0];
		var combat = NCombatRoom.Instance;
		var merchant = NMerchantRoom.Instance;
		var reward = NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen;
		var state = player.PlayerCombatState;
		string room = NRun.Instance.GetInstanceId() + ":" + (combat?.GetInstanceId() ?? merchant?.GetInstanceId() ?? 0) + ":" + run.CurrentRoom?.GetType().Name + ":" + merchant?.Inventory?.IsOpen;
		string page = reward != null ? "reward" : merchant != null ? "shop" : combat != null ? "combat" : "run";
		var viewSize = NRun.Instance.GetViewportRect().Size;
		room += ":" + viewSize;
		if (room != _backgroundRoom || _background.Count == 0)
		{
			_backgroundRoom = room;
			_background = CaptureBackground(combat?.Background ?? (Node?)merchant);
		}
		var snap = new SpectatorSnapshot
		{
			Session = NRun.Instance.GetInstanceId().ToString(), Room = room, Page = page,
			Width = viewSize.X, Height = viewSize.Y, Background = _background,
			Character = player.Character.Title.GetFormattedText(),
			Summary = $"{T("生命", "HP")} {player.Creature.CurrentHp}/{player.Creature.MaxHp}   {T("格挡", "Block")} {player.Creature.Block}   {T("金币", "Gold")} {player.Gold}   {T("牌组", "Deck")} {player.Deck.Cards.Count}",
			Detail = combat != null && state != null
				? $"{T("能量", "Energy")} {state.Energy}/{state.MaxEnergy}   {T("星星", "Stars")} {state.Stars}   {T("抽牌堆", "Draw")} {state.DrawPile.Cards.Count}   {T("弃牌堆", "Discard")} {state.DiscardPile.Cards.Count}   {T("消耗", "Exhaust")} {state.ExhaustPile.Cards.Count}   {T("回合", "Turn")} {state.TurnNumber}"
				: T("单人本地模拟 · 仅展示，不操作游戏", "Local singleplayer preview · read only")
		};
		var top = Descendants<NTopBar>(NRun.Instance).FirstOrDefault();
		var hudRoots = new List<Node?> { top, NRun.Instance.GlobalUi.RelicInventory };
		if (top != null) snap.DeckButtonRect = GlobalRect(top.Deck);
		if (combat != null) hudRoots.AddRange(new Node?[] { combat.Ui.EnergyCounterContainer, combat.Ui.DrawPile, combat.Ui.DiscardPile, combat.Ui.ExhaustPile, combat.Ui.EndTurnButton });
		foreach (var hud in hudRoots) { snap.HudArt.AddRange(CaptureBackground(hud, includeButtons: true)); snap.HudLabels.AddRange(CaptureLabels(hud)); }
		// Only traverse the actual run, never the separate spectator window.
		var visibleCards = Descendants<NCard>(NRun.Instance).Where(c => c.Model != null && c.IsVisibleInTree())
			.GroupBy(c => c.Model!).ToDictionary(g => g.Key, g => g.First());
		CardSnapshot Card(CardModel model, PileType pile) => CaptureCard(model, pile, visibleCards.TryGetValue(model, out var visual) ? visual : null);
		snap.Deck = player.Deck.Cards.Select(c => Card(c, PileType.Deck)).ToList();
		snap.Inventory.AddRange(player.Relics.Select(r => new ItemSnapshot { Name = r.Title.GetFormattedText(), Icon = Path(r.Icon) }));
		snap.Inventory.AddRange(player.Potions.Select(p => new ItemSnapshot { Name = p.Title.GetFormattedText(), Icon = Path(p.Image) }));
		if (combat != null)
		{
			foreach (var node in combat.CreatureNodes)
			{
				var creature = node.Entity;
				var display = node.GetNodeOrNull<Control>("%HealthBar");
				var entry = new CreatureSnapshot
				{
					Name = creature.Name, Player = creature.IsPlayer || creature.IsPet,
					Hp = creature.CurrentHp, MaxHp = creature.MaxHp, Block = creature.Block,
					VisualScene = node.Visuals?.SceneFilePath ?? "",
					// An idle visual is sufficient for the prototype; no combat animator is created.
					Animation = "idle_loop",
					Transform = Transform(node.Visuals?.GetGlobalTransform() ?? node.GetGlobalTransform()),
					HealthRect = GlobalRect(Descendants<NHealthBar>(node).FirstOrDefault()?.HpBarContainer),
					IntentRect = GlobalRect(node.IntentContainer),
					StateArt = CaptureBackground(display), StateLabels = CaptureLabels(display),
					Powers = creature.Powers.Select(p => new ItemSnapshot { Name = p.Title.GetFormattedText(), Icon = p.IconPath, Text = p.DisplayAmount.ToString() }).ToList()
				};
				if (creature.Monster != null && creature.IsAlive)
				{
					// Read displayed intent, including the game's final damage calculation.
					// Do not roll moves, reveal hidden intentions, or infer future actions.
					foreach (var intent in Descendants<NIntent>(node.IntentContainer).Where(i => i.IsVisibleInTree()))
					{
						var holder = intent.GetNodeOrNull<Control>("%IntentHolder");
						if (holder == null || holder.Modulate.A < 0.01f) continue;
						var sprite = intent.GetNodeOrNull<Sprite2D>("%Intent");
						var label = intent.GetNodeOrNull<MegaRichTextLabel>("%Value");
						// Use a stable animation frame so the view is not rebuilt at animation FPS.
						var model = DisplayedIntent?.GetValue(intent) as AbstractIntent;
						string texture = Path(model?.GetTexture(new[] { player.Creature }, creature) ?? sprite?.Texture);
						entry.Intents.Add(new ItemSnapshot { Icon = texture, Text = label?.Text ?? "" });
					}
				}
				snap.Creatures.Add(entry);
			}
			if (state != null) snap.Cards = state.Hand.Cards.Select(c => Card(c, PileType.Hand)).ToList();
		}
		if (merchant != null)
		{
			snap.Cards.Clear();
			// Never generate an inventory: opening the actual merchant supplies it.
			var inventory = merchant.Inventory?.Inventory;
			if (inventory == null)
				snap.Detail = T("请在游戏中打开商店以读取商品", "Open the merchant in the game to inspect stock");
			else
			{
				var slots = Descendants<NMerchantSlot>(merchant.Inventory).ToList();
				foreach (var offer in inventory.CardEntries)
				{
					var card = offer.CreationResult?.Card;
					var item = card == null ? new CardSnapshot { Title = T("已售出", "Sold out"), Sold = true } : Card(card, PileType.None);
					item.Price = offer.Cost; item.Sold = !offer.IsStocked;
					var slot = slots.FirstOrDefault(s => s.Entry == offer);
					if (slot != null) { item.Art = CaptureBackground(slot); item.Labels = CaptureLabels(slot); }
					snap.Cards.Add(item);
				}
				foreach (var offer in inventory.RelicEntries)
					snap.Items.Add(new ItemSnapshot { Name = offer.Model?.Title.GetFormattedText() ?? T("已售出", "Sold out"), Icon = Path(offer.Model?.Icon), Price = offer.Cost, Sold = !offer.IsStocked });
				foreach (var offer in inventory.PotionEntries)
					snap.Items.Add(new ItemSnapshot { Name = offer.Model?.Title.GetFormattedText() ?? T("已售出", "Sold out"), Icon = Path(offer.Model?.Image), Price = offer.Cost, Sold = !offer.IsStocked });
				if (inventory.CardRemovalEntry is { } removal)
					snap.Items.Add(new ItemSnapshot { Name = T("移除卡牌", "Remove card"), Price = removal.Cost, Sold = !removal.IsStocked });
				foreach (var item in snap.Items)
				{
					var slot = slots.FirstOrDefault(s => s.Entry is MerchantRelicEntry r && r.Model?.Title.GetFormattedText() == item.Name || s.Entry is MerchantPotionEntry p && p.Model?.Title.GetFormattedText() == item.Name || s.Entry == inventory.CardRemovalEntry && item.Name == T("移除卡牌", "Remove card"));
					if (slot != null) { item.Rect = GlobalRect(slot.Hitbox); item.Art = CaptureBackground(slot); item.Labels = CaptureLabels(slot); }
				}
			}
		}
		else if (combat == null) snap.Cards = snap.Deck;
		if (reward != null)
		{
			if (RewardOptions?.GetValue(reward) is not IReadOnlyList<CardCreationResult> options)
				throw new InvalidOperationException("Card reward options are unavailable in this game version.");
			snap.UnderlayCards = snap.Cards; snap.UnderlayItems = snap.Items; snap.Items = new();
			snap.Cards = options.Select(o => Card(o.Card, PileType.None)).ToList();
			snap.RewardArt = CaptureBackground(NOverlayStack.Instance?.GetNodeOrNull<Node>("OverlayBackstop"));
			snap.RewardArt.AddRange(CaptureBackground(reward)); snap.RewardLabels = CaptureLabels(reward);
			if (RewardAlternatives?.GetValue(reward) is IReadOnlyList<CardRewardAlternative> alternatives)
				snap.Choices = alternatives.Select(a => a.Title.GetFormattedText()).ToList();
		}
		return snap;
	}

	private static CardSnapshot CaptureCard(CardModel c, PileType pile, NCard? visual)
	{
		bool ancient = c.Rarity == CardRarity.Ancient;
		int cost = c.EnergyCost.GetWithModifiers(pile == PileType.Hand ? CostModifiers.All : CostModifiers.Local);
		return new CardSnapshot
		{
			Transform = visual == null ? null : Transform(visual.Body.GetGlobalTransform()),
			Id = c.Id.ToString(), Title = c.Title, Type = c.Type.ToLocString().GetFormattedText(),
			Description = visual?.GetNodeOrNull<MegaRichTextLabel>("%DescriptionLabel")?.Text ?? c.GetDescriptionForPile(pile),
			Cost = c.EnergyCost.CostsX ? "X" : cost < 0 ? "" : cost.ToString(),
			StarCost = c.HasStarCostX ? "X" : c.CurrentStarCost < 0 ? "" : (pile == PileType.Hand ? c.GetStarCostWithModifiers() : c.CurrentStarCost).ToString(),
			Upgraded = c.IsUpgraded, Ancient = ancient, Portrait = Path(c.Portrait), Frame = Path(c.Frame),
			Border = Path(c.PortraitBorder), Banner = Path(c.BannerTexture), EnergyIcon = Path(c.EnergyIcon),
			FrameMaterial = Path(c.FrameMaterial), BannerMaterial = Path(c.BannerMaterial),
			AncientBorder = ancient ? Path(c.AncientBorder) : "", AncientText = ancient ? Path(c.AncientTextBg) : "",
			EnchantmentIcon = Path(c.Enchantment?.Icon),
			EnchantmentAmount = c.Enchantment?.ShowAmount == true ? c.Enchantment.DisplayAmount.ToString() : ""
		};
	}

	internal static List<ArtSnapshot> CaptureBackground(Node? root, bool includeButtons = false)
	{
		var result = new List<ArtSnapshot>();
		if (root == null) return result;
		void Visit(Node node, Color inherited, int z)
		{
			if (node is NMerchantSlot && node != root || node is NCreature || node is NCard || !includeButtons && node.GetType().Name.Contains("Button") && node.GetType().Name != "NMerchantButton" || node.Name == "MerchantHandContainer" || node.Name == "Dialogue") return;
			if (node is CanvasItem item)
			{
				if (!item.Visible) return;
				inherited *= item.Modulate;
				z = item.ZAsRelative ? z + item.ZIndex : item.ZIndex;
				Texture2D? texture = null;
				Rect2 rect = default;
				var art = new ArtSnapshot();
				if (item.GetClass() == "SpineSprite")
				{
					art.Skeleton = Path(item.Get("skeleton_data_res").As<Resource>());
					art.Animation = new MegaSprite(item).GetAnimationState().GetCurrentAnimationName(0) ?? "";
				}
				if (item is Sprite2D sprite)
				{
					texture = sprite.Texture; rect = sprite.GetRect(); art.FlipH = sprite.FlipH; art.FlipV = sprite.FlipV;
					if (sprite.RegionEnabled) art.Region = Rect(sprite.RegionRect);
					else if (texture != null && (sprite.Hframes > 1 || sprite.Vframes > 1))
					{
						var size = texture.GetSize() / new Vector2(sprite.Hframes, sprite.Vframes);
						art.Region = Rect(new Rect2((Vector2)sprite.FrameCoords * size, size));
					}
				}
				else if (item is TextureRect tr)
				{
					texture = tr.Texture; rect = new Rect2(Vector2.Zero, tr.Size);
					art.FlipH = tr.FlipH; art.FlipV = tr.FlipV; art.Stretch = (int)tr.StretchMode;
				}
				// Some native scenes attach a Control-derived C# script to a
				// TextureRect/ColorRect node (notably NCommonBanner). The managed
				// wrapper then is not TextureRect although the native node is.
				else if (item is Control drawing && item.GetClass() == "TextureRect")
				{
					texture = item.Get("texture").As<Texture2D>(); rect = new Rect2(Vector2.Zero, drawing.Size);
					art.FlipH = item.Get("flip_h").AsBool(); art.FlipV = item.Get("flip_v").AsBool(); art.Stretch = item.Get("stretch_mode").AsInt32();
				}
				else if (item is Control rectangle && item.GetClass() == "ColorRect") { art.Solid = true; rect = new Rect2(Vector2.Zero, rectangle.Size); }
				else if (item is NinePatchRect patch) { texture = patch.Texture; rect = new Rect2(Vector2.Zero, patch.Size); art.PatchMargins = new[] { patch.PatchMarginLeft, patch.PatchMarginTop, patch.PatchMarginRight, patch.PatchMarginBottom }; }
				else if (item is ColorRect solid) { art.Solid = true; rect = new Rect2(Vector2.Zero, solid.Size); }
				if ((texture != null && !string.IsNullOrEmpty(texture.ResourcePath) || art.Skeleton.Length > 0 || art.Solid) && result.Count < 512)
				{
					var transform = item.GetGlobalTransform();
					var color = inherited * item.SelfModulate;
					if (item is ColorRect solid) color *= solid.Color;
					else if (art.Solid) color *= item.Get("color").AsColor();
					art.Texture = Path(texture); art.Material = Path(item.Material);
					art.Rect = Rect(rect); art.Z = z;
					art.Transform = new[] { transform.X.X, transform.X.Y, transform.Y.X, transform.Y.Y, transform.Origin.X, transform.Origin.Y };
					art.Tint = new[] { color.R, color.G, color.B, color.A };
					result.Add(art);
				}
			}
			foreach (Node child in node.GetChildren()) Visit(child, inherited, z);
		}
		Visit(root, Colors.White, 0);
		return result.OrderBy(a => a.Z).ToList();
	}

	// Copy the already formatted native labels, including the game's hp/block
	// fonts and colors. No Creature is bound to the spectator health display.
	private static List<TextSnapshot> CaptureLabels(Node? root)
	{
		var result = new List<TextSnapshot>();
		if (root == null) return result;
		bool InsideCard(Node node) { for (Node? p = node; p != null && p != root; p = p.GetParent()) if (p is NCard) return true; return false; }
		foreach (var label in Descendants<Label>(root).Where(l => l.IsVisibleInTree() && !InsideCard(l)))
		{
			var settings = label.LabelSettings;
			var tint = (settings?.FontColor ?? label.GetThemeColor("font_color")) * label.SelfModulate;
			for (Node? parent = label; parent != null; parent = parent.GetParent()) if (parent is CanvasItem item) tint *= item.Modulate;
			if (tint.A < 0.01f) continue;
			var outline = settings?.OutlineColor ?? label.GetThemeColor("font_outline_color");
			int fontSize = settings?.FontSize ?? label.GetThemeFontSize("font_size", "Label");
			if (settings == null && label is MegaLabel && NativeFontSize?.GetValue(label) is int actual && actual > 0) fontSize = actual;
			result.Add(new TextSnapshot { Text = label.Text, Font = Path(settings?.Font ?? label.GetThemeFont("font", "Label")), FontSize = fontSize, OutlineSize = settings?.OutlineSize ?? label.GetThemeConstant("outline_size", "Label"), Transform = Transform(label.GetGlobalTransform()), Size = new[] { label.Size.X, label.Size.Y }, Color = new[] { tint.R, tint.G, tint.B, tint.A }, OutlineColor = new[] { outline.R, outline.G, outline.B, outline.A }, Alignment = (int)label.HorizontalAlignment, VerticalAlignment = (int)label.VerticalAlignment });
		}
		foreach (var label in Descendants<RichTextLabel>(root).Where(l => l.IsVisibleInTree() && !InsideCard(l)))
		{
			var color = label.GetThemeColor("default_color") * label.SelfModulate;
			for (Node? parent = label; parent != null; parent = parent.GetParent()) if (parent is CanvasItem item) color *= item.Modulate;
			if (color.A < 0.01f) continue;
			var outline = label.GetThemeColor("font_outline_color");
			result.Add(new TextSnapshot { Rich = true, Text = label.Text, Font = Path(label.GetThemeFont("normal_font")), FontSize = label.GetThemeFontSize("normal_font_size"), OutlineSize = label.GetThemeConstant("outline_size"), Transform = Transform(label.GetGlobalTransform()), Size = new[] { label.Size.X, label.Size.Y }, Color = new[] { color.R, color.G, color.B, color.A }, OutlineColor = new[] { outline.R, outline.G, outline.B, outline.A } });
		}
		return result;
	}

	private static float[] Rect(Rect2 r) => new[] { r.Position.X, r.Position.Y, r.Size.X, r.Size.Y };
	private static float[] Transform(Transform2D t) => new[] { t.X.X, t.X.Y, t.Y.X, t.Y.Y, t.Origin.X, t.Origin.Y };
	private static float[] GlobalRect(Control? c) => c == null ? new[] { 0f, 0f, 200f, 24f } : Rect(c.GetGlobalRect());
	private static string Path(Resource? resource) => resource?.ResourcePath ?? "";
	internal static string T(string zh, string en) => MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language == "zhs" ? zh : en;
	internal static IEnumerable<TNode> Descendants<TNode>(Node root) where TNode : Node
	{
		if (root is TNode match) yield return match;
		foreach (Node child in root.GetChildren())
			foreach (var result in Descendants<TNode>(child)) yield return result;
	}
}
