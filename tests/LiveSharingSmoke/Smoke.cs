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
public static partial class Smoke
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
	private static async Task Pointer(Vector2 position, bool? pressed = null)
	{
		// Keep the OS cursor at the injected position across layout/animation
		// frames; otherwise native motion can replace the synthetic hover before
		// release, cancelling clicks on small controls.
		if (!pressed.HasValue) Game.GetViewport().WarpMouse(position);
		InputEvent input = pressed.HasValue
			? new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = pressed.Value }
			: new InputEventMouseMotion { Position = position, GlobalPosition = position };
		Game.GetViewport().PushInput(input, true); await Frames(3);
	}
	private static async Task Click(Vector2 position) { await Pointer(position); await Pointer(position, true); await Pointer(position, false); }
	private static async Task CheckPanelInput(Player player)
	{
		var view = Field("_view")!;
		var panel = (Control)view.GetType().GetField("_panel", Instance)!.GetValue(view)!;
		// Keep the same harness usable for the previous DLL's performance baseline.
		var content = panel.GetNodeOrNull<Control>("SpectatorContent");
		var surface = (content ?? panel).GetNode<SubViewportContainer>("SpectatorSurface");
		if (content != null) Check(content.ClipContents && surface.Position == Vector2.Zero, "spectator picture is embedded in its own clipped content region");
		void CheckFit()
		{
			if (content == null) return;
			Check((surface.Size * surface.Scale).DistanceTo(content.Size) < 0.01f, "spectator picture fills content without letterboxing or overflow");
			Check(Math.Abs(content.Position.X - (panel.Size.X - content.Position.X - content.Size.X)) < 0.01f && Math.Abs(content.Position.X - (panel.Size.Y - content.Position.Y - content.Size.Y)) < 0.01f, "spectator frame has equal left right and bottom borders");
			var fill = panel.GetNode<ColorRect>("SpectatorTitleFill");
			Check(Math.Abs(fill.Position.Y + fill.Size.Y - content.Position.Y) < 0.01f, "title background directly meets spectator picture with no separator gap");
			Check(new Rect2(panel.GlobalPosition, new Vector2(panel.Size.X, content.Position.Y)).Encloses(panel.GetNode<TextureRect>("SpectatorClose/Icon").GetGlobalRect()), "red cross remains fully inside resized navigation row");
		}
		CheckFit();
		var closeIcon = panel.GetNode<TextureRect>("SpectatorClose/Icon");
		var relicRow = Descendants(NRun.Instance!.GlobalUi.RelicInventory).OfType<MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder>().Where(n => n.IsVisibleInTree()).Select(n => n.GetGlobalRect()).OrderBy(r => r.Position.Y).First();
		Check(Math.Abs(panel.Size.X - 640f * 5 / 6) < 1 && Math.Abs(panel.Position.Y - relicRow.Position.Y - relicRow.Size.Y * 3 / 5) < 1, "new profile defaults to five-sixths minimum size exposing three-fifths of native relic row");
		var firstCorner = panel.Position + panel.Size - new Vector2(10, 10);
		await Pointer(firstCorner); await Pointer(firstCorner, true); await Pointer(firstCorner + new Vector2(300, 170)); await Pointer(firstCorner + new Vector2(300, 170), false);
		Check(closeIcon.Texture?.ResourcePath == "res://images/atlases/compressed.sprites/back_button_x.tres" && closeIcon.SelfModulate.R > closeIcon.SelfModulate.G * 2 && panel.GetNode<Button>("SpectatorClose").Text == "", "spectator close uses native angular cross with red tint and no text button");
		Check(closeIcon.Size == new Vector2(28, 28) && closeIcon.Position.X + closeIcon.Size.X <= 48 && closeIcon.Position.Y + closeIcon.Size.Y <= 36 && closeIcon.GlobalPosition.X >= panel.GlobalPosition.X + panel.Size.X - 56 * panel.GetNode<Button>("SpectatorClose").Scale.X, "red close cross fits far right of navigation row without native texture minimum-size overflow");
		var deck = (Button)view.GetType().GetField("_deck", Instance)!.GetValue(view)!;
		string before = Fingerprint(player);
		await Click(surface.GetGlobalTransform() * deck.GetGlobalRect().GetCenter());
		Check((bool)view.GetType().GetField("_showDeck", Instance)!.GetValue(view)!, "game pointer reaches embedded deck control");
		Check(MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance!.CurrentCapstoneScreen == null && before == Fingerprint(player), "embedded pointer does not click or change underlying game");
		await Click(surface.GetGlobalTransform() * deck.GetGlobalRect().GetCenter());
		Check(!(bool)view.GetType().GetField("_showDeck", Instance)!.GetValue(view)!, "embedded deck control returns to source page");
		var origin = panel.Position; var title = panel.Position + new Vector2(panel.Size.X - 120 * panel.GetNode<Control>("SpectatorTitlebar").Scale.X, 22);
		await Pointer(title); await Pointer(title, true); await Pointer(title + new Vector2(80, 50)); await Pointer(title + new Vector2(80, 50), false);
		Check(panel.Position.DistanceTo(origin + new Vector2(80, 50)) < 2, $"titlebar drag moves embedded panel (from {origin} to {panel.Position})");
		float width = panel.Size.X; var corner = panel.Position + panel.Size - new Vector2(10, 10);
		var iconSize = closeIcon.GetGlobalRect().Size; var sourceBox = panel.GetNode<Button>("SpectatorTitlebar/SpectatorSourceSelector/SpectatorSource0"); var sourceSize = sourceBox.GetGlobalRect().Size;
		await Pointer(corner); await Pointer(corner, true); await Pointer(corner - new Vector2(100, 56)); await Pointer(corner - new Vector2(100, 56), false);
		Check(panel.Size.X < width - 90 && Math.Abs(surface.Scale.X - surface.Scale.Y) < 0.001f, "resize keeps spectator aspect ratio");
		Check((closeIcon.GetGlobalRect().Size - iconSize * (panel.Size.X / width)).Length() < 0.01f && (sourceBox.GetGlobalRect().Size - sourceSize * (panel.Size.X / width)).Length() < 0.01f, "real pointer resize scales player boxes and red close cross together");
		CheckFit();
		Check(before == Fingerprint(player), "moving and resizing panel leaves player unchanged");
	}
	private static async Task CheckSourceSelector(RunState state)
	{
		var view = Field("_view")!; var type = view.GetType(); var source = Field("_source")!;
		_controller.GetMethod("CancelCapture", Static)?.Invoke(null, null);
		var snapshot = source.GetType().GetMethod("Capture", Instance)!.Invoke(source, new object[] { state })!;
		var options = (System.Collections.IEnumerable)snapshot.GetType().GetProperty("Sources")!.GetValue(snapshot)!;
		var participant = options.Cast<object>().First();
		var participantType = participant!.GetType();
		Check((string)participant.GetType().GetProperty("Id")!.GetValue(participant)! == state.Players[0].NetId.ToString(), "singleplayer selector uses actual player network ID");
		Check((string)participant.GetType().GetProperty("Name")!.GetValue(participant)! == MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerNameRaw(RunManager.Instance.NetService.Platform, state.Players[0].NetId), "singleplayer selector uses native platform player name");
		var panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		var selector = panel.GetNode<Control>("SpectatorTitlebar/SpectatorSourceSelector");
		Check(selector.GetNode<Button>("SpectatorSourcePrevious").Disabled && !selector.GetNode<Button>("SpectatorSourceNext").Disabled && selector.GetChildren().OfType<Button>().Count(b => b.Visible && b.Name.ToString().StartsWith("SpectatorSource") && char.IsDigit(b.Name.ToString()[^1])) == 3 && options.Cast<object>().Count() == 5, "local preview provides five sources with three visible on its first page");
		Check(options.Cast<object>().Skip(1).All(p => (bool)p.GetType().GetProperty("Simulated")!.GetValue(p)!), "four extra preview sources are explicitly marked as simulated");
		Check(selector.GetNode<TextureRect>("SpectatorSource0/Background").Texture?.ResourcePath == "res://images/ui/reward_screen/reward_item_button.png", "source frame reuses native blue loot button texture");
		Check(selector.GetNode<Button>("SpectatorSource0").GetChildren().OfType<Label>().Single().GetThemeColor("font_color") == Colors.White, "source player label is white");
		var frame = (StyleBoxFlat)selector.GetNode<Panel>("SpectatorSource0/FrameBorder").GetThemeStylebox("panel");
		Check(!frame.DrawCenter && frame.BorderColor == new Color("142a35") && frame.BorderWidthLeft == 2, "source frame has distinct darker blue border without recoloring its center");
		Check(selector.Position.Y >= 6 && selector.Size.Y <= 26 && selector.GetNode<Button>("SpectatorSource0").Size.X <= 150 && panel.Size.X / panel.GetNode<Control>("SpectatorTitlebar").Scale.X - 64 - (selector.Position.X + selector.GetNode<Button>("SpectatorSourceNext").Position.X + 24) >= 96, "compact player boxes leave vertical padding and a generous drag area");
		Check(selector.GetNode<TextureRect>("SpectatorSourcePrevious/Icon").Texture != null && selector.GetNode<TextureRect>("SpectatorSourceNext/Icon").Texture != null, "source arrows reuse native inspect-card textures");
		string before = Fingerprint(state.Players[0]); await Click(selector.GetNode<Button>("SpectatorSource0").GetGlobalRect().GetCenter());
		Check(before == Fingerprint(state.Players[0]), "source player click does not change the game");
		await Click(selector.GetNode<Button>("SpectatorSourceNext").GetGlobalRect().GetCenter());
		Check(selector.GetNode<Button>("SpectatorSourceNext").Disabled && !selector.GetNode<Button>("SpectatorSourcePrevious").Disabled && !selector.GetNode<Button>("SpectatorSource2").Visible, "real pointer flips five-source preview to final two-player page");
		await Click(selector.GetNode<Button>("SpectatorSource1").GetGlobalRect().GetCenter()); await Frames(40);
		Check((string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)! == "local-preview:5", "real pointer selects fifth preview source after refreshed snapshot");
		Check(selector.GetNode<TextureRect>("SpectatorSource1/Background").SelfModulate == Colors.White && selector.GetNode<TextureRect>("SpectatorSource0/Background").SelfModulate != Colors.White, "confirmed fifth source alone uses native loot blue without dark tint");
		await SaveFrame("source-selector-page2", 0);
		await Click(selector.GetNode<Button>("SpectatorSourcePrevious").GetGlobalRect().GetCenter());
		Check(selector.GetNode<Button>("SpectatorSourcePrevious").Disabled && !selector.GetNode<Button>("SpectatorSourceNext").Disabled, "real pointer returns preview list to first page");
		await Click(selector.GetNode<Button>("SpectatorSource0").GetGlobalRect().GetCenter()); await Frames(40);
		Check((string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)! == state.Players[0].NetId.ToString() && before == Fingerprint(state.Players[0]), "preview switches back to actual local ID without changing player state");
		Check(selector.GetNode<TextureRect>("SpectatorSource0/Background").SelfModulate == Colors.White && selector.GetNode<TextureRect>("SpectatorSource1/Background").SelfModulate != Colors.White && selector.GetNode<TextureRect>("SpectatorSource2/Background").SelfModulate != Colors.White, "native loot blue selection follows real pointer back to local source");
		await SaveFrame("source-selector-page1", 0);
		// A second, test-only renderer exercises generic paging with DTO fixtures,
		// without changing the local provider's preview list or enabling multiplayer.
		string requested = "";
		var testView = type.GetConstructors(Instance).Single().Invoke(new object?[] { new Action(() => { }), null, new Action<string>(id => requested = id) });
		try
		{
			var testPanel = (Control)type.GetField("_panel", Instance)!.GetValue(testView)!;
			var testSelector = testPanel.GetNode<Control>("SpectatorTitlebar/SpectatorSourceSelector");
			var listType = snapshot.GetType().GetProperty("Sources")!.PropertyType;
			System.Collections.IList Players(int count)
			{
				var result = (System.Collections.IList)Activator.CreateInstance(listType)!;
				for (int i = 0; i < count; i++) { var item = Activator.CreateInstance(participantType!)!; item.GetType().GetProperty("Id")!.SetValue(item, (i + 1).ToString()); item.GetType().GetProperty("Name")!.SetValue(item, "玩家 " + (i + 1)); result.Add(item); }
				return result;
			}
			void Update(int count, string current = "1") => type.GetMethod("UpdateSources", Instance)!.Invoke(testView, new object[] { Players(count), current });
			Button left = testSelector.GetNode<Button>("SpectatorSourcePrevious"), right = testSelector.GetNode<Button>("SpectatorSourceNext");
			string[] Names() => Enumerable.Range(0, 3).Select(i => testSelector.GetNode<Button>("SpectatorSource" + i)).Where(b => b.Visible).Select(b => b.GetChildren().OfType<Label>().Single().Text).ToArray();
			foreach (int count in new[] { 0, 1, 2, 3 }) { Update(count); Check(left.Disabled && right.Disabled && Names().Length == count, "source selector fits " + count + " players without paging or empty slots"); }
			Update(7); Check(left.Disabled && !right.Disabled && Names().SequenceEqual(new[] { "玩家 1", "玩家 2", "玩家 3" }), "source selector first page contains three players");
			type.GetMethod("NavigateSources", Instance)!.Invoke(testView, new object[] { -1 }); Check(Names()[0] == "玩家 1", "source paging does not wrap before the first page");
			right.EmitSignal(Button.SignalName.Pressed); Check(!left.Disabled && !right.Disabled && Names().SequenceEqual(new[] { "玩家 4", "玩家 5", "玩家 6" }), "source selector middle page contains next three players");
			testSelector.GetNode<Button>("SpectatorSource1").EmitSignal(Button.SignalName.Pressed); Check(requested == "5", "source selection requests clicked stable player ID");
			Check((string)type.GetField("_selectedSourceId", Instance)!.GetValue(testView)! == "1", "source selection awaits new snapshot rather than relabeling old content");
			Update(7, "5"); Check((string)type.GetField("_selectedSourceId", Instance)!.GetValue(testView)! == "5", "source selection is confirmed by incoming source ID");
			right.EmitSignal(Button.SignalName.Pressed); Check(!left.Disabled && right.Disabled && Names().SequenceEqual(new[] { "玩家 7" }), "source selector final page hides unused player slots");
			type.GetMethod("NavigateSources", Instance)!.Invoke(testView, new object[] { 1 }); Check(Names()[0] == "玩家 7", "source paging does not wrap after the final page");
			Check(testSelector.GetNode<TextureRect>("SpectatorSourceNext/Icon").Material is ShaderMaterial material && material.GetShaderParameter("s").AsSingle() == 0, "disabled source arrow is desaturated gray");
			Update(2); Check(left.Disabled && right.Disabled && Names().Length == 2, "shrinking source list clamps page and hides stale players");
			Check(before == Fingerprint(state.Players[0]), "source paging and selection fixtures leave the game unchanged");
		}
		finally { type.GetMethod("Dispose", Instance)!.Invoke(testView, null); }
		await Frames(2);
	}
	private static string Fingerprint(Player p) => JsonSerializer.Serialize(p.ToSerializable()) + ":" + p.PlayerCombatState?.Energy + ":" + string.Join(",", p.PlayerCombatState?.Hand.Cards.Select(c => c.Id + ":" + c.CurrentUpgradeLevel) ?? Array.Empty<string>());
	private static async Task Benchmark(RunState state, string page)
	{
		var source = Field("_source")!; var view = Field("_view")!;
		var capture = source.GetType().GetMethod("Capture", Instance)!;
		var update = view.GetType().GetMethod("Update", Instance)!;
		bool optimized = source.GetType().Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.SnapshotEquality") != null;
		var costs = new System.Collections.Generic.List<double>(); var frames = new System.Collections.Generic.List<double>();
		double captureMs = 0, jsonMs = 0, renderMs = 0; long allocated = 0;
		int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
		var watch = System.Diagnostics.Stopwatch.StartNew(); double lastFrame = watch.Elapsed.TotalMilliseconds;
		for (int i = 0; i < 60; i++)
		{
			await Frames(1); double frame = watch.Elapsed.TotalMilliseconds; frames.Add(frame - lastFrame); lastFrame = frame;
			_controller.GetMethod("CancelCapture", Static)?.Invoke(null, null);
			long bytes = GC.GetAllocatedBytesForCurrentThread(); double started = watch.Elapsed.TotalMilliseconds;
			var sample = capture.Invoke(source, new object[] { state })!; double captured = watch.Elapsed.TotalMilliseconds;
			var detached = optimized ? sample : sample.GetType().GetMethod("RoundTrip", Static)!.Invoke(null, new[] { sample })!;
			double serialized = watch.Elapsed.TotalMilliseconds; update.Invoke(view, new[] { detached }); double ended = watch.Elapsed.TotalMilliseconds;
			captureMs += captured - started; jsonMs += serialized - captured; renderMs += ended - serialized; costs.Add(ended - started);
			allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
		}
		double Percentile(System.Collections.Generic.List<double> values, double percentile) { var ordered = values.OrderBy(v => v).ToArray(); return ordered[(int)Math.Ceiling(ordered.Length * percentile) - 1]; }
		var stats = new { Page = page, Optimized = optimized, Samples = costs.Count, CaptureMs = captureMs / costs.Count, JsonMs = jsonMs / costs.Count, UpdateMs = renderMs / costs.Count, MeanMs = costs.Average(), P95Ms = Percentile(costs, .95), P99Ms = Percentile(costs, .99), AllocatedBytes = allocated / costs.Count, FrameP95Ms = Percentile(frames, .95), FrameP99Ms = Percentile(frames, .99), GC0 = GC.CollectionCount(0) - gen0, GC1 = GC.CollectionCount(1) - gen1, GC2 = GC.CollectionCount(2) - gen2, EngineMaxFps = Engine.MaxFps, EngineFPS = Engine.GetFramesPerSecond() };
		File.WriteAllText(Path.Combine(Output, page + "-performance.json"), JsonSerializer.Serialize(stats));
		GD.Print("[LiveSharingSmoke] PERFORMANCE " + JsonSerializer.Serialize(stats));
		if (source.GetType().GetField("_metrics", Instance)?.GetValue(source) is { } profile)
		{ File.WriteAllText(Path.Combine(Output, page + "-capture-stages.json"), JsonSerializer.Serialize(profile)); GD.Print("[LiveSharingSmoke] CAPTURE STAGES " + JsonSerializer.Serialize(profile)); }
	}
	private static void CheckOptimizations(RunState state)
	{
		var source = Field("_source")!; var view = Field("_view")!;
		var equalType = source.GetType().Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.SnapshotEquality");
		if (equalType == null) return; // The same benchmark also runs the old baseline.
		_controller.GetMethod("CancelCapture", Static)?.Invoke(null, null);
		var capture = source.GetType().GetMethod("Capture", Instance)!;
		var first = capture.Invoke(source, new object[] { state })!; var second = capture.Invoke(source, new object[] { state })!;
		var deckProperty = first.GetType().GetProperty("Deck")!;
		Check(ReferenceEquals(deckProperty.GetValue(first), deckProperty.GetValue(second)), "unchanged deck domain reuses read-only DTO graph");
		var detached = first.GetType().GetMethod("RoundTrip", Static)!.Invoke(null, new[] { first })!;
		var equal = equalType.GetMethod("Equal", Static, null, new[] { first.GetType(), first.GetType() }, null)!;
		Check((bool)equal.Invoke(null, new[] { first, detached })!, "typed comparison includes all round-tripped snapshot fields");
		var detachedDeck = (System.Collections.IList)deckProperty.GetValue(detached)!;
		var description = detachedDeck[0]!.GetType().GetProperty("Description")!;
		description.SetValue(detachedDeck[0], "changed snapshot description");
		Check(!(bool)equal.Invoke(null, new[] { first, detached })!, "typed comparison catches nested card text changes");
		Check(!ReferenceEquals(deckProperty.GetValue(first), deckProperty.GetValue(detached)), "transport test still detaches mutable DTO containers");
		state.Players[0].Deck.Cards[0].InvokeEnergyCostChanged();
		Check((bool)source.GetType().GetField("_deckDirty", Instance)!.GetValue(source)!, "native card notification invalidates hidden deck cache");
		var artPane = (Control)view.GetType().GetField("_screenArt", Instance)!.GetValue(view)!;
		var pageArt = (System.Collections.IList)first.GetType().GetProperty("PageArt")!.GetValue(first)!;
		var retained = (System.Collections.IDictionary)view.GetType().GetField("_retainedArt", Instance)!.GetValue(view)!;
		System.Collections.IDictionary Index() => (System.Collections.IDictionary)retained[artPane.GetInstanceId()]!.GetType().GetProperty("Index")!.GetValue(retained[artPane.GetInstanceId()])!;
		ulong DrawingId(object value) => ((CanvasItem)value.GetType().GetProperty("Drawing")!.GetValue(value)!).GetInstanceId();
		var before = Index().Keys.Cast<string>().ToDictionary(key => key, key => DrawingId(Index()[key]!));
		var parents = pageArt.Cast<object>().Select(a => (string)a.GetType().GetProperty("Parent")!.GetValue(a)!).ToHashSet();
		var leaf = pageArt.Cast<object>().First(a => ((string)a.GetType().GetProperty("Texture")!.GetValue(a)!).Length > 0 && !parents.Contains((string)a.GetType().GetProperty("Key")!.GetValue(a)!));
		var reduced = (System.Collections.IList)Activator.CreateInstance(pageArt.GetType())!;
		foreach (var a in pageArt) if (!ReferenceEquals(a, leaf)) reduced.Add(a);
		var retain = view.GetType().GetMethod("RetainArt", Instance)!;
		retain.Invoke(view, new object[] { artPane, reduced });
		Check(Index().Keys.Cast<string>().All(key => before[key] == DrawingId(Index()[key]!)), "map visibility change retains surviving native art nodes");
		retain.Invoke(view, new object[] { artPane, pageArt });
		var big = first.GetType().GetMethod("RoundTrip", Static)!.Invoke(null, new[] { first })!;
		big.GetType().GetProperty("Page")!.SetValue(big, "deck");
		var bigDeck = (System.Collections.IList)deckProperty.GetValue(big)!; var originals = bigDeck.Cast<object>().ToArray(); bigDeck.Clear();
		for (int i = 0; i < 200; i++) bigDeck.Add(originals[i % originals.Length]);
		view.GetType().GetMethod("Update", Instance)!.Invoke(view, new[] { big });
		Check(Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<NCard>().Count() < 40, "200-card deck creates only visible and buffered rows");
		view.GetType().GetMethod("OpenInspect", Instance)!.Invoke(view, new object[] { bigDeck, 199, false });
		view.GetType().GetMethod("NavigateInspect", Instance)!.Invoke(view, new object[] { 1 });
		Check((int)view.GetType().GetField("_inspectIndex", Instance)!.GetValue(view)! == 199, "virtualized deck inspection still reaches last card without wrapping");
		view.GetType().GetMethod("CloseInspect", Instance)!.Invoke(view, null);
		view.GetType().GetMethod("Update", Instance)!.Invoke(view, new[] { second });
	}
	private static async Task BenchmarkLive(string page)
	{
		var times = new System.Collections.Generic.List<double>(); var slices = new System.Collections.Generic.List<double>();
		var renders = new System.Collections.Generic.List<double>(); var acquire = new System.Collections.Generic.List<double>();
		var watch = System.Diagnostics.Stopwatch.StartNew(); double last = 0;
		for (int i = 0; i < 180; i++)
		{
			await Frames(1); double now = watch.Elapsed.TotalMilliseconds; times.Add(now - last); last = now;
			if (_controller.GetField("_lastCaptureSliceMs", Static)?.GetValue(null) is double slice && slice > 0)
			{ slices.Add(slice); double render = (double)(_controller.GetField("_lastCaptureRenderMs", Static)?.GetValue(null) ?? 0d); if (render > 0) renders.Add(render); acquire.Add(slice - render); }
		}
		double Percentile(System.Collections.Generic.List<double> values, double p) { var sorted = values.OrderBy(v => v).ToArray(); return sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * p) - 1]; }
		var stats = new { Page = page, Frames = times.Count, FrameP95Ms = Percentile(times, .95), FrameP99Ms = Percentile(times, .99), FramesOver33Ms = times.Count(t => t > 33), FramesOver50Ms = times.Count(t => t > 50), CaptureSlices = slices.Count, SliceP95Ms = Percentile(slices, .95), SliceP99Ms = Percentile(slices, .99), AcquireP95Ms = Percentile(acquire, .95), RenderP95Ms = Percentile(renders, .95), LastCaptureFrames = _controller.GetField("_lastCaptureFrames", Static)?.GetValue(null), LastCaptureCpuMs = _controller.GetField("_lastCaptureTotalMs", Static)?.GetValue(null), EngineFPS = Engine.GetFramesPerSecond() };
		File.WriteAllText(Path.Combine(Output, page + "-live-performance.json"), JsonSerializer.Serialize(stats)); GD.Print("[LiveSharingSmoke] LIVE PERFORMANCE " + JsonSerializer.Serialize(stats));
		if (slices.Count > 0 && page == "map") Check((int)_controller.GetField("_lastCaptureFrames", Static)!.GetValue(null)! > 1, "complex map capture is spread across game frames");
	}
	private static async Task SaveFrame(string name, int waitFrames = 30, bool keepPreview = false)
	{
		await Frames(waitFrames);
		if (DisplayServer.GetName() == "headless") return;
		var overlay = Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator");
		await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		var view = Field("_view")!;
		((Control)view.GetType().GetField("_preview", Instance)!.GetValue(view)!).Visible = keepPreview;
		await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		((SubViewport)view.GetType().GetField("_viewport", Instance)!.GetValue(view)!).GetTexture().GetImage().SavePng(Path.Combine(Output, name + ".png"));
		Game.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Output, name + "-embedded.png"));
		overlay.Hide(); await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Game.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Output, name + "-original.png"));
		overlay.Show();
		((Control)view.GetType().GetField("_preview", Instance)!.GetValue(view)!).Visible = true;
		// Screenshot capture temporarily hides the overlay. Allow the viewport's
		// GUI hit testing to observe it again before the next synthetic click.
		await Frames(2);
	}
	private static async Task Inspect(RunState state, string page, int minimumCards, int waitFrames = 40)
	{
		await Frames(waitFrames);
		_controller.GetMethod("CancelCapture", Static)?.Invoke(null, null);
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
		var rendered = Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<NCard>().ToList();
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
			RunManager.Instance.SetUpNewSingleplayer(state, true);
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
			int windowCount = Descendants(Game.GetTree().Root).OfType<Window>().Count();
			int foregroundFps = Engine.MaxFps;
			bool initialFocus = DisplayServer.WindowIsFocused();
			await Key(Godot.Key.F8);
			Check(Field("_view") != null, "F8 opens spectator");
			await SaveFrame("default-layout", 0);
			Check(Descendants(Game.GetTree().Root).OfType<Window>().Count() == windowCount, "embedded spectator creates no native or embedded Window");
			Check(DisplayServer.WindowIsFocused() != initialFocus || Engine.MaxFps == foregroundFps, "opening embedded panel preserves FPS limit when window focus is unchanged");
			await Inspect(state, "shop", 7);
			await CheckSourceSelector(state);
			await CheckPanelInput(state.Players[0]);
			await CheckSourcePointer(state);
			var closeView = Field("_view")!;
			var closePanel = (Control)closeView.GetType().GetField("_panel", Instance)!.GetValue(closeView)!;
			var savedPosition = closePanel.Position; var savedSize = closePanel.Size;
			await Click(closePanel.GetNode<Button>("SpectatorClose").GetGlobalRect().GetCenter());
			Check(Field("_view") == null, "real pointer red cross closes spectator");
			await Key(Godot.Key.F8); Check(Field("_view") != null, "F8 reopens spectator after red cross close");
			var reopenedPanel = (Control)Field("_view")!.GetType().GetField("_panel", Instance)!.GetValue(Field("_view"))!;
			Check(reopenedPanel.Position == savedPosition && reopenedPanel.Size == savedSize, "F8 reopen restores saved position and size");
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
			await CheckDeckUpgrade(state);
			File.WriteAllLines(Path.Combine(Output, "deck-tree.txt"), Descendants(nativeDeck!).Where(n => n is Control).Select(n => n.GetPath() + " " + n.GetType().Name + "/" + n.GetClass() + " " + ((Control)n).GetGlobalRect()));
			MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance!.Close();
			await Frames(30);
			await Inspect(state, "combat", 1);
			await CheckNewInteractions(state);
			await CheckDocking(state);
			await BenchmarkLive("combat");
			var intentValue = Descendants(NCombatRoom.Instance).OfType<MegaCrit.Sts2.Core.Nodes.Combat.NIntent>().First().GetNode<MegaRichTextLabel>("%Value");
			string oldIntent = intentValue.Text; intentValue.Text = "123[font_size=18]×12[/font_size]"; await Frames(5);
			await Inspect(state, "combat", 1, 0); await SaveFrame("intent-multidigit");
			Check(Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<MegaRichTextLabel>().Any(l => l.Text == intentValue.Text && l.GetContentHeight() <= l.Size.Y && l.GetContentWidth() <= l.Size.X), "multi-digit and multi-hit intent text fits its rendered rectangle");
			intentValue.Text = oldIntent; await Frames(10);
			string beforeDeck = Fingerprint(state.Players[0]);
			var view = Field("_view")!;
			var deck = (Button)view.GetType().GetField("_deck", Instance)!.GetValue(view)!;
			deck.EmitSignal(Button.SignalName.Pressed);
			await Frames(30);
			Check(Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<NCard>().Count() == state.Players[0].Deck.Cards.Count, "spectator deck shows all cards");
			Check(beforeDeck == Fingerprint(state.Players[0]), "deck browsing leaves player unchanged");
			await CheckDeckUpgrade(state);
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
			await CheckInspectUpgrade("deck");
			view.GetType().GetMethod("NavigateInspect", Instance)!.Invoke(view, new object[] { -1 });
			Check((int)view.GetType().GetField("_inspectIndex", Instance)!.GetValue(view)! == 0, "first card has no previous or wrap");
			await SaveFrame("inspect");
			await Click(SpectatorPoint(Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<Button>().Single(b => b.Name == "SpectatorInspectUpgrade").GetGlobalRect().GetCenter()));
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
			await Click(SpectatorPoint(Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<Button>().Single(b => b.Name == "SpectatorInspectUpgrade").GetGlobalRect().GetCenter()));
			await SaveFrame("inspect-base");
			Check(beforeDeck == Fingerprint(state.Players[0]), "downgrade comparison leaves original upgraded card unchanged");
			view.GetType().GetMethod("CloseInspect", Instance)!.Invoke(view, null);
			var panel = Game.GetTree().Root.GetNode<Control>("RmpLocalSpectator/SpectatorPanel");
			Check(panel.GetGlobalRect().Size.X > 600 && panel.GetGlobalRect().End.X <= Game.GetViewport().GetVisibleRect().Size.X, "embedded panel fits game viewport");
			Check(panel.Size.X >= 640f * 5 / 6, "saved panel respects reduced minimum size");
			Check(((SubViewport)view.GetType().GetField("_viewport", Instance)!.GetValue(view)!).Size == new Vector2I(1920, 1080), "small panel preserves full 1920x1080 rendering resolution");
			deck.EmitSignal(Button.SignalName.Pressed);
			var map = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance!.Open(true);
			await Frames(90);
			map.Drawings.BeginLineLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(700, 500), MegaCrit.Sts2.Core.Nodes.Screens.Map.DrawingMode.Drawing);
			map.Drawings.UpdateCurrentLinePositionLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(1100, 750)); map.Drawings.UpdateCurrentLinePositionLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(1500, 650)); map.Drawings.StopLineLocal();
			await Inspect(state, "map", 0);
			await BenchmarkLive("map");
			var artPane = (Control)view.GetType().GetField("_screenArt", Instance)!.GetValue(view)!;
			await Benchmark(state, "map");
			var currentSnapshot = view.GetType().GetField("_snapshot", Instance)!.GetValue(view)!;
			var originalIds = Descendants(artPane).Select(n => n.GetInstanceId()).ToArray();
			view.GetType().GetMethod("Update", Instance)!.Invoke(view, new[] { currentSnapshot });
			Check(originalIds.SequenceEqual(Descendants(artPane).Select(n => n.GetInstanceId())), "map refresh retains native drawing nodes");
			CheckOptimizations(state);
			map.Drawings.BeginLineLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(900, 600), MegaCrit.Sts2.Core.Nodes.Screens.Map.DrawingMode.Erasing); map.Drawings.UpdateCurrentLinePositionLocal(map.Drawings.GetGlobalTransform().AffineInverse() * new Vector2(1400, 700)); map.Drawings.StopLineLocal();
			await Inspect(state, "map", 0); await SaveFrame("map-erased");
			map.Close(); await Frames(30);
			var lootSet = new MegaCrit.Sts2.Core.Rewards.RewardsSet(state.Players[0]).WithCustomRewards(new System.Collections.Generic.List<MegaCrit.Sts2.Core.Rewards.Reward> { new MegaCrit.Sts2.Core.Rewards.GoldReward(10, state.Players[0]) });
			var loot = MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen.ShowScreen(lootSet, false, state); await Inspect(state, "loot", 0);
			await CheckNativeRelicAndLootMap(state, loot);
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
			await CheckMultipleRelics(state);
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
			var closingSource = Field("_source")!;
			Descendants(Game.GetTree().Root.GetNode<CanvasLayer>("RmpLocalSpectator")).OfType<Button>().Single(b => b.Name == "SpectatorClose").EmitSignal(Button.SignalName.Pressed);
			await Frames(5); Check(Field("_view") == null, "embedded panel close disposes spectator");
			if (closingSource.GetType().GetField("_watchedDeck", Instance) is { } watchedDeck)
			{
				var cards = closingSource.GetType().GetField("_watchedCards", Instance)!.GetValue(closingSource)!;
				Check(watchedDeck.GetValue(closingSource) == null && (int)cards.GetType().GetProperty("Count")!.GetValue(cards)! == 0, "closing spectator detaches native card and pile subscriptions");
			}
			await Key(Godot.Key.F9); Check(Field("_view") != null, "spectator can reopen");
			await CheckRunRestoration(state);
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
