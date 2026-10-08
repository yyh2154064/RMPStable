using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class LocalSpectatorSource
{
	private SpectatorSnapshot? _previous;
	private string _observedState = "";
	internal bool WantDeck { get; set; }
	internal string WantPile { get; set; } = "";
	internal void RestorePreviewSource(string id) { if (_previewParticipants.Exists(p => p.Id == id)) _previewSourceId = id; }
	private string _previewSourceId = "";
	private readonly List<ParticipantSnapshot> _previewParticipants = new();
	internal bool SelectPreviewSource(string id)
	{
		if (id != _participant?.Id && !_previewParticipants.Exists(p => p.Id == id)) return false;
		_previewSourceId = id; _eventDirty = true; return true;
	}
	internal void AbortCapture() { _backgroundRoom = ""; _materialSamples.Clear(); }
	internal string PageToken() => $"{NRun.Instance?.GetInstanceId()}:{NCombatRoom.Instance?.GetInstanceId()}:{NMerchantRoom.Instance?.GetInstanceId()}:{NEventRoom.Instance?.GetInstanceId()}:{(NOverlayStack.Instance?.Peek() as Node)?.GetInstanceId()}:{(NCapstoneContainer.Instance?.CurrentCapstoneScreen as Node)?.GetInstanceId()}:{NMapScreen.Instance?.IsOpen}:{NMerchantRoom.Instance?.Inventory?.IsOpen}:{NGame.Instance?.InspectRelicScreen?.IsVisibleInTree()}:{(NModalContainer.Instance?.OpenModal as Node)?.GetInstanceId()}:{NRun.Instance?.GetViewportRect().Size}";
	private CardPile? _watchedDeck;
	private readonly HashSet<CardModel> _watchedCards = new();
	private bool _deckDirty = true, _eventDirty;
	private long _deckGeneration;
	private ulong _deckExpires;
	private string _deckRoom = "", _deckCulture = "";
	private List<CardSnapshot> _deckCache = new();
	internal void RequestDeck() { WantDeck = true; DeckChanged(); }
	private void DeckChanged() { _deckDirty = true; _eventDirty = true; _deckGeneration++; }
	private void BindDeck(CardPile deck)
	{
		if (_watchedDeck != deck)
		{
			if (_watchedDeck != null) _watchedDeck.ContentsChanged -= DeckChanged;
			_watchedDeck = deck; deck.ContentsChanged += DeckChanged; DeckChanged();
		}
		var live = new HashSet<CardModel>(deck.Cards);
		foreach (var old in new List<CardModel>(_watchedCards)) if (!live.Contains(old)) WatchCard(old, false);
		foreach (var card in live) if (!_watchedCards.Contains(card)) WatchCard(card, true);
	}
	private void WatchCard(CardModel card, bool on)
	{
		if (on)
		{
			card.Upgraded += DeckChanged; card.EnchantmentChanged += DeckChanged; card.EnergyCostChanged += DeckChanged;
			card.StarCostChanged += DeckChanged; card.KeywordsChanged += DeckChanged; card.Forged += DeckChanged;
			_watchedCards.Add(card);
		}
		else
		{
			card.Upgraded -= DeckChanged; card.EnchantmentChanged -= DeckChanged; card.EnergyCostChanged -= DeckChanged;
			card.StarCostChanged -= DeckChanged; card.KeywordsChanged -= DeckChanged; card.Forged -= DeckChanged;
			_watchedCards.Remove(card);
		}
	}
	public void Dispose()
	{
		UnbindAnimations();
		ClearParticles();
		SetControlMode(false); _controlBindings.Clear(); _controlCardIds.Clear();
		if (_watchedDeck != null) _watchedDeck.ContentsChanged -= DeckChanged;
		foreach (var card in new List<CardModel>(_watchedCards)) WatchCard(card, false);
		_watchedDeck = null; _previous = null; _deckCache = new(); _upgrades.Clear(); _paths.Clear(); _materialSamples.Clear();
	}
	private static List<T> Reuse<T>(List<T> value, List<T>? previous, Func<T, T, bool> equal)
		=> SnapshotEquality.List(value, previous, equal) ? previous! : value;

	// Finished DTOs are read-only to consumers. Reuse exactly equal domains;
	// never edit any object from the previous capture. Revisions are independent.
	private SpectatorSnapshot Freeze(SpectatorSnapshot s)
	{
		var p = _previous;
		s.Background = Reuse(s.Background, p?.Background, SnapshotEquality.Equal);
		s.BackgroundLabels = Reuse(s.BackgroundLabels, p?.BackgroundLabels, SnapshotEquality.Equal);
		s.ForegroundArt = Reuse(s.ForegroundArt, p?.ForegroundArt, SnapshotEquality.Equal);
		s.ForegroundLabels = Reuse(s.ForegroundLabels, p?.ForegroundLabels, SnapshotEquality.Equal);
		s.Sources = Reuse(s.Sources, p?.Sources, SnapshotEquality.Equal);
		s.HudArt = Reuse(s.HudArt, p?.HudArt, SnapshotEquality.Equal);
		s.HudLabels = Reuse(s.HudLabels, p?.HudLabels, SnapshotEquality.Equal);
		s.HudHovers = Reuse(s.HudHovers, p?.HudHovers, SnapshotEquality.Equal);
		s.Relics = Reuse(s.Relics, p?.Relics, SnapshotEquality.Equal);
		s.Piles = Reuse(s.Piles, p?.Piles, SnapshotEquality.Equal);
		s.ModalArt = Reuse(s.ModalArt, p?.ModalArt, SnapshotEquality.Equal);
		s.ModalLabels = Reuse(s.ModalLabels, p?.ModalLabels, SnapshotEquality.Equal);
		s.ModalCards = Reuse(s.ModalCards, p?.ModalCards, SnapshotEquality.Equal);
		s.Creatures = Reuse(s.Creatures, p?.Creatures, SnapshotEquality.Equal);
		s.Cards = Reuse(s.Cards, p?.Cards, SnapshotEquality.Equal);
		s.Deck = Reuse(s.Deck, p?.Deck, SnapshotEquality.Equal);
		s.Inventory = Reuse(s.Inventory, p?.Inventory, SnapshotEquality.Equal);
		s.Items = Reuse(s.Items, p?.Items, SnapshotEquality.Equal);
		s.Choices = Reuse(s.Choices, p?.Choices, SnapshotEquality.Equal);
		s.PageArt = Reuse(s.PageArt, p?.PageArt, SnapshotEquality.Equal);
		s.PageLabels = Reuse(s.PageLabels, p?.PageLabels, SnapshotEquality.Equal);
		s.Hovers = Reuse(s.Hovers, p?.Hovers, SnapshotEquality.Equal);
		s.Drawings = Reuse(s.Drawings, p?.Drawings, SnapshotEquality.Equal);
		s.CombatHudArt = Reuse(s.CombatHudArt, p?.CombatHudArt, SnapshotEquality.Equal);
		s.CombatHudLabels = Reuse(s.CombatHudLabels, p?.CombatHudLabels, SnapshotEquality.Equal);
		s.UnderlayCards = Reuse(s.UnderlayCards, p?.UnderlayCards, SnapshotEquality.Equal);
		s.UnderlayItems = Reuse(s.UnderlayItems, p?.UnderlayItems, SnapshotEquality.Equal);
		s.RewardArt = Reuse(s.RewardArt, p?.RewardArt, SnapshotEquality.Equal);
		s.RewardLabels = Reuse(s.RewardLabels, p?.RewardLabels, SnapshotEquality.Equal);
		bool Same(object a, object? b) => ReferenceEquals(a, b);
		if (p != null && SnapshotEquality.Equal(s.Control, p.Control)) s.Control = p.Control;
		long Next(long? revision, bool same) => (revision ?? 0) + (same ? 0 : 1);
		s.Revisions = new SpectatorRevisions
		{
			Hud = Next(p?.Revisions.Hud, Same(s.HudArt, p?.HudArt) && Same(s.HudLabels, p?.HudLabels) && Same(s.HudHovers, p?.HudHovers) && Same(s.Relics, p?.Relics) && Same(s.Piles, p?.Piles)),
			Background = Next(p?.Revisions.Background, s.Room == p?.Room && Same(s.Background, p?.Background) && Same(s.BackgroundLabels, p?.BackgroundLabels)),
			Creatures = Next(p?.Revisions.Creatures, Same(s.Creatures, p?.Creatures)),
			Screen = Next(p?.Revisions.Screen, s.Page == p?.Page && Same(s.PageLabels, p?.PageLabels) && Same(s.Hovers, p?.Hovers) && Same(s.CombatHudLabels, p?.CombatHudLabels)),
			Inventory = Next(p?.Revisions.Inventory, Same(s.Inventory, p?.Inventory)),
			Offers = Next(p?.Revisions.Offers, s.Page == p?.Page && Same(s.Items, p?.Items) && Same(s.Choices, p?.Choices)),
			Deck = Next(p?.Revisions.Deck, s.Culture == p?.Culture && Same(s.Deck, p?.Deck)),
			Sources = Next(p?.Revisions.Sources, s.SourceId == p?.SourceId && Same(s.Sources, p?.Sources)),
			Cards = Next(p?.Revisions.Cards, s.Page == p?.Page && s.UnderlayPage == p?.UnderlayPage && Same(s.Cards, p?.Cards) && Same(s.UnderlayCards, p?.UnderlayCards) && Same(s.UnderlayItems, p?.UnderlayItems) && Same(s.RewardArt, p?.RewardArt) && Same(s.RewardLabels, p?.RewardLabels))
		};
		_previous = s;
		return s;
	}

	// Cheap state notification, backed by periodic full capture. No scene-tree
	// traversal here. The poll catches modifiers/text changes not present here.
	internal bool Observe(RunState run)
	{
		var p = run.Players[0]; var c = p.PlayerCombatState;
		var overlay = NOverlayStack.Instance?.Peek() as Node;
		var capstone = NCapstoneContainer.Instance?.CurrentCapstoneScreen as Node;
		string state = $"{NRun.Instance.GetInstanceId()}:{run.CurrentRoom?.GetType().Name}:{overlay?.GetInstanceId()}:{capstone?.GetInstanceId()}:{NMapScreen.Instance?.IsOpen}:{NMerchantRoom.Instance?.Inventory?.IsOpen}:{p.Creature.CurrentHp}:{p.Creature.Block}:{p.Gold}:{p.Deck.Cards.Count}:{c?.Energy}:{c?.Stars}:{c?.Hand.Cards.Count}:{c?.DrawPile.Cards.Count}:{c?.DiscardPile.Cards.Count}:{c?.TurnNumber}";
		bool changed = state != _observedState || _eventDirty; _eventDirty = false; _observedState = state; return changed;
	}

	internal MapMotionFrame? CaptureMapDrawings()
	{
		if (_previous?.Page != "map" || NMapScreen.Instance?.IsOpen != true) return null;
		_drawingSamples.Clear(); _drawingCapture = true;
		try
        {
            var map = NMapScreen.Instance.GetNode<Control>("TheMap");
            return new MapMotionFrame { Session = _previous.Session, SourceId = _previous.SourceId, Key = map.GetInstanceId().ToString(), Transform = Transform(map.GetGlobalTransform()), Drawings = CaptureDrawings(NMapScreen.Instance.Drawings) };
        }
		finally { _drawingCapture = false; }
	}
}
