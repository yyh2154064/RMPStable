using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class LocalSpectatorSource
{
	private static readonly Dictionary<(Type, string), FieldInfo?> TipFields = new();
	private static IHoverTip? NativeTip(object node, string field)
	{
		var key = (node.GetType(), field);
		if (!TipFields.TryGetValue(key, out var info)) TipFields[key] = info = key.Item1.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
		return info?.GetValue(node) as IHoverTip;
	}
	private void CaptureInspectionTargets(SpectatorSnapshot snapshot, RunState run, NTopBar? top)
	{
		void Tip(Control? target, IEnumerable<IHoverTip> tips)
		{
			if (target?.IsVisibleInTree() != true) return;
			var captured = CaptureTips(tips);
			if (captured.Count > 0) snapshot.HudHovers.Add(new HoverSnapshot { Rect = GlobalRect(target), Tips = captured });
		}
		void StaticTip(Control? target, string prefix) => Tip(target, new IHoverTip[] { new HoverTip(new LocString("static_hover_tips", prefix + ".title"), new LocString("static_hover_tips", prefix + ".description")) });
		if (top != null)
		{
			StaticTip(top.Hp, "HIT_POINTS"); StaticTip(top.Gold, "MONEY_POUCH"); StaticTip(top.Map, "ROOM_MAP");
			if (NativeTip(top.PortraitTip, "_hoverTip") is { } portrait) Tip(top.PortraitTip, new[] { portrait });
			// Unknown-node travel updates map history before BaseRoom exists.
			// The game's optional tooltip resolver dereferences that room; a
			// missing tooltip must not invalidate the whole scene or its controls.
			string? prefix = null;
			if (run.BaseRoom != null)
			{
				try { prefix = top.RoomIcon.GetType().GetMethod("GetHoverTipPrefixForRoomType", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(top.RoomIcon, null) as string; }
				catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException or ArgumentOutOfRangeException) { }
			}
			if (prefix != null) StaticTip(top.RoomIcon, prefix);
			bool secondOnly = run.Map.SecondBossMapPoint != null && run.CurrentMapPoint == run.Map.BossMapPoint;
			bool both = run.Act.SecondBossEncounter != null && !secondOnly;
			var title = new LocString("static_hover_tips", (both ? "DOUBLE_BOSS" : "BOSS") + ".title");
			var body = new LocString("static_hover_tips", (both ? "DOUBLE_BOSS" : "BOSS") + ".description");
			if (both) { title.Add("BossName1", run.Act.BossEncounter.Title); title.Add("BossName2", run.Act.SecondBossEncounter!.Title); body.Add("BossName1", run.Act.BossEncounter.Title); body.Add("BossName2", run.Act.SecondBossEncounter.Title); }
			else { var boss = secondOnly ? run.Act.SecondBossEncounter : run.Act.BossEncounter; if (boss != null) { title.Add("BossName", boss.Title); body.Add("BossName", boss.Title); } }
			Tip(top.BossIcon, new IHoverTip[] { new HoverTip(title, body) });
			foreach (var holder in Descendants<NPotionHolder>(top.PotionContainer))
			{
				if (holder.Potion != null) Tip(holder, holder.Potion.Model.HoverTips);
				else StaticTip(holder, "POTION_SLOT");
			}
		}
		foreach (var holder in Descendants<NRelicInventoryHolder>(MegaCrit.Sts2.Core.Nodes.NRun.Instance.GlobalUi.RelicInventory).Where(n => n.IsVisibleInTree()))
		{
			var relic = holder.Relic.Model;
			var (hsv, color) = relic.Rarity switch
			{
				RelicRarity.Uncommon => (new[] { 0.426f, 0.8f, 1.1f }, StsColors.blue),
				RelicRarity.Rare => (new[] { 1f, 0.8f, 1.15f }, StsColors.gold),
				RelicRarity.Shop => (new[] { 0.525f, 2.5f, 0.85f }, StsColors.blue),
				RelicRarity.Event => (new[] { 0.23f, 0.75f, 0.9f }, StsColors.green),
				RelicRarity.Ancient => (new[] { 0.875f, 3f, 0.9f }, StsColors.red),
				_ => (new[] { 0.95f, 0.25f, 0.9f }, StsColors.cream)
			};
			snapshot.Relics.Add(new RelicSnapshot { Title = relic.Title.GetFormattedText(), Description = relic.DynamicDescription.GetFormattedText(), Flavor = relic.Flavor.GetFormattedText(), Icon = Path(relic.BigIcon), Rarity = new LocString("gameplay_ui", "RELIC_RARITY." + relic.Rarity.ToString().ToUpperInvariant()).GetFormattedText(), FrameHsv = hsv, RarityColor = new[] { color.R, color.G, color.B, color.A }, Rect = GlobalRect(holder), Tips = CaptureTips(relic.HoverTips), ExtraTips = CaptureTips(relic.HoverTipsExcludingRelic) });
		}
	}
	private IEnumerable<byte> CapturePiles(SpectatorSnapshot snapshot, CardPile[] piles, Control[] buttons)
	{
		for (int i = 0; i < piles.Length; i++)
		{
			var pile = piles[i]; string kind = pile.Type.ToString();
			var target = new PileSnapshot { Kind = kind, Rect = GlobalRect(buttons[i]) };
			if (WantPile == kind)
			{
				var cards = pile.Cards.ToList();
				// Native draw-pile screen deliberately hides draw order.
				if (pile.Type == PileType.Draw) cards.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.Compare(a.Id.Entry, b.Id.Entry, StringComparison.Ordinal));
				foreach (var card in cards) { target.Cards.Add(CaptureCard(card, pile.Type, null)); yield return 0; }
			}
			snapshot.Piles.Add(target);
		}
	}
}
