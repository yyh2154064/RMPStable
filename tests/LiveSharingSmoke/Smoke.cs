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
	private static async Task SaveFrame(string name)
	{
		await Frames(30);
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
	private static async Task Inspect(RunState state, string page, int minimumCards)
	{
		await Frames(40);
		var source = Field("_source")!;
		string before = Fingerprint(state.Players[0]);
		object snap = source.GetType().GetMethod("Capture", Instance)!.Invoke(source, new object[] { state })!;
		var json = JsonSerializer.Serialize(snap);
		File.WriteAllText(Path.Combine(Output, page + ".json"), json);
		using var doc = JsonDocument.Parse(json);
		Check(doc.RootElement.GetProperty("Page").GetString() == page, page + " page selected");
		Check(doc.RootElement.GetProperty("Cards").GetArrayLength() >= minimumCards, page + " cards populated");
		Check(doc.RootElement.GetProperty("Background").GetArrayLength() > 0, page + " native background populated");
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
		Check(rendered.Count > 0 && rendered.All(c => c.Model == null), page + " cards have no gameplay model");
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
			await Inspect(state, "combat", 1);
			string beforeDeck = Fingerprint(state.Players[0]);
			var view = Field("_view")!;
			var deck = (Button)view.GetType().GetField("_deck", Instance)!.GetValue(view)!;
			deck.EmitSignal(Button.SignalName.Pressed);
			await Frames(30);
			Check(Descendants(Game.GetTree().Root.GetNode<Window>("RmpLocalSpectator")).OfType<NCard>().Count() == state.Players[0].Deck.Cards.Count, "spectator deck shows all cards");
			Check(beforeDeck == Fingerprint(state.Players[0]), "deck browsing leaves player unchanged");
			await SaveFrame("deck");
			deck.EmitSignal(Button.SignalName.Pressed);
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
}
