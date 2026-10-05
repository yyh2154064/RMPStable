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
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
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
	private readonly Dictionary<CardModel, (string Key, CardSnapshot Preview, CardSnapshot? Base)> _upgrades = new();
	private static readonly Dictionary<string, string[]> UniformNames = new();

	internal SpectatorSnapshot Capture(RunState run)
	{
		var player = run.Players[0];
		var combat = NCombatRoom.Instance;
		var merchant = NMerchantRoom.Instance;
		var reward = NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen;
		var overlay = NOverlayStack.Instance?.Peek() as Control;
		if (overlay?.IsVisibleInTree() != true) { overlay = null; reward = null; }
		var mapScreen = NMapScreen.Instance;
		var capstone = NCapstoneContainer.Instance?.CurrentCapstoneScreen as Control;
		var eventRoom = NEventRoom.Instance;
		var state = player.PlayerCombatState;
		string room = NRun.Instance.GetInstanceId() + ":" + (combat?.GetInstanceId() ?? merchant?.GetInstanceId() ?? 0) + ":" + run.CurrentRoom?.GetType().Name + ":" + merchant?.Inventory?.IsOpen;
		string page = reward != null ? "reward" : merchant != null ? "shop" : combat != null ? "combat" : "run";
		string underlayPage = merchant != null ? "shop" : combat != null ? "combat" : "";
		Control? pageRoot = overlay ?? (mapScreen?.IsOpen == true ? mapScreen : eventRoom);
		if (eventRoom?.IsVisibleInTree() == true) page = "event";
		if (mapScreen?.IsOpen == true) page = "map";
		if (overlay != null) page = reward != null ? "reward" : overlay is NRewardsScreen ? "loot" : "selection";
		if (capstone != null) { pageRoot = capstone; page = capstone is NDeckViewScreen ? "deck" : "selection"; }
		var viewSize = NRun.Instance.GetViewportRect().Size;
		room += ":" + viewSize;
		if (room != _backgroundRoom || _background.Count == 0)
		{
			_backgroundRoom = room;
			_background = CaptureBackground(combat?.Background ?? (Node?)merchant);
		}
		var snap = new SpectatorSnapshot
		{
			Session = NRun.Instance.GetInstanceId().ToString(), Room = room, Page = page, UnderlayPage = underlayPage,
			Width = viewSize.X, Height = viewSize.Y, Background = _background, Culture = LocManager.Instance.CultureInfo.Name,
			Character = player.Character.Title.GetFormattedText(),
			Summary = $"{T("生命", "HP")} {player.Creature.CurrentHp}/{player.Creature.MaxHp}   {T("格挡", "Block")} {player.Creature.Block}   {T("金币", "Gold")} {player.Gold}   {T("牌组", "Deck")} {player.Deck.Cards.Count}",
			Detail = combat != null && state != null
				? $"{T("能量", "Energy")} {state.Energy}/{state.MaxEnergy}   {T("星星", "Stars")} {state.Stars}   {T("抽牌堆", "Draw")} {state.DrawPile.Cards.Count}   {T("弃牌堆", "Discard")} {state.DiscardPile.Cards.Count}   {T("消耗", "Exhaust")} {state.ExhaustPile.Cards.Count}   {T("回合", "Turn")} {state.TurnNumber}"
				: T("单人本地模拟 · 仅展示，不操作游戏", "Local singleplayer preview · read only")
		};
		var top = Descendants<NTopBar>(NRun.Instance).FirstOrDefault();
		var hudRoots = new List<Node?> { top, NRun.Instance.GlobalUi.RelicInventory };
		if (top != null) snap.DeckButtonRect = GlobalRect(top.Deck);
		if (combat != null) foreach (var ui in new Node[] { combat.Ui.EnergyCounterContainer, combat.Ui.DrawPile, combat.Ui.DiscardPile, combat.Ui.ExhaustPile, combat.Ui.EndTurnButton })
		{ snap.CombatHudArt.AddRange(CaptureBackground(ui, true)); snap.CombatHudLabels.AddRange(CaptureLabels(ui)); }
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
						var nativeArt = CaptureBackground(holder);
						if (sprite != null) foreach (var art in nativeArt.Where(a => a.Key == sprite.GetInstanceId().ToString())) art.Texture = texture;
						var labels = CaptureLabels(holder);
						// Intent labels are intentionally very short native rectangles.
						// Leave room for the font's descent and outline on a separate
						// viewport; preserve its original position and wrapping mode.
						foreach (var value in labels) value.Size[1] = Math.Max(value.Size[1], value.FontSize * 2 + value.OutlineSize);
						if (label != null) foreach (var value in labels.Where(l => l.Text == label.Text))
						{
							float extra = Math.Max(0, label.GetContentWidth() + value.OutlineSize - value.Size[0]);
							value.Size[0] += extra;
							// Preserve the native text anchor when its rectangle grows.
							if (value.Alignment == (int)HorizontalAlignment.Center) value.Transform[4] -= extra / 2;
							else if (value.Alignment == (int)HorizontalAlignment.Right) value.Transform[4] -= extra;
						}
						entry.Intents.Add(new ItemSnapshot { Icon = texture, Text = label?.Text ?? "", Art = nativeArt, Labels = labels });
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
		if (pageRoot != null && page is not "combat" and not "shop" and not "run" and not "reward")
		{
			snap.UnderlayCards = snap.Cards; snap.Cards = new(); snap.Items.Clear();
			if (pageRoot == overlay) snap.PageArt.AddRange(CaptureBackground(NOverlayStack.Instance?.GetNodeOrNull<Node>("OverlayBackstop")));
			if (eventRoom?.IsVisibleInTree() == true && pageRoot != eventRoom)
			{ snap.PageArt.AddRange(CaptureBackground(eventRoom, true, includeCards: true)); snap.PageLabels.AddRange(CaptureLabels(eventRoom)); }
			var mapBounds = new Rect2(Vector2.Zero, viewSize).Grow(128);
			if (page == "deck" && mapScreen?.IsOpen == true)
			{ snap.PageArt.AddRange(CaptureBackground(mapScreen, true, mapBounds)); snap.PageLabels.AddRange(CaptureLabels(mapScreen)); snap.Drawings = CaptureDrawings(mapScreen.Drawings); }
			if (page != "deck") { snap.PageArt.AddRange(CaptureBackground(pageRoot, true, page == "map" ? mapBounds : null, true)); snap.PageLabels.AddRange(CaptureLabels(pageRoot)); }
			foreach (var card in Descendants<NCard>(pageRoot).Where(c => c.Model != null && c.IsVisibleInTree())) snap.Cards.Add(CaptureCard(card.Model!, PileType.None, card));
			if (page == "map") snap.Drawings = CaptureDrawings(mapScreen!.Drawings);
		}
		if (page == "event")
		{
			foreach (Node previewRoot in new Node[] { NRun.Instance.GlobalUi.CardPreviewContainer, NRun.Instance.GlobalUi.EventCardPreviewContainer, NRun.Instance.GlobalUi.GridCardPreviewContainer, NRun.Instance.GlobalUi.MessyCardPreviewContainer })
			{
				snap.PageArt.AddRange(CaptureBackground(previewRoot, includeCards: true)); snap.PageLabels.AddRange(CaptureLabels(previewRoot));
				foreach (var card in Descendants<NCard>(previewRoot).Where(c => c.Model != null && c.IsVisibleInTree())) snap.Cards.Add(CaptureCard(card.Model!, PileType.None, card));
			}
		}
		if (page == "event" && eventRoom?.IsVisibleInTree() == true) foreach (var option in Descendants<NEventOptionButton>(eventRoom).Where(n => n.IsVisibleInTree()))
			snap.Hovers.Add(new HoverSnapshot { Rect = GlobalRect(option), Tips = CaptureTips(option.Option.HoverTips) });
		// Evict detached previews for cards that have left the player's run.
		var present = new HashSet<CardModel>(visibleCards.Keys.Concat(player.Deck.Cards));
		foreach (var old in _upgrades.Keys.Where(c => !present.Contains(c)).ToList()) _upgrades.Remove(old);
		return snap;
	}
	private List<TipSnapshot> CaptureTips(IEnumerable<IHoverTip> tips)
	{
		var result = new List<TipSnapshot>();
		foreach (var tip in IHoverTip.RemoveDupes(tips))
		{
			if (tip is HoverTip text) result.Add(new TipSnapshot { Title = text.Title ?? "", Description = text.Description, Icon = Path(text.Icon), Debuff = text.IsDebuff });
			else if (tip is CardHoverTip card) result.Add(new TipSnapshot { Card = CaptureCard(card.Card, PileType.None, null, tips: false) });
		}
		return result;
	}

	private CardSnapshot CaptureCard(CardModel c, PileType pile, NCard? visual, bool preview = false, bool tips = true)
	{
		bool ancient = c.Rarity == CardRarity.Ancient;
		int cost = c.EnergyCost.GetWithModifiers(pile == PileType.Hand ? CostModifiers.All : CostModifiers.Local);
		var result = new CardSnapshot
		{
			ArtKey = visual?.GetInstanceId().ToString() ?? "",
			Transform = visual == null ? null : Transform(visual.Body.GetGlobalTransform()),
			Id = c.Id.ToString(), Title = c.Title, Type = c.Type.ToLocString().GetFormattedText(),
			Description = preview ? c.GetDescriptionForUpgradePreview() : visual?.GetNodeOrNull<MegaRichTextLabel>("%DescriptionLabel")?.Text ?? c.GetDescriptionForPile(pile),
			Cost = c.EnergyCost.CostsX ? "X" : cost < 0 ? "" : cost.ToString(),
			StarCost = c.HasStarCostX ? "X" : c.CurrentStarCost < 0 ? "" : (pile == PileType.Hand ? c.GetStarCostWithModifiers() : c.CurrentStarCost).ToString(),
			Upgraded = c.IsUpgraded, Ancient = ancient, Portrait = Path(c.Portrait), Frame = Path(c.Frame),
			Border = Path(c.PortraitBorder), Banner = Path(c.BannerTexture), EnergyIcon = Path(c.EnergyIcon),
			FrameMaterial = Path(c.FrameMaterial), BannerMaterial = Path(c.BannerMaterial),
			AncientBorder = ancient ? Path(c.AncientBorder) : "", AncientText = ancient ? Path(c.AncientTextBg) : "",
			EnchantmentIcon = Path(c.Enchantment?.Icon),
			EnchantmentAmount = c.Enchantment?.ShowAmount == true ? c.Enchantment.DisplayAmount.ToString() : "",
			SortCost = c.EnergyCost.GetResolved(), SortType = (int)c.Type,
			CostColor = ColorValues(visual?.GetNodeOrNull<Label>("%EnergyLabel")?.GetThemeColor("font_color") ?? (preview && c.EnergyCost.WasJustUpgraded ? StsColors.green : StsColors.cream)),
			CostOutline = ColorValues(visual?.GetNodeOrNull<Label>("%EnergyLabel")?.GetThemeColor("font_outline_color") ?? c.Pool.EnergyOutlineColor),
			StarColor = ColorValues(visual?.GetNodeOrNull<Label>("%StarLabel")?.GetThemeColor("font_color") ?? StsColors.cream),
			StarOutline = ColorValues(visual?.GetNodeOrNull<Label>("%StarLabel")?.GetThemeColor("font_outline_color") ?? StsColors.defaultStarCostOutline)
		};
		if (tips) result.Tips = CaptureTips(c.HoverTips);
		if (!preview && tips && (c.IsUpgradable || c.IsUpgraded))
		{
			string key = c.CurrentUpgradeLevel + ":" + c.EnergyCost.GetResolved() + ":" + result.Description + ":" + c.Enchantment?.Amount;
			if (!_upgrades.TryGetValue(c, out var cached) || cached.Key != key)
			{
				// Same detached preview procedure as NInspectCardScreen. MutableClone
				// clears the live card's event delegates; never upgrade the live model.
				var copy = (CardModel)c.MutableClone(); copy.UpgradePreviewType = CardUpgradePreviewType.Deck;
				if (!copy.IsUpgraded) copy.UpgradeInternal();
				var upgraded = CaptureCard(copy, PileType.None, null, preview: true);
				CardSnapshot? baseCard = null;
				if (c.IsUpgraded) { var normal = (CardModel)c.MutableClone(); CardCmd.Downgrade(normal); baseCard = CaptureCard(normal, PileType.None, null, preview: true); baseCard.Description = normal.GetDescriptionForPile(PileType.None); }
				cached = (key, upgraded, baseCard); _upgrades[c] = cached;
			}
			result.Upgrade = cached.Preview;
			result.Base = cached.Base;
		}
		return result;
	}

	private static float[] ColorValues(Color c) => new[] { c.R, c.G, c.B, c.A };
	private static List<DrawingSnapshot> CaptureDrawings(NMapDrawings drawings)
	{
		var result = new List<DrawingSnapshot>();
		foreach (var surface in Descendants<TextureRect>(drawings).Where(t => t.Texture is ViewportTexture && t.IsVisibleInTree()))
		{
			var viewport = surface.GetParent().GetChildren().OfType<SubViewport>().FirstOrDefault();
			if (viewport == null) continue;
			var lines = new List<ArtSnapshot>();
			foreach (Node child in viewport.GetChildren()) lines.AddRange(CaptureBackground(child));
			result.Add(new DrawingSnapshot { Transform = Transform(surface.GetGlobalTransform()), Size = new[] { surface.Size.X, surface.Size.Y }, ViewportSize = new[] { viewport.Size.X, viewport.Size.Y }, Lines = lines });
		}
		return result;
	}

	internal static List<ArtSnapshot> CaptureBackground(Node? root, bool includeButtons = false, Rect2? visibleRegion = null, bool includeCards = false)
	{
		var result = new List<ArtSnapshot>();
		if (root == null) return result;
		void Visit(Node node, string parentKey)
		{
			if (node is SubViewport || node is NMerchantSlot && node != root || node is NCreature || node is NCard && !includeCards || !includeButtons && node.GetType().Name.Contains("Button") && node.GetType().Name != "NMerchantButton" || node.Name == "MerchantHandContainer") return;
			if (node is CanvasItem item)
			{
				if (!item.Visible) return;
				// Map content spans several screen heights. Omit room points and
				// texture leaves outside the current viewport, with a glow margin.
				// Camera scroll captures newly visible content on the next snapshot.
				if (visibleRegion is { } viewport && item is Control boundsToCull && (item is NMapPoint || item.GetClass() == "TextureRect") && boundsToCull.Size.X > 0 && boundsToCull.Size.Y > 0 && !viewport.Intersects(boundsToCull.GetGlobalRect())) return;
				Texture2D? texture = null;
				Rect2 rect = item is Control bounds ? new Rect2(Vector2.Zero, bounds.Size) : default;
				var art = new ArtSnapshot { Key = item.GetInstanceId().ToString(), Parent = parentKey, Z = item.ZIndex, ZRelative = item.ZAsRelative, BehindParent = item.ShowBehindParent, ClipChildren = (int)item.ClipChildren, ClipContents = item is Control clipping && clipping.ClipContents };
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
				else if (item is Line2D line)
				{
					art.Points = line.Points.SelectMany(p => new[] { p.X, p.Y }).ToArray(); art.LineWidth = line.Width;
					art.BeginCap = (int)line.BeginCapMode; art.EndCap = (int)line.EndCapMode; art.Joint = (int)line.JointMode; art.Antialiased = line.Antialiased;
				}
				else if (item is Polygon2D polygon) { art.Polygon = true; art.Points = polygon.Polygon.SelectMany(p => new[] { p.X, p.Y }).ToArray(); texture = polygon.Texture; }
				if (result.Count < 8192)
				{
					// AtlasManager creates pathless AtlasTexture instances for map
					// icons and other packed UI. Send the underlying atlas + region.
					if (texture is AtlasTexture atlas && texture.ResourcePath.Length == 0) { art.Region = Rect(atlas.Region); texture = atlas.Atlas; }
					var transform = item.GetGlobalTransform();
					var color = item.SelfModulate;
					var modulation = item.Modulate;
					if (parentKey.Length == 0) for (Node? p = item.GetParent(); p != null; p = p.GetParent()) if (p is CanvasItem ancestor) modulation *= ancestor.Modulate;
					if (item is ColorRect solid) color *= solid.Color;
					else if (art.Solid) color *= item.Get("color").AsColor();
					else if (item is Line2D line) color *= line.DefaultColor;
					else if (item is Polygon2D polygon) color *= polygon.Color;
					art.Texture = Path(texture); art.Material = Path(item.Material);
					if (item.Material is ShaderMaterial shader) { art.Shader = Path(shader.Shader); art.ShaderValues = CaptureShaderValues(shader); }
					art.Group = art.Texture.Length == 0 && art.Skeleton.Length == 0 && !art.Solid && art.Points == null;
					art.Rect = Rect(rect);
					art.Transform = new[] { transform.X.X, transform.X.Y, transform.Y.X, transform.Y.Y, transform.Origin.X, transform.Origin.Y };
					art.Tint = ColorValues(modulation); art.SelfTint = ColorValues(color);
					result.Add(art);
					parentKey = art.Key;
				}
			}
			// A card placeholder preserves the native mask/order. Its visual
			// children are rebuilt from DTOs, never copied as game models.
			if (node is NCard) return;
			foreach (Node child in node.GetChildren()) Visit(child, parentKey);
		}
		Visit(root, "");
		return result;
	}
	private static List<ShaderValueSnapshot> CaptureShaderValues(ShaderMaterial material)
	{
		var result = new List<ShaderValueSnapshot>();
		if (material.Shader == null) return result;
		string shaderPath = Path(material.Shader);
		if (!UniformNames.TryGetValue(shaderPath, out var names))
		{
			names = material.Shader.GetShaderUniformList().Select(u => u.AsGodotDictionary()["name"].AsString()).ToArray();
			if (shaderPath.Length > 0) UniformNames[shaderPath] = names;
		}
		foreach (string name in names)
		{
			var value = material.GetShaderParameter(name);
			var entry = new ShaderValueSnapshot { Name = name, Kind = value.VariantType.ToString() };
			switch (value.VariantType)
			{
				case Variant.Type.Bool: entry.Values = new[] { value.AsBool() ? 1f : 0f }; break;
				case Variant.Type.Int: entry.Values = new[] { (float)value.AsInt32() }; break;
				case Variant.Type.Float: entry.Values = new[] { value.AsSingle() }; break;
				case Variant.Type.Color: entry.Values = ColorValues(value.AsColor()); break;
				case Variant.Type.Vector2: var v2 = value.AsVector2(); entry.Values = new[] { v2.X, v2.Y }; break;
				case Variant.Type.Vector3: var v3 = value.AsVector3(); entry.Values = new[] { v3.X, v3.Y, v3.Z }; break;
				case Variant.Type.Vector4: var v4 = value.AsVector4(); entry.Values = new[] { v4.X, v4.Y, v4.Z, v4.W }; break;
				case Variant.Type.Object when value.AsGodotObject() is Texture2D tex: entry.Texture = Path(tex); if (entry.Texture.Length == 0) continue; break;
				default: continue;
			}
			result.Add(entry);
		}
		return result;
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
			result[^1].ArtKey = label.GetInstanceId().ToString(); result[^1].LocalColor = ColorValues((settings?.FontColor ?? label.GetThemeColor("font_color")) * label.SelfModulate);
		}
		foreach (var label in Descendants<RichTextLabel>(root).Where(l => l.IsVisibleInTree() && !InsideCard(l)))
		{
			var color = label.GetThemeColor("default_color") * label.SelfModulate;
			for (Node? parent = label; parent != null; parent = parent.GetParent()) if (parent is CanvasItem item) color *= item.Modulate;
			if (color.A < 0.01f) continue;
			var outline = label.GetThemeColor("font_outline_color");
			result.Add(new TextSnapshot { Rich = true, Text = label.Text.Replace("[ancient_banner]", "").Replace("[/ancient_banner]", ""), Font = Path(label.GetThemeFont("normal_font")), FontSize = label.GetThemeFontSize("normal_font_size"), OutlineSize = label.GetThemeConstant("outline_size"), Transform = Transform(label.GetGlobalTransform()), Size = new[] { label.Size.X, label.Size.Y }, Color = new[] { color.R, color.G, color.B, color.A }, OutlineColor = new[] { outline.R, outline.G, outline.B, outline.A }, Alignment = (int)label.HorizontalAlignment, VerticalAlignment = (int)label.VerticalAlignment, WrapMode = (int)label.AutowrapMode });
			result[^1].ArtKey = label.GetInstanceId().ToString(); result[^1].LocalColor = ColorValues(label.GetThemeColor("default_color") * label.SelfModulate);
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
