using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.addons.mega_text;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// This renderer must not access RunManager, Player, CardModel, or any game command.
// Native NCard instances intentionally have Model == null for their entire lifetime.
internal sealed partial class SpectatorView : IDisposable
{
	private readonly CanvasLayer _overlay;
	private readonly Control _panel;
	private readonly SubViewport _viewport;
	private readonly Control _canvas;
	private readonly Control _background;
	private readonly Control _actors;
	private readonly Control _actorSprites;
	private readonly Control _actorState;
	private readonly Control _inventory;
	private readonly Control _offers;
	private readonly Control _preview;
	private readonly Control _cards;
	private readonly Control _page;
	private readonly Control _hud;
	private readonly Control _underlay;
	private readonly Control _screen;
	private readonly Control _inspect;
	private readonly Control _hudHovers, _pileTargets, _browse, _modal;
	internal string BrowsePile { get; private set; } = "";
	private string _browseKey = "";
	private readonly Control _combatHud;
	private readonly Control _combatHudText;
	private readonly Control _screenArt;
	private readonly Control _screenText;
	private readonly Control _screenHovers;
	private readonly Control _screenDrawings;
	private readonly ColorRect _header;
	private readonly Label _title;
	private readonly Label _summary;
	private readonly Label _detail;
	private readonly Label _status;
	private readonly Button _deck;
	private SpectatorSnapshot? _snapshot;
	private bool _showDeck;
	private string _roomKey = "", _actorKey = "", _cardKey = "", _itemKey = "", _offerKey = "", _hudKey = "";
	private string _screenKey = "", _sourcePage = "";
	private string _revisionSession = "";
	private readonly List<(Node2D Holder, Node2D Hitbox, CardSnapshot Card)> _handNodes = new();
	private readonly List<(Node2D Holder, Node2D Hitbox, CardSnapshot Card)> _underlayHandNodes = new();
	private readonly HashSet<string> _reportedAssets = new();

	internal bool WantsDeck => _showDeck || _snapshot?.Page == "deck";
	internal SpectatorView(Action close, Action? requestDeck = null, Action<string>? selectSource = null, Func<bool, long>? setControl = null, Func<SpectatorCommand, SpectatorCommandResult>? executeCommand = null)
	{
		_setControl = setControl; _executeCommand = executeCommand;
		_selectSource = selectSource;
		(_overlay, _panel, _viewport) = CreatePanel(close);
		try
		{
		_canvas = Area(_viewport, Vector2.Zero, new Vector2(1920, 1080));
		Solid(_canvas, new Rect2(0, 0, 1920, 1080), new Color("10151f"));
		_page = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		_background = Area(_page, Vector2.Zero, _page.Size);
		_actors = Area(_page, Vector2.Zero, _page.Size);
		_actorSprites = Area(_actors, Vector2.Zero, _page.Size);
		_actorState = Area(_actors, Vector2.Zero, _page.Size);
		_underlay = Area(_page, Vector2.Zero, _page.Size);
		_combatHud = Area(_page, Vector2.Zero, _page.Size);
		_combatHudText = Area(_page, Vector2.Zero, _page.Size);
		_offers = Area(_page, Vector2.Zero, _page.Size);
		_screen = Area(_page, Vector2.Zero, _page.Size);
		_screenArt = Area(_screen, Vector2.Zero, _page.Size);
		_screenDrawings = Area(_screen, Vector2.Zero, _page.Size);
		_screenText = Area(_screen, Vector2.Zero, _page.Size);
		_screenHovers = Area(_screen, Vector2.Zero, _page.Size);
		_cards = Area(_page, Vector2.Zero, _page.Size);
		_hud = Area(_page, Vector2.Zero, _page.Size);
		// Small spectator controls sit over the native composition rather than
		// squeezing combat, rewards, and the merchant into a dashboard layout.
		_header = new ColorRect { Size = new Vector2(1920, 74), Color = new Color(0.04f, 0.035f, 0.03f, 0.92f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_canvas.AddChild(_header);
		_title = Text(_canvas, "", new Rect2(24, 9, 320, 38), 27);
		_summary = Text(_canvas, "", new Rect2(340, 9, 1230, 38), 27);
		_detail = Text(_canvas, "", new Rect2(24, 760, 260, 290), 24);
		_inventory = Area(_canvas, new Vector2(24, 83), new Vector2(1840, 58));
		_deck = new Button { Text = T("查看牌组", "View deck"), Position = new Vector2(1665, 12), Size = new Vector2(230, 45), FocusMode = Control.FocusModeEnum.None };
		_canvas.AddChild(_deck);
		_deck.Pressed += () => { BrowsePile = ""; _browseKey = ""; CloseInspect(); _showDeck = !_showDeck; _cardKey = ""; if (_showDeck) requestDeck?.Invoke(); if (_snapshot != null) Update(_snapshot); };
		_status = Text(_canvas, "", new Rect2(24, 1055, 1840, 25), 18);
		_hudHovers = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		_pileTargets = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		_browse = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		_preview = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		_inspect = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		_modal = Area(_canvas, Vector2.Zero, new Vector2(1920, 1080));
		CreatePointer();
		CreateControlLayer();
		}
		catch { _overlay.QueueFree(); throw; }
	}

	internal void Update(SpectatorSnapshot snapshot)
	{
		if (snapshot.Schema != 1) throw new InvalidOperationException("Unsupported spectator snapshot version.");
		string revisionSession = snapshot.Session + ":" + snapshot.SourceId;
		if (_revisionSession != revisionSession)
		{ _revisionSession = revisionSession; SetControlEnabled(false); CloseInspect(); _showDeck = false; BrowsePile = ""; _browseKey = ""; _hudKey = _roomKey = _actorKey = _itemKey = _offerKey = _cardKey = _screenKey = ""; }
		UpdateSources(snapshot.Sources, snapshot.SourceId);
		if (_sourcePage != snapshot.Page) { CloseInspect(); _showDeck = false; BrowsePile = ""; _browseKey = ""; _cardKey = ""; _sourcePage = snapshot.Page; }
		_snapshot = snapshot;
		UpdatePointer(snapshot.Pointer);
		_deck.Disabled = false;
		_title.Text = snapshot.Character + "  ·  " + (_controlEnabled ? T("本机控制", "Local control") : T("只读观战", "Spectator"));
		_summary.Text = snapshot.Summary;
		_detail.Text = snapshot.Page == "combat" ? snapshot.Detail.Replace("   ", "\n") : "";
		_status.Text = T("单人本地测试 · 悬停放大卡牌 · 在游戏窗口按观战快捷键开关", "Local test · hover to enlarge cards · toggle the hotkey in the game window");
		_deck.Text = _showDeck ? T("返回当前页面", "Current page") : T("查看牌组", "View deck");
		_page.Scale = new Vector2(1920 / Math.Max(1, snapshot.Width), 1080 / Math.Max(1, snapshot.Height));
		_page.Size = new Vector2(snapshot.Width, snapshot.Height);
		_hudHovers.Scale = _pileTargets.Scale = _browse.Scale = _modal.Scale = _page.Scale;
		UpdateControl(snapshot);
		_hudHovers.Size = _pileTargets.Size = _browse.Size = _modal.Size = _page.Size;
		RetainArt(_modal, snapshot.ModalArt); RetainLabels(_modal, snapshot.ModalLabels);
		_modal.MouseFilter = snapshot.ModalArt.Count > 0 ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		if (snapshot.ModalArt.Count > 0) { CloseInspect(); BrowsePile = ""; _browseKey = ""; }
		UpdatePileBrowser(snapshot);
		_pileTargets.Visible = snapshot.Page == "combat" && !_showDeck && BrowsePile.Length == 0;
		foreach (var pane in new[] { _cards, _screen, _underlay, _background, _actors, _offers, _hud }) pane.Size = _page.Size;
		bool nativeHud = snapshot.HudArt.Count > 0;
		_header.Visible = _title.Visible = _summary.Visible = _inventory.Visible = !nativeHud;
		_detail.Visible = !nativeHud;
		_deck.Flat = nativeHud;
		_deck.Text = nativeHud ? "" : (_showDeck ? T("返回当前页面", "Current page") : T("查看牌组", "View deck"));
		_deck.TooltipText = _showDeck ? T("返回当前页面", "Current page") : T("查看牌组（只读）", "View deck (read only)");
		if (nativeHud && snapshot.DeckButtonRect != null) { var rect = Rectangle(snapshot.DeckButtonRect); _deck.Position = rect.Position * _page.Scale; _deck.Size = rect.Size * _page.Scale; }
		string hudKey = snapshot.Revisions.Hud.ToString();
		if (_hudKey != hudKey) { _hudKey = hudKey; RetainArt(_hud, snapshot.HudArt); RetainLabels(_hud, snapshot.HudLabels); }
		RetainHudInteractions(snapshot);
		string roomKey = snapshot.Revisions.Background.ToString();
		if (_roomKey != roomKey)
		{
			_roomKey = roomKey;
			Clear(_background);
			DrawBackground(snapshot);
		}
		string actorKey = snapshot.Revisions.Creatures.ToString();
		RetainCreatureArt(snapshot.Creatures);
		if (_actorKey != actorKey)
		{
			_actorKey = actorKey;
			RetainCreatureState(snapshot.Creatures);
		}
		_actors.Visible = snapshot.Creatures.Count > 0;
		bool screenRebuilt = RetainArt(_screenArt, snapshot.PageArt);
		RetainArt(_combatHud, snapshot.CombatHudArt);
		UpdateDrawings(snapshot.Drawings);
		string screenKey = snapshot.Revisions.Screen.ToString();
		if (_screenKey != screenKey || screenRebuilt)
		{ _screenKey = screenKey; RetainLabels(_screenText, snapshot.PageLabels, nativeOrder: true); RetainLabels(_combatHudText, snapshot.CombatHudLabels); Clear(_screenHovers); DrawHovers(snapshot.Hovers); }
		string itemKey = snapshot.Revisions.Inventory.ToString();
		if (_itemKey != itemKey)
		{
			_itemKey = itemKey; Clear(_inventory);
			DrawItems(_inventory, snapshot.Inventory, false);
		}
		string offerKey = snapshot.Revisions.Offers.ToString();
		if (_offerKey != offerKey)
		{
			_offerKey = offerKey; Clear(_offers);
			if (snapshot.Page == "shop") DrawItems(_offers, snapshot.Items, true);
		}
		var cards = _showDeck || snapshot.Page == "deck" ? snapshot.Deck : snapshot.Cards;
		string cardKey = _showDeck + snapshot.Page + ":" + (_showDeck || snapshot.Page == "deck" ? snapshot.Revisions.Deck : snapshot.Revisions.Cards);
		if (_cardKey != cardKey || screenRebuilt)
		{
			if ((!screenRebuilt || snapshot.Page == "map") && TryMoveHand(snapshot, cards)) { _cardKey = cardKey; return; }
			_handNodes.Clear(); _underlayHandNodes.Clear();
			_cardKey = cardKey; ClearMounted(_mountedCards); Clear(_preview); Clear(_cards); Clear(_underlay);
			if (snapshot.Page is not "combat" and not "shop" && snapshot.UnderlayPage.Length > 0 && !_showDeck)
			{
				var basePage = new SpectatorSnapshot { Width = snapshot.Width, Height = snapshot.Height, Page = snapshot.UnderlayPage };
				DrawPageCards(basePage, snapshot.UnderlayCards, _underlay);
				DrawItems(_underlay, snapshot.UnderlayItems, true);
			}
			DrawPageCards(snapshot, cards);
		}
	}

	internal void ShowError(string message)
	{
		_snapshot = null; _deck.Disabled = true;
		ClearMounted(_mountedCards); ClearMounted(_mountedLabels);
		Clear(_preview); Clear(_cards); Clear(_actorSprites); Clear(_actorState); Clear(_offers); Clear(_inventory); Clear(_background); Clear(_hud); Clear(_underlay); Clear(_hudHovers); Clear(_pileTargets); Clear(_browse); Clear(_modal); BrowsePile = ""; _browseKey = "";
		foreach (var pane in new[] { _screenArt, _screenDrawings, _screenText, _screenHovers, _combatHud, _combatHudText }) Clear(pane);
		_retainedArt.Clear(); _retainedLabels.Clear(); _creatureSprites.Clear(); _creatureStates.Clear(); _drawingSurfaces.Clear(); CloseInspect();
		_interactionHovers = null; _interactionRelics = null; _interactionPiles = null;
		_handNodes.Clear(); _underlayHandNodes.Clear();
		_cardKey = _actorKey = _offerKey = _itemKey = _roomKey = _hudKey = _screenKey = "";
		_header.Visible = _title.Visible = _summary.Visible = true;
		_status.Text = T("状态暂不可用：", "State unavailable: ") + message;
		_summary.Text = T("等待有效的单人游戏状态", "Waiting for a valid singleplayer state");
	}

	private void DrawPageCards(SpectatorSnapshot snapshot, List<CardSnapshot> cards, Control? destination = null)
	{
		var cardRoot = destination ?? _cards;
		bool deck = _showDeck || snapshot.Page is "run" or "deck";
		if (deck)
		{
			DrawDeck(snapshot, cards, cardRoot);
			return;
		}
		if (snapshot.Page == "reward")
		{
			if (snapshot.RewardArt.Count > 0) { DrawArt(cardRoot, snapshot.RewardArt); DrawLabels(cardRoot, snapshot.RewardLabels); }
			else { Solid(cardRoot, new Rect2(0, 74, snapshot.Width, snapshot.Height - 74), new Color(0.01f, 0.01f, 0.015f, 0.8f)); Text(cardRoot, T("选择一张卡牌", "Choose a card"), new Rect2(0, 195, snapshot.Width, 70), 52, HorizontalAlignment.Center); }
		}
		for (int i = 0; i < cards.Count; i++)
		{
			var card = cards[i];
			Transform2D transform;
			if (card.Transform != null) transform = Matrix(card.Transform);
			else
			{
				float scale = snapshot.Page == "reward" ? 1f : 0.65f;
				var center = snapshot.Page == "shop" ? new Vector2(380 + i % 5 * 250, i < 5 ? 355 : 790) : new Vector2(snapshot.Width * 0.5f + (i - (cards.Count - 1) * 0.5f) * 320 * scale, snapshot.Page == "reward" ? snapshot.Height * 0.54f : snapshot.Height - 160);
				transform = new Transform2D(0, Vector2.One * scale, 0, center);
			}
			var holder = new Node2D { Transform = transform };
			Node drawingParent = cardRoot;
			if (destination == null && PageAnchor(card.ArtKey) is { } nativeAnchor && snapshot.Page is "event" or "selection")
			{
				var nativeTransform = snapshot.PageArt.First(a => a.Key == card.ArtKey).Transform;
				holder.Transform = Matrix(nativeTransform).AffineInverse() * transform;
				drawingParent = nativeAnchor; _mountedCards.Add(holder);
			}
			drawingParent.AddChild(holder);
			DrawCard(holder, card, Vector2.Zero, 1, centered: true);
			var hitboxHolder = new Node2D { Transform = transform }; cardRoot.AddChild(hitboxHolder);
			if (destination == null && snapshot.Page == "combat") _handNodes.Add((holder, hitboxHolder, card));
			else if (destination == _underlay && snapshot.Page == "combat") _underlayHandNodes.Add((holder, hitboxHolder, card));
			var hitbox = Area(hitboxHolder, new Vector2(-160, -230), new Vector2(320, 450));
			hitbox.MouseFilter = Control.MouseFilterEnum.Stop;
			Hover(hitbox, card, cards);
			if (card.Art.Count > 0) { DrawArt(cardRoot, card.Art); DrawLabels(cardRoot, card.Labels); }
			else if (card.Price.HasValue) Text(holder, card.Sold ? T("已售出", "Sold out") : $"{card.Price} G", new Rect2(-150, 225, 300, 55), 44, HorizontalAlignment.Center);
		}
	}
	private void Hover(Control slot, CardSnapshot card, List<CardSnapshot> cards, int? index = null, bool upgraded = false)
	{
		slot.MouseEntered += () =>
		{
			if (_inspectIndex >= 0 || _relicIndex >= 0) return;
			Clear(_preview);
			var center = slot.GetGlobalRect().GetCenter();
			bool hand = !_showDeck && BrowsePile.Length == 0 && _snapshot?.Page == "combat";
			var origin = new Vector2(Math.Clamp(center.X - 180, 24, 1536), hand ? 1080 - 422 * 1.2f : Math.Clamp(center.Y - 254, 150, 520));
			DrawCard(_preview, card, origin, 1.2f);
			DrawTips(_preview, card.Tips, new Vector2(origin.X > 1160 ? origin.X - 380 : origin.X + 370, origin.Y));
		};
		slot.MouseExited += () => { if (_inspectIndex < 0) Clear(_preview); };
		slot.GuiInput += input =>
		{
			if (HandleControlCard(slot, card, input)) return;
			if (input is InputEventMouseButton { Pressed: true } click && (click.ButtonIndex == MouseButton.Left || _controlEnabled && click.ButtonIndex == MouseButton.Right)) { OpenInspect(cards, index ?? cards.IndexOf(card), upgraded); slot.AcceptEvent(); }
		};
	}
	private bool TryMoveHand(SpectatorSnapshot snapshot, List<CardSnapshot> cards)
	{
		var nodes = _handNodes;
		if (_showDeck) return false;
		if (snapshot.Page == "map" && cards.Count == 0 && snapshot.UnderlayPage == "combat" && snapshot.UnderlayItems.Count == 0)
		{ nodes = _underlayHandNodes; cards = snapshot.UnderlayCards; }
		else if (snapshot.Page != "combat") return false;
		if (nodes.Count != cards.Count) return false;
		for (int i = 0; i < cards.Count; i++)
			if (cards[i].Transform == null || !SnapshotEquality.SameCardFace(nodes[i].Card, cards[i])) return false;
		for (int i = 0; i < cards.Count; i++)
		{
			var entry = nodes[i]; var transform = Matrix(cards[i].Transform!);
			entry.Holder.Transform = transform; entry.Hitbox.Transform = transform;
			nodes[i] = (entry.Holder, entry.Hitbox, cards[i]);
		}
		return true;
	}
	private static Transform2D Matrix(float[] t) => new(new Vector2(t[0], t[1]), new Vector2(t[2], t[3]), new Vector2(t[4], t[5]));
	private static Rect2 Rectangle(float[] r) => new(r[0], r[1], r[2], r[3]);

	private void DrawBackground(SpectatorSnapshot snapshot)
		=> DrawArt(_background, snapshot.Background);

	private List<ArtNode> DrawArt(Node parent, List<ArtSnapshot> layers, Node2D? retainedRoot = null, Dictionary<string, ArtNode>? existing = null)
	{
		var rendered = new List<ArtNode>();
		var root = retainedRoot ?? new Node2D();
		if (root.GetParent() == null) parent.AddChild(root);
		var ancestors = new Dictionary<string, (Node Node, Transform2D Matrix)>();
		foreach (var art in layers)
		{
			var matrix = Matrix(art.Transform); var r = art.Rect;
			var ancestor = ancestors.TryGetValue(art.Parent, out var found) ? found : (Node: (Node)root, Matrix: Transform2D.Identity);
			if (existing != null && existing.TryGetValue(art.Key, out var old) && SameArtStructure(old.Snapshot, art))
			{
				if (old.Holder.GetParent() != ancestor.Node) { old.Holder.GetParent()?.RemoveChild(old.Holder); ancestor.Node.AddChild(old.Holder); }
				ancestor.Node.MoveChild(old.Holder, ancestor.Node.GetChildCount() - 1);
				UpdateArtNode(old, art, ancestor.Matrix.AffineInverse() * matrix);
				rendered.Add(old);
				if (art.Key.Length > 0) ancestors[art.Key] = (old.Drawing, matrix * new Transform2D(0, new Vector2(r[0], r[1])));
				continue;
			}
			var holder = new Node2D { Transform = ancestor.Matrix.AffineInverse() * matrix, Modulate = ColorOf(art.Tint), ZIndex = art.Z, ZAsRelative = art.ZRelative, ShowBehindParent = art.BehindParent };
			ancestor.Node.AddChild(holder);
			CanvasItem drawing;
			var texture = Asset<Texture2D>(art.Texture);
			if (art.Region is { Length: 4 } region && texture != null) texture = new AtlasTexture { Atlas = texture, Region = Rectangle(region) };
			if (art.Points != null)
			{
				var points = Enumerable.Range(0, art.Points.Length / 2).Select(i => new Vector2(art.Points[i * 2], art.Points[i * 2 + 1])).ToArray();
				drawing = art.Polygon ? new Polygon2D { Polygon = points, Texture = texture } : new Line2D { Points = points, Width = art.LineWidth, DefaultColor = Colors.White, BeginCapMode = (Line2D.LineCapMode)art.BeginCap, EndCapMode = (Line2D.LineCapMode)art.EndCap, JointMode = (Line2D.LineJointMode)art.Joint, Antialiased = art.Antialiased };
			}
			else if (art.Skeleton.Length > 0)
			{
				var skeleton = Asset<Resource>(art.Skeleton);
				if (skeleton == null) continue;
				// Rebuild only the native drawing node. No source scripts, signals,
				// children, merchant logic, or gameplay objects are duplicated.
				var spine = ClassDB.Instantiate("SpineSprite").As<Node2D>();
				spine.Set("skeleton_data_res", skeleton);
				drawing = spine;
				if (art.Animation.Length > 0) Callable.From(() => { if (GodotObject.IsInstanceValid(spine)) new MegaSprite(spine).GetAnimationState().SetAnimation(art.Animation); }).CallDeferred();
			}
			else if (art.PatchMargins is { Length: 4 } margins)
			{
				drawing = new NinePatchRect { PatchMarginLeft = margins[0], PatchMarginTop = margins[1], PatchMarginRight = margins[2], PatchMarginBottom = margins[3], Texture = texture, Size = new Vector2(r[2], r[3]) };
			}
			else if (art.Group) drawing = new Control { Size = new Vector2(r[2], r[3]) };
			else if (art.Solid) drawing = new ColorRect { Size = new Vector2(r[2], r[3]), Color = Colors.White };
			else drawing = new TextureRect
			{
				// Set expansion before the texture/size: otherwise Godot clamps
				// the requested native size to the asset's minimum dimensions.
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				Texture = texture, Size = new Vector2(r[2], r[3]),
				StretchMode = (TextureRect.StretchModeEnum)art.Stretch,
				FlipH = art.FlipH, FlipV = art.FlipV,
			};
			drawing.SelfModulate = ColorOf(art.SelfTint); drawing.Material = SnapshotMaterial(art);
			drawing.ClipChildren = (CanvasItem.ClipChildrenMode)art.ClipChildren;
			if (drawing is Control control) { control.Position = new Vector2(r[0], r[1]); control.ClipContents = art.ClipContents; control.MouseFilter = Control.MouseFilterEnum.Ignore; }
			holder.AddChild(drawing);
			rendered.Add(new ArtNode(art.Key, holder, drawing, art));
			if (art.Key.Length > 0) ancestors[art.Key] = (drawing, matrix * new Transform2D(0, new Vector2(r[0], r[1])));
		}
		return rendered;
	}
	private static Color ColorOf(float[] c) => new(c[0], c[1], c[2], c[3]);

	private void DrawCreatureArt(List<CreatureSnapshot> creatures, Node? destination = null)
	{
		foreach (var c in creatures)
		{
			var holder = new Node2D { Transform = Matrix(c.Transform) };
			(destination ?? _actorSprites).AddChild(holder);
			var packed = Asset<PackedScene>(c.VisualScene);
			if (packed != null)
			{
				NCreatureVisuals? visuals = null;
				try
				{
					// Only the art prefab, never NCreature or its gameplay subscriptions.
					visuals = packed.Instantiate<NCreatureVisuals>();
					holder.AddChild(visuals);
					visuals.Position = Vector2.Zero;
					visuals.Scale = Vector2.One;
					if (visuals.HasSpineAnimation) visuals.SpineAnimation.SetAnimation(c.Animation);
					DisableInput(visuals);
				}
				catch (Exception ex) { visuals?.QueueFree(); Report(c.VisualScene, ex.Message); }
			}
		}
	}
	private void DrawCreatures(List<CreatureSnapshot> creatures, Node? destination = null)
	{
		var parent = destination ?? _actorState;
		foreach (var c in creatures)
		{
			var intents = Rectangle(c.IntentRect);
			for (int j = 0; j < c.Intents.Count; j++)
			{
				var intent = c.Intents[j];
				if (intent.Art.Count > 0) { DrawArt(parent, intent.Art); DrawLabels(parent, intent.Labels); continue; }
				float ix = intents.Position.X + intents.Size.X * 0.5f + (j - c.Intents.Count * 0.5f) * 95;
				Icon(parent, intent.Icon, new Rect2(ix, intents.Position.Y, 70, 70));
				Rich(parent, intent.Text, new Rect2(ix + 52, intents.Position.Y + 42, 100, 44), 32);
			}
			DrawArt(parent, c.StateArt);
			DrawLabels(parent, c.StateLabels);
		}
	}

	private void DrawItems(Control parent, List<ItemSnapshot> items, bool merchant)
	{
		float spacing = merchant ? Math.Min(220, 1548f / Math.Max(1, items.Count)) : 66;
		for (int i = 0; i < items.Count; i++)
		{
			var item = items[i];
			if (merchant && item.Art.Count > 0)
			{
				DrawArt(parent, item.Art); DrawLabels(parent, item.Labels);
				if (item.Rect != null) { var rect = Rectangle(item.Rect); var hover = Area(parent, rect.Position, rect.Size); hover.MouseFilter = Control.MouseFilterEnum.Stop; hover.TooltipText = item.Name + (item.Sold ? T("（已售出）", " (sold)") : ""); }
				continue;
			}
			int columns = Math.Max(1, (int)(parent.Size.X / spacing));
			float x = i % columns * spacing, y = i / columns * (merchant ? 165 : 62);
			var box = Area(parent, new Vector2(x, y), new Vector2(spacing - 4, merchant ? 160 : 58));
			box.MouseFilter = Control.MouseFilterEnum.Stop; box.TooltipText = item.Name + " " + item.Text;
			if (merchant)
			{
				var rect = item.Rect == null ? new Rect2(660 + i * 150, 700, 112, 112) : Rectangle(item.Rect);
				box.Position = rect.Position; box.Size = rect.Size;
				if (item.Icon.Length > 0) Icon(box, item.Icon, new Rect2((rect.Size - new Vector2(90, 90)) * 0.5f, new Vector2(90, 90)));
				else Text(box, item.Name, new Rect2(Vector2.Zero, rect.Size), 26, HorizontalAlignment.Center);
				Text(box, item.Sold ? T("已售出", "Sold out") : $"{item.Price} G", new Rect2(-30, rect.Size.Y + 4, rect.Size.X + 60, 45), 32, HorizontalAlignment.Center);
			}
			else
			{
				Icon(box, item.Icon, new Rect2(0, 0, 48, 48));
				if (item.Text.Length > 0) Text(box, item.Text, new Rect2(27, 25, 34, 27), 21);
			}
		}
	}

	private List<Node2D> DrawLabels(Node parent, List<TextSnapshot> labels, bool nativeOrder = false)
	{
		var rendered = new List<Node2D>();
		foreach (var text in labels)
		{
			var holder = new Node2D { Transform = Matrix(text.Transform) };
			Node destination = parent; var color = text.Color;
			if (nativeOrder && PageAnchor(text.ArtKey) is { } anchor) { destination = anchor; holder.Transform = Transform2D.Identity; color = text.LocalColor; _mountedLabels.Add(holder); }
			destination.AddChild(holder);
			rendered.Add(holder);
			if (text.Rich)
			{
				var rich = new MegaRichTextLabel { Size = new Vector2(text.Size[0], text.Size[1]), HorizontalAlignment = (HorizontalAlignment)text.Alignment, VerticalAlignment = (VerticalAlignment)text.VerticalAlignment, AutowrapMode = (TextServer.AutowrapMode)text.WrapMode, BbcodeEnabled = true, ScrollActive = false, AutoSizeEnabled = false, MouseFilter = Control.MouseFilterEnum.Ignore };
				var richFont = Asset<Font>(text.Font) ?? ThemeDB.FallbackFont;
				foreach (var key in new[] { "normal_font", "bold_font", "italics_font" }) rich.AddThemeFontOverride(key, richFont);
				foreach (var key in ThemeConstants.RichTextLabel.AllFontSizes) rich.AddThemeFontSizeOverride(key, text.FontSize);
				rich.AddThemeColorOverride("default_color", ColorOf(color));
				rich.AddThemeColorOverride("font_outline_color", new Color(text.OutlineColor[0], text.OutlineColor[1], text.OutlineColor[2], text.OutlineColor[3]));
				rich.AddThemeConstantOverride("outline_size", text.OutlineSize);
				holder.AddChild(rich); rich.SetTextAutoSize(text.Text); continue;
			}
			var label = new Label { Text = text.Text, Size = new Vector2(text.Size[0], text.Size[1]), HorizontalAlignment = (HorizontalAlignment)text.Alignment, VerticalAlignment = (VerticalAlignment)text.VerticalAlignment, MouseFilter = Control.MouseFilterEnum.Ignore };
			var font = Asset<Font>(text.Font); if (font != null) label.AddThemeFontOverride("font", font);
			label.AddThemeFontSizeOverride("font_size", text.FontSize);
			label.AddThemeColorOverride("font_color", ColorOf(color));
			label.AddThemeColorOverride("font_outline_color", new Color(text.OutlineColor[0], text.OutlineColor[1], text.OutlineColor[2], text.OutlineColor[3]));
			label.AddThemeConstantOverride("outline_size", text.OutlineSize);
			holder.AddChild(label);
		}
		return rendered;
	}

	private void DrawCard(Node parent, CardSnapshot c, Vector2 position, float scale, bool centered = false)
	{
		if (c.Sold || c.Id.Length == 0)
		{
			Text(parent, c.Title, new Rect2(position, new Vector2(300, 422) * scale), 23, HorizontalAlignment.Center);
			return;
		}
		var packed = Asset<PackedScene>("res://scenes/cards/card.tscn");
		if (packed == null) return;
		var node = packed.Instantiate<NCard>();
		parent.AddChild(node);
		// The native card uses a centered CardContainer. Normalize its origin for our list.
		node.Position = centered ? position : position + new Vector2(150, 211) * scale; node.Scale = Vector2.One * scale;
		node.Body.Position = Vector2.Zero;
		void Visible(string path, bool visible) { if (node.GetNodeOrNull<CanvasItem>(path) is { } n) n.Visible = visible; }
		void Texture(string path, string asset, string material = "")
		{
			if (node.GetNodeOrNull<TextureRect>(path) is { } n) { n.Texture = Asset<Texture2D>(asset); n.Material = Asset<Material>(material); }
		}
		foreach (string path in new[] { "%Frame", "%Portrait", "%PortraitBorder", "%TitleBanner" }) Visible(path, !c.Ancient);
		foreach (string path in new[] { "%AncientPortrait", "%AncientBorder", "%AncientTextBg", "%AncientBanner", "%AncientBorderGlassOverlay" }) Visible(path, c.Ancient);
		Texture("%Frame", c.Frame, c.FrameMaterial); Texture("%Portrait", c.Portrait);
		Texture("%PortraitBorder", c.Border, c.BannerMaterial); Texture("%TitleBanner", c.Banner, c.BannerMaterial);
		Texture("%AncientPortrait", c.Portrait); Texture("%AncientBorder", c.AncientBorder); Texture("%AncientTextBg", c.AncientText);
		Texture("%EnergyIcon", c.EnergyIcon);
		Visible("%EnergyIcon", c.Cost.Length > 0); Visible("%EnergyLabel", c.Cost.Length > 0);
		Visible("%StarIcon", c.StarCost.Length > 0); Visible("%StarLabel", c.StarCost.Length > 0);
		foreach (var path in new[] { "%Lock", "%Highlight", "%UnplayableEnergyIcon", "%UnplayableStarIcon", "CardContainer/CardSparkles" }) Visible(path, false);
		var title = node.GetNode<MegaLabel>("%TitleLabel"); title.SetTextAutoSize(c.Title);
		title.AddThemeColorOverride("font_color", c.Upgraded ? StsColors.green : StsColors.cream);
		if (c.Upgraded) title.AddThemeColorOverride("font_outline_color", StsColors.cardTitleOutlineSpecial);
		node.GetNode<MegaLabel>("%EnergyLabel").SetTextAutoSize(c.Cost);
		node.GetNode<MegaLabel>("%StarLabel").SetTextAutoSize(c.StarCost);
		foreach (var value in new[] { ("%EnergyLabel", c.CostColor, c.CostOutline), ("%StarLabel", c.StarColor, c.StarOutline) })
		{ var label = node.GetNode<Label>(value.Item1); label.AddThemeColorOverride("font_color", ColorOf(value.Item2)); label.AddThemeColorOverride("font_outline_color", ColorOf(value.Item3)); }
		node.GetNode<MegaLabel>("%TypeLabel").SetTextAutoSize(c.Type);
		node.GetNode<MegaRichTextLabel>("%DescriptionLabel").SetTextAutoSize(c.Description.StartsWith("[center]") ? c.Description : "[center]" + c.Description + "[/center]");
		if (node.GetNodeOrNull<NinePatchRect>("%TypePlaque") is { } plaque) plaque.Material = Asset<Material>(c.BannerMaterial);
		Visible("%Enchantment", c.EnchantmentIcon.Length > 0);
		Texture("%Enchantment/Icon", c.EnchantmentIcon);
		if (node.GetNodeOrNull<MegaLabel>("%Enchantment/Label") is { } enchant) enchant.SetTextAutoSize(c.EnchantmentAmount);
		DisableInput(node);
	}

	private TResource? Asset<TResource>(string path) where TResource : Resource
	{
		if (string.IsNullOrEmpty(path)) return null;
		if (_assets.TryGetValue(path, out var cached)) { if (GodotObject.IsInstanceValid(cached)) return cached as TResource; _assets.Remove(path); }
		if (!path.StartsWith("res://", StringComparison.Ordinal) || !ResourceLoader.Exists(path)) { Report(path, "resource not found"); return null; }
		try { var resource = ResourceLoader.Load<TResource>(path); if (resource != null) _assets[path] = resource; return resource; }
		catch (Exception ex) { Report(path, ex.Message); return null; }
	}
	private void Report(string path, string error) { if (_reportedAssets.Add(path)) Log.Warn("[RMP:LiveSharing] Visual fallback: " + path + " — " + error); }
	private void Icon(Node parent, string path, Rect2 rect)
	{
		var texture = Asset<Texture2D>(path);
		if (texture == null) return;
		parent.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Texture = texture, Position = rect.Position, Size = rect.Size, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore });
	}
	private static void DisableInput(Node root)
	{
		root.SetProcessInput(false); root.SetProcessUnhandledInput(false); root.SetProcessUnhandledKeyInput(false); root.SetProcessShortcutInput(false);
		if (root is Control c) { c.MouseFilter = Control.MouseFilterEnum.Ignore; c.FocusMode = Control.FocusModeEnum.None; }
		foreach (Node child in root.GetChildren()) DisableInput(child);
	}
	private static Control Area(Node parent, Vector2 position, Vector2 size)
	{
		var c = new Control { Position = position, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
		parent.AddChild(c); return c;
	}
	private static void Solid(Node parent, Rect2 rect, Color color) => parent.AddChild(new ColorRect { Position = rect.Position, Size = rect.Size, Color = color, MouseFilter = Control.MouseFilterEnum.Ignore });
	private static Label Text(Node parent, string text, Rect2 rect, int size, HorizontalAlignment align = HorizontalAlignment.Left)
	{
		var label = new Label { Text = text, Position = rect.Position, Size = rect.Size, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true };
		label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", new Color("f4e7d0"));
		label.AddThemeColorOverride("font_outline_color", Colors.Black); label.AddThemeConstantOverride("outline_size", 4);
		parent.AddChild(label); return label;
	}
	private static void Rich(Node parent, string text, Rect2 rect, int size)
	{
		var label = new MegaRichTextLabel { Position = rect.Position, Size = rect.Size, MouseFilter = Control.MouseFilterEnum.Ignore, BbcodeEnabled = true, AutoSizeEnabled = false, ScrollActive = false };
		label.AddThemeFontOverride("normal_font", ThemeDB.FallbackFont);
		label.AddThemeFontOverride("bold_font", ThemeDB.FallbackFont);
		label.AddThemeFontOverride("italics_font", ThemeDB.FallbackFont);
		label.AddThemeFontSizeOverride("normal_font_size", size);
		parent.AddChild(label); label.SetTextAutoSize(text);
	}
	private static void Clear(Node node) { foreach (Node child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
	private static string T(string zh, string en) => LocalSpectatorSource.T(zh, en);
	public void Dispose() { SetControlEnabled(false); if (GodotObject.IsInstanceValid(_overlay)) { RememberPanelLayout(); _overlay.Hide(); _overlay.GetParent()?.RemoveChild(_overlay); _overlay.QueueFree(); } }
}
