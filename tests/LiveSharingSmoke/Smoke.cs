using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Environment = System.Environment;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

// Test-only mod. Run exclusively in the isolated, non-Steam profile described in
// README.md. It intentionally creates a disposable run and never ships in RMP.
[ModInitializer(nameof(Initialize))]
public static class Smoke
{
	private static Type _controller = null!;
	private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
	private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
	private static string Output => Environment.GetEnvironmentVariable("RMP_SMOKE_OUTPUT")!;
	private static NGame Game => NGame.Instance ?? throw new InvalidOperationException("Game not initialized");
	public static void Initialize()
	{
		var args = OS.GetCmdlineArgs();
		int steam = Array.IndexOf(args, "--force-steam");
		if (string.IsNullOrEmpty(Output) || steam < 0 || steam + 1 >= args.Length || args[steam + 1] != "off") return;
		TaskHelper.RunSafely(Run());
	}
	private static async Task Frames(int count) { for (int i = 0; i < count; i++) await Game.ToSignal(Game.GetTree(), SceneTree.SignalName.ProcessFrame); }
	private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); GD.Print("[LiveSharingSmoke] PASS " + label); }
	private static object? Field(string name) => _controller.GetField(name, Static)!.GetValue(null);
	private static async Task Key(Key key)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true }); await Frames(3);
		Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false }); await Frames(3);
	}
	private static string Fingerprint(Player p) => JsonSerializer.Serialize(p.ToSerializable()) + ":" + p.PlayerCombatState?.Energy + ":" + string.Join(",", p.PlayerCombatState?.Hand.Cards.Select(c => c.Id + ":" + c.CurrentUpgradeLevel) ?? Array.Empty<string>());
	private static async Task SaveFrame(string name, int waitFrames = 30)
	{
		await Frames(waitFrames);
		if (DisplayServer.GetName() == "headless") return;
		var window = Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator");
		await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		var view = Field("_view")!;
		((Control)view.GetType().GetField("_preview", Instance)!.GetValue(view)!).Visible = false;
		await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		window.GetTexture().GetImage().SavePng(Path.Combine(Output, name + ".png"));
		Game.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Output, name + "-original.png"));
		((Control)view.GetType().GetField("_preview", Instance)!.GetValue(view)!).Visible = true;
	}
	private static async Task Inspect(RunState state, string page, int minimumCards, int waitFrames = 40)
	{
		await Frames(waitFrames);
		var source = Field("_source")!;
		string before = Fingerprint(state.Players[0]);
		object snap = source.GetType().GetMethod("Capture", Instance)!.Invoke(source, new object[] { state })!;
		var json = JsonSerializer.Serialize(snap);
		File.WriteAllText(Path.Combine(Output, page + ".json"), json);
		using var doc = JsonDocument.Parse(json);
		Check(doc.RootElement.GetProperty("Page").GetString() == page, page + " page selected");
		Check(doc.RootElement.GetProperty("Cards").GetArrayLength() >= minimumCards, page + " cards populated");
		Check(doc.RootElement.GetProperty("Background").GetArrayLength() > 0 || doc.RootElement.GetProperty("PageArt").GetArrayLength() > 0, page + " native background populated");
		if (page == "combat") Check(doc.RootElement.GetProperty("Creatures").EnumerateArray().Any(c => !c.GetProperty("Player").GetBoolean() && c.GetProperty("Intents").EnumerateArray().Any(i => i.GetProperty("Icon").GetString()!.Contains("attack") && i.GetProperty("Text").GetString()!.Length > 0)), "enemy attack icon and damage populated");
		if (page == "combat") Check(doc.RootElement.GetProperty("Creatures").EnumerateArray().All(c => c.GetProperty("StateArt").GetArrayLength() > 0 && c.GetProperty("StateLabels").GetArrayLength() > 0), "native health bar art and labels populated");
		if (page == "combat") Check(doc.RootElement.GetProperty("Creatures").EnumerateArray().Any(c => c.GetProperty("Block").GetInt32() == 7 && c.GetProperty("StateLabels").EnumerateArray().Any(l => l.GetProperty("Text").GetString() == "7")), "native block shield value populated");
		if (page == "reward") Check(doc.RootElement.GetProperty("RewardArt").GetArrayLength() > 0 && doc.RootElement.GetProperty("RewardLabels").GetArrayLength() > 0 && doc.RootElement.GetProperty("UnderlayCards").GetArrayLength() > 0, "native reward banner and underlying page populated");
		if (page == "shop") Check(doc.RootElement.GetProperty("Items").EnumerateArray().Last().GetProperty("Art").GetArrayLength() > 0, "native card removal icon populated");
		Check(doc.RootElement.GetProperty("HudArt").GetArrayLength() > 0, page + " native HUD populated");
		Check(before == Fingerprint(state.Players[0]), page + " capture leaves player unchanged");
		object isolated = snap.GetType().GetMethod("RoundTrip", Static)!.Invoke(null, new[] { snap })!;
		Field("_view")!.GetType().GetMethod("Update", Instance)!.Invoke(Field("_view"), new[] { isolated });
		Check(before == Fingerprint(state.Players[0]), page + " rendering leaves player unchanged");
		var rendered = Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<NCard>().ToList();
		Check((minimumCards == 0 || rendered.Count > 0) && rendered.All(c => c.Model == null), page + " cards have no gameplay model");
		if (page is "loot" or "event" or "selection") Check(doc.RootElement.GetProperty("PageArt").GetArrayLength() > 0 && doc.RootElement.GetProperty("PageLabels").GetArrayLength() > 0, page + " native art and options populated");
		if (page == "map") Check(doc.RootElement.GetProperty("Drawings").EnumerateArray().Any(s => s.GetProperty("Lines").EnumerateArray().Any(l => l.GetProperty("Points").ValueKind == JsonValueKind.Array && l.GetProperty("Points").GetArrayLength() >= 4)), "map drawings contain actual vector strokes");
		await SaveFrame(page);
	}
	private static System.Collections.Generic.IEnumerable<Node> Descendants(Node root)
	{
		yield return root;
		foreach (Node child in root.GetChildren()) foreach (var nested in Descendants(child)) yield return nested;
	}
	private static async Task Run()
	{
		try
		{
			for (int i = 0; i < 1800 && NGame.Instance?.MainMenu == null; i++) await Frames(1);
			await Frames(300);
			_controller = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.LiveSharingController")).First(t => t != null)!;
			Check(Field("_view") == null, "OFF by default");
			Game.MainMenu!.OpenSettingsMenu();
			await Frames(60);
			var entry = Descendants(Game.MainMenu.SubmenuStack).OfType<NInputSettingsEntry>().Single(e => e.InputName == "rmpLiveSharing");
			string entryText = entry.GetNode<Node>("%InputLabel") is Label plain ? plain.Text : entry.GetNode<RichTextLabel>("%InputLabel").Text;
			Check(entryText.Contains("观战") || entryText.Contains("spectator"), "native settings row has spectator label");
			Game.MainMenu.SubmenuStack.Pop();
			await Frames(30);
			SaveManager.Instance.SetFtuesEnabled(false);
#if STS2_0111
			NInputManager.Instance!.ModifyMKbKey("rmpLiveSharing", Godot.Key.F8);
#else
			NInputManager.Instance!.ModifyShortcutKey("rmpLiveSharing", Godot.Key.F8);
#endif
			var character = ModelDb.Character<Ironclad>();
			var state = RunState.CreateForNewRun(new[] { Player.CreateForNewRun(character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1) }, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), Array.Empty<ModifierModel>(), GameMode.Standard, 0, "RMPLOCALTEST");
			RunManager.Instance.SetUpNewSingleplayer(state, false);
			await PreloadManager.LoadRunAssets(new[] { character });
			RunManager.Instance.Launch();
			Game.RootSceneContainer.SetCurrentScene(NRun.Create(state));
			await RunManager.Instance.SetActInternal(0);
			RunManager.Instance.RunLocationTargetedBuffer.OnLocationChanged(state.RunLocation);
			RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(state.MapLocation);
			await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
			RunManager.Instance.ActionExecutor.Unpause();
			await Frames(90);
			NMerchantRoom.Instance!.OpenInventory();
			await Frames(90);
			File.WriteAllLines(Path.Combine(Output, "shop-tree.txt"), Descendants(NMerchantRoom.Instance).Select(n => n.GetPath() + " " + n.GetType().Name + "/" + n.GetClass() + " " + (n is Control c ? c.Position + " " + c.Size : "") + " " + (n is TextureRect t ? t.Texture?.ResourcePath : "") + " " + (n.GetClass() == "SpineSprite" ? string.Join(";", n.GetPropertyList().Select(p => p["name"].AsString()).Where(p => p.Contains("skeleton") || p.Contains("animation")).Select(p => p + "=" + n.Get(p))) : "")));
			var realCard = Descendants(NRun.Instance!).OfType<NCard>().First();
			File.WriteAllLines(Path.Combine(Output, "card-tree.txt"), Descendants(realCard).OfType<Control>().Select(c => c.GetPath() + " " + c.Position + " " + c.Size));
			await Key(Godot.Key.F8);
			Check(Field("_view") != null, "F8 opens spectator");
			await Inspect(state, "shop", 7);
			await Key(Godot.Key.F8); Check(Field("_view") == null, "F8 closes spectator");
#if STS2_0111
			NInputManager.Instance!.ModifyMKbKey("rmpLiveSharing", Godot.Key.F9);
#else
			NInputManager.Instance!.ModifyShortcutKey("rmpLiveSharing", Godot.Key.F9);
#endif
			await Key(Godot.Key.F8); Check(Field("_view") == null, "old key inactive after rebind");
			await Key(Godot.Key.F9); Check(Field("_view") != null, "custom key opens spectator");
			var rewards = NCardRewardSelectionScreen.ShowScreen(state.Players[0].Deck.Cards.Take(3).Select(c => new CardCreationResult(c)).ToList(), Array.Empty<CardRewardAlternative>())!;
			File.WriteAllLines(Path.Combine(Output, "reward-tree.txt"), Descendants(rewards).Select(n => n.GetPath() + " " + n.GetType().Name + " " + (n is TextureRect tr ? tr.Texture?.GetType().Name + ":" + tr.Texture?.ResourcePath : n is Sprite2D sp ? sp.Texture?.GetType().Name + ":" + sp.Texture?.ResourcePath : n is Polygon2D poly ? poly.Texture?.ResourcePath : n is MeshInstance2D mesh ? mesh.Texture?.ResourcePath : "")));
			await Inspect(state, "reward", 3);
			NOverlayStack.Instance!.Remove(rewards);
			await Frames(30);
			await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<ExoskeletonsWeak>().ToMutable());
			await Frames(180);
			state.Players[0].Creature.GainBlockInternal(7);
			state.Players[0].Creature.SetCurrentHpInternal(52);
			NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.Monster != null).Entity.SetCurrentHpInternal(12);
			await Frames(60);
			File.WriteAllLines(Path.Combine(Output, "health-tree.txt"), NCombatRoom.Instance.CreatureNodes.SelectMany(n => Descendants(n.GetNode<Control>("%HealthBar"))).Select(n => n.GetPath() + " " + n.GetType().Name + "/" + n.GetClass() + " " + (n is Control c ? c.Size.ToString() : "") + " " + (n is CanvasItem ci ? ci.Material?.ResourcePath : "") + " " + (n is Label l ? l.GetThemeFontSize("font_size") + " " + l.GetThemeColor("font_color") + " " + l.GetThemeColor("font_outline_color") : "")));
			var bash = state.Players[0].Deck.Cards.First(c => c.Id.Entry == "BASH");
			MegaCrit.Sts2.Core.Commands.CardCmd.Upgrade(bash);
			var nativeDeck = MegaCrit.Sts2.Core.Nodes.Screens.NDeckViewScreen.ShowScreen(state.Players[0]);
			await Frames(60);
			File.WriteAllLines(Path.Combine(Output, "deck-tree.txt"), Descendants(nativeDeck!).Where(n => n is Control).Select(n => n.GetPath() + " " + n.GetType().Name + "/" + n.GetClass() + " " + ((Control)n).GetGlobalRect()));
			MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance!.Close();
			await Frames(30);
			await Inspect(state, "combat", 1);
			var intentValue = Descendants(NCombatRoom.Instance).OfType<MegaCrit.Sts2.Core.Nodes.Combat.NIntent>().First().GetNode<MegaRichTextLabel>("%Value");
			string oldIntent = intentValue.Text; intentValue.Text = "123[font_size=18]×12[/font_size]"; await Frames(5);
			await Inspect(state, "combat", 1, 0); await SaveFrame("intent-multidigit");
			Check(Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<MegaRichTextLabel>().Any(l => l.Text == intentValue.Text && l.GetContentHeight() <= l.Size.Y && l.GetContentWidth() <= l.Size.X), "multi-digit and multi-hit intent text fits its rendered rectangle");
			intentValue.Text = oldIntent; await Frames(10);
			string beforeDeck = Fingerprint(state.Players[0]);
			var view = Field("_view")!;
			var deck = (Button)view.GetType().GetField("_deck", Instance)!.GetValue(view)!;
			deck.EmitSignal(Button.SignalName.Pressed);
			await Frames(30);
			Check(Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<NCard>().Count() == state.Players[0].Deck.Cards.Count, "spectator deck shows all cards");
			Check(beforeDeck == Fingerprint(state.Players[0]), "deck browsing leaves player unchanged");
			await SaveFrame("deck");
			foreach (int mode in new[] { 0, 1, 2, 3 })
			{
				view.GetType().GetMethod("SortDeck", Instance)!.Invoke(view, new object[] { mode }); await Frames(5);
				CheckSort(state, view, mode);
				view.GetType().GetMethod("SortDeck", Instance)!.Invoke(view, new object[] { mode }); await Frames(5);
				CheckSort(state, view, mode);
			}
			Check(beforeDeck == Fingerprint(state.Players[0]), "all deck sorts leave player unchanged");
			var current = Field("_source")!.GetType().GetMethod("Capture", Instance)!.Invoke(Field("_source"), new object[] { state })!;
			var list = current.GetType().GetProperty("Deck")!.GetValue(current)!;
			view.GetType().GetMethod("OpenInspect", Instance)!.Invoke(view, new object[] { list, 0, false }); await Frames(10);
			view.GetType().GetMethod("NavigateInspect", Instance)!.Invoke(view, new object[] { -1 });
			Check((int)view.GetType().GetField("_inspectIndex", Instance)!.GetValue(view)! == 0, "first card has no previous or wrap");
			await SaveFrame("inspect");
			Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<Button>().Single(b => b.Name == "SpectatorInspectUpgrade").EmitSignal(Button.SignalName.Pressed);
			await SaveFrame("inspect-upgrade");
			for (int i = 0; i < 30; i++) view.GetType().GetMethod("NavigateInspect", Instance)!.Invoke(view, new object[] { 1 });
			Check((int)view.GetType().GetField("_inspectIndex", Instance)!.GetValue(view)! == state.Players[0].Deck.Cards.Count - 1, "last card has no next or wrap");
			Check(beforeDeck == Fingerprint(state.Players[0]), "inspection and upgrade preview leave player unchanged");
			view.GetType().GetMethod("OpenInspect", Instance)!.Invoke(view, new object[] { list, state.Players[0].Deck.Cards.ToList().IndexOf(bash), false });
			Check((bool)view.GetType().GetField("_inspectUpgraded", Instance)!.GetValue(view)!, "upgraded card opens with upgrade ticked");
			await SaveFrame("inspect-keywords");
			var detail = (Control)view.GetType().GetField("_inspect", Instance)!.GetValue(view)!;
			var cardFrame = Descendants(detail).OfType<NCard>().Single().GetNode<Control>("%Frame").GetGlobalRect();
			var tipsRect = Descendants(detail).OfType<VBoxContainer>().Single(n => n.Name == "SpectatorTips").GetGlobalRect();
			Check(tipsRect.Position.X >= cardFrame.End.X || tipsRect.End.X <= cardFrame.Position.X, "keyword panel sits beside the inspected card");
			Check(Descendants(detail).OfType<NCard>().Single().GetNode<Label>("%TitleLabel").GetThemeColor("font_color") == StsColors.green, "upgrade title uses original bright green");
			Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<Button>().Single(b => b.Name == "SpectatorInspectUpgrade").EmitSignal(Button.SignalName.Pressed);
			await SaveFrame("inspect-base");
			Check(beforeDeck == Fingerprint(state.Players[0]), "downgrade comparison leaves original upgraded card unchanged");
			view.GetType().GetMethod("CloseInspect", Instance)!.Invoke(view, null);
			var pin = Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<Button>().Single(b => b.Name == "SpectatorPin");
			pin.ButtonPressed = false; Check(!Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator").AlwaysOnTop, "pin off updates native window");
			pin.ButtonPressed = true; Check(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator").AlwaysOnTop, "pin on updates native window");
			deck.EmitSignal(Button.SignalName.Pressed);
			var map = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance!.Open(true);
			await Frames(90);
			map.Drawings.BeginLineLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(700, 500), MegaCrit.Sts2.Core.Nodes.Screens.Map.DrawingMode.Drawing);
			map.Drawings.UpdateCurrentLinePositionLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(1100, 750)); map.Drawings.UpdateCurrentLinePositionLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(1500, 650)); map.Drawings.StopLineLocal();
			await Inspect(state, "map", 0);
			var artPane = (Control)view.GetType().GetField("_screenArt", Instance)!.GetValue(view)!;
			var originalIds = Descendants(artPane).Select(n => n.GetInstanceId()).ToArray();
			var watch = System.Diagnostics.Stopwatch.StartNew(); double captureMs = 0, jsonMs = 0, renderMs = 0;
			for (int i = 0; i < 10; i++) { var source = Field("_source")!; double started = watch.Elapsed.TotalMilliseconds; var sample = source.GetType().GetMethod("Capture", Instance)!.Invoke(source, new object[] { state })!; double captured = watch.Elapsed.TotalMilliseconds; var detached = sample.GetType().GetMethod("RoundTrip", Static)!.Invoke(null, new[] { sample })!; double serialized = watch.Elapsed.TotalMilliseconds; view.GetType().GetMethod("Update", Instance)!.Invoke(view, new[] { detached }); captureMs += captured - started; jsonMs += serialized - captured; renderMs += watch.Elapsed.TotalMilliseconds - serialized; }
			watch.Stop();
			Check(originalIds.SequenceEqual(Descendants(artPane).Select(n => n.GetInstanceId())), "map refresh retains native drawing nodes");
			GD.Print($"[LiveSharingSmoke] Map update avg={watch.Elapsed.TotalMilliseconds / 10:F1} ms (capture={captureMs / 10:F1}, JSON={jsonMs / 10:F1}, render={renderMs / 10:F1}); Engine.MaxFps={Engine.MaxFps}; actualFPS={Engine.GetFramesPerSecond()}");
			map.Drawings.BeginLineLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(900, 600), MegaCrit.Sts2.Core.Nodes.Screens.Map.DrawingMode.Erasing); map.Drawings.UpdateCurrentLinePositionLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(1400, 700)); map.Drawings.StopLineLocal();
			await Inspect(state, "map", 0); await SaveFrame("map-erased");
			map.Close(); await Frames(30);
			var lootSet = new MegaCrit.Sts2.Core.Rewards.RewardsSet(state.Players[0]).WithCustomRewards(new System.Collections.Generic.List<MegaCrit.Sts2.Core.Rewards.Reward> { new MegaCrit.Sts2.Core.Rewards.GoldReward(10, state.Players[0]) });
			var loot = MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen.ShowScreen(lootSet, false, state); await Inspect(state, "loot", 0);
			NOverlayStack.Instance!.Remove(loot); await Frames(20);
			var canonicalAncient = ModelDb.AncientEvent<MegaCrit.Sts2.Core.Models.Events.Neow>();
			state.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, canonicalAncient.Id); await RunManager.Instance.EnterRoom(new EventRoom(canonicalAncient) { OnStart = e => ((AncientEventModel)e).DebugOption = "LEAFY_POULTICE" }); await Frames(90); var ancient = RunManager.Instance.EventSynchronizer.GetLocalEvent();
			await Inspect(state, "event", 0); await SaveFrame("ancient-options");
			var choice = ancient.CurrentOptions.First(o => o.Relic is MegaCrit.Sts2.Core.Models.Relics.LeafyPoultice);
			var chooseTask = choice.Chosen();
			for (int i = 0; i < 90 && !Descendants(NRun.Instance!.GlobalUi.CardPreviewContainer).OfType<NCard>().Any(c => c.IsVisibleInTree()); i++) await Frames(1);
			await Inspect(state, "event", 1, 0); await SaveFrame("ancient-transform", 0); await chooseTask; await Frames(90);
			await Inspect(state, "event", 0); await SaveFrame("ancient-poultice");
			Check(state.Players[0].Creature.MaxHp == 68 && state.Players[0].Relics.Any(r => r is MegaCrit.Sts2.Core.Models.Relics.LeafyPoultice), "Leafy Poultice completion follows HP and transformed deck");
			var secondAncient = ModelDb.AncientEvent<MegaCrit.Sts2.Core.Models.Events.Tezcatara>();
			state.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, secondAncient.Id);
			await RunManager.Instance.EnterRoom(new EventRoom(secondAncient) { OnStart = e => ((AncientEventModel)e).DebugOption = "BIIIG_HUG" }); await Frames(90);
			await Inspect(state, "event", 0); await SaveFrame("ancient-tezcatara");
			var selectTask = RunManager.Instance.EventSynchronizer.GetLocalEvent().CurrentOptions.First(o => o.Relic is MegaCrit.Sts2.Core.Models.Relics.BiiigHug).Chosen(); await Frames(30);
			await Inspect(state, "selection", 1); await SaveFrame("ancient-remove-selection");
			var selection = (NDeckCardSelectScreen)NOverlayStack.Instance!.Peek()!;
			foreach (var c in state.Players[0].Deck.Cards.Take(4).ToList()) typeof(NDeckCardSelectScreen).GetMethod("OnCardClicked", Instance)!.Invoke(selection, new object[] { c });
			await Inspect(state, "selection", 4); await SaveFrame("ancient-remove-confirm");
			typeof(NDeckCardSelectScreen).GetMethod("ConfirmSelection", Instance)!.Invoke(selection, new object?[] { null }); await selectTask; await Frames(60);
			await Inspect(state, "event", 0); Check(state.Players[0].Deck.Cards.Count == 6, "Ancient selection returns to event after removing chosen cards");
			if (Environment.GetEnvironmentVariable("RMP_SMOKE_HOLD") == "1") { GD.Print("[LiveSharingSmoke] HOLD for visual review"); return; }
			Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator").EmitSignal(Window.SignalName.CloseRequested);
			await Frames(5); Check(Field("_view") == null, "native window close disposes spectator");
			await Key(Godot.Key.F9); Check(Field("_view") != null, "spectator can reopen");
			RunManager.Instance.CleanUp();
			await Frames(5); Check(Field("_view") == null && !Game.GetTree().Root.HasNode("RmpLocalSpectator"), "run cleanup closes and frees spectator");
			GD.Print("[LiveSharingSmoke] ALL PASSED");
			Game.GetTree().Quit();
		}
		catch (Exception ex) { GD.PrintErr("[LiveSharingSmoke] FAIL " + ex); File.WriteAllText(Path.Combine(Output, "failure.txt"), ex.ToString()); Game.GetTree().Quit(1); }
	}
	private static void CheckSort(RunState state, object view, int mode)
	{
		var grid = new NCardGrid();
		try
		{
			var realCards = state.Players[0].Deck.Cards.ToList();
			((System.Collections.Generic.List<CardModel>)typeof(NCardGrid).GetField("_cards", Instance)!.GetValue(grid)!).AddRange(realCards);
			var algorithms = (System.Collections.Generic.Dictionary<SortingOrders, Func<CardModel, CardModel, int>>)typeof(NCardGrid).GetProperty("SortingAlgorithms", Instance)!.GetValue(grid)!;
			var priority = (System.Collections.Generic.List<int>)view.GetType().GetField("_sortPriority", Instance)!.GetValue(view)!;
			var descending = (bool[])view.GetType().GetField("_descending", Instance)!.GetValue(view)!;
			SortingOrders Order(int m) => m switch { 0 => descending[m] ? SortingOrders.Descending : SortingOrders.Ascending, 1 => descending[m] ? SortingOrders.TypeDescending : SortingOrders.TypeAscending, 2 => descending[m] ? SortingOrders.CostDescending : SortingOrders.CostAscending, _ => descending[m] ? SortingOrders.AlphabetDescending : SortingOrders.AlphabetAscending };
			var expected = realCards.ToList();
			if (priority[0] == 0) { if (descending[0]) expected.Reverse(); }
			else expected.Sort((a, b) => { foreach (int m in priority) { int diff = algorithms[Order(m)](a, b); if (diff != 0) return diff; } return a.Id.CompareTo(b.Id); });
			var snapshot = view.GetType().GetField("_snapshot", Instance)!.GetValue(view)!;
			var actual = (System.Collections.IEnumerable)view.GetType().GetMethod("SortedCards", Instance)!.Invoke(view, new[] { snapshot.GetType().GetProperty("Deck")!.GetValue(snapshot) })!;
			Check(expected.Select(c => c.Title).SequenceEqual(actual.Cast<object>().Select(c => (string)c.GetType().GetProperty("Title")!.GetValue(c)!)), "native sort " + mode + (descending[mode] ? " descending" : " ascending"));
		}
		finally { grid.Free(); }
	}
}
