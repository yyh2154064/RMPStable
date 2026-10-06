using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

public static partial class Smoke
{
	private static async Task CheckInspectUpgrade(string context)
	{
		var view = Field("_view")!; var type = view.GetType();
		var inspect = (Control)type.GetField("_inspect", Instance)!.GetValue(view)!;
		await Frames(3);
		System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, context + "-inspect-tree.txt"), Descendants(inspect).OfType<Control>().Select(c => c.GetPath() + " " + c.GetGlobalRect() + " " + c.MouseFilter + " z=" + c.ZIndex + " clip=" + c.ClipContents + " visible=" + c.IsVisibleInTree()));
		var button = Descendants(inspect).OfType<Button>().Single(b => b.Name == "SpectatorInspectUpgrade");
		bool before = (bool)type.GetField("_inspectUpgraded", Instance)!.GetValue(view)!;
		await Click(SpectatorPoint(button.GetGlobalRect().GetCenter()));
		Check((bool)type.GetField("_inspectUpgraded", Instance)!.GetValue(view)! != before, context + " real pointer toggles inspect upgrade");
		await SaveFrame(context + "-inspect-upgraded", 0);
		button = Descendants(inspect).OfType<Button>().Single(b => b.Name == "SpectatorInspectUpgrade");
		await Click(SpectatorPoint(button.GetGlobalRect().GetCenter()));
		Check((bool)type.GetField("_inspectUpgraded", Instance)!.GetValue(view)! == before, context + " real pointer restores inspect base");
	}
	private static object Prop(object value, string property) => value.GetType().GetProperty(property, Instance)!.GetValue(value)!;
	private static object CaptureNow(RunState state)
	{
		_controller.GetMethod("CancelCapture", Static)!.Invoke(null, null);
		var source = Field("_source")!;
		var snapshot = source.GetType().GetMethod("Capture", Instance)!.Invoke(source, new object[] { state })!;
		Field("_view")!.GetType().GetMethod("Update", Instance)!.Invoke(Field("_view"), new[] { snapshot });
		return snapshot;
	}
	private static Vector2 SpectatorPoint(Vector2 point)
	{
		var view = Field("_view")!;
		var panel = (Control)view.GetType().GetField("_panel", Instance)!.GetValue(view)!;
		return panel.GetNode<SubViewportContainer>("SpectatorContent/SpectatorSurface").GetGlobalTransform() * point;
	}
	private static async Task CheckDeckUpgrade(RunState state)
	{
		CaptureNow(state); await Frames(3);
		var view = Field("_view")!; var type = view.GetType();
		var button = Descendants((Control)type.GetField("_cards", Instance)!.GetValue(view)!).OfType<Button>().Single(b => b.Name == "SpectatorDeckUpgrade");
		System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "spectator-deck-input-tree.txt"), Descendants((Control)type.GetField("_canvas", Instance)!.GetValue(view)!).OfType<Control>().Select(c => c.GetPath() + " " + c.GetGlobalRect() + " " + c.MouseFilter + " z=" + c.ZIndex + " top=" + c.TopLevel + " clip=" + c.ClipContents + " visible=" + c.IsVisibleInTree()));
		string before = Fingerprint(state.Players[0]);
		await Click(SpectatorPoint(button.GetGlobalRect().GetCenter()));
		Check((bool)type.GetField("_deckUpgraded", Instance)!.GetValue(view)!, "native deck page upgrade checkbox responds to real pointer");
		Check(Descendants((Control)type.GetField("_cards", Instance)!.GetValue(view)!).OfType<NCard>().All(c => c.GetNode<Label>("%TitleLabel").Text.Contains('+')), "view upgrades previews every upgradable deck card");
		await SaveFrame("deck-all-upgraded");
		Check(before == Fingerprint(state.Players[0]), "view-all-upgrades never upgrades live deck");
		button = Descendants((Control)type.GetField("_cards", Instance)!.GetValue(view)!).OfType<Button>().Single(b => b.Name == "SpectatorDeckUpgrade");
		await Click(SpectatorPoint(button.GetGlobalRect().GetCenter()));
	}
	private static async Task CheckNewInteractions(RunState state)
	{
		// Populate both otherwise-empty piles with real combat cards in this disposable run.
		var combatState = state.Players[0].PlayerCombatState!;
		foreach (var destination in new[] { combatState.DiscardPile, combatState.ExhaustPile })
		{
			var card = combatState.DrawPile.Cards.First(); combatState.DrawPile.RemoveInternal(card); destination.AddInternal(card);
		}
		var snapshot = CaptureNow(state); var view = Field("_view")!; var type = view.GetType();
		var hand = Descendants((Control)type.GetField("_cards", Instance)!.GetValue(view)!).OfType<NCard>().First();
		// Resting fan-card centers can lie below the viewport; hit the visible
		// lower portion instead of injecting a pointer outside the clipped panel.
		var handPoint = hand.GetGlobalTransform().Origin; handPoint.Y = Math.Min(handPoint.Y, 1020);
		await Pointer(SpectatorPoint(handPoint));
		var enlarged = Descendants((Control)type.GetField("_preview", Instance)!.GetValue(view)!).OfType<NCard>().Single();
		Check(Math.Abs(enlarged.GlobalPosition.Y + 211 * enlarged.Scale.Y - 1080) < 1, "hovered combat hand card bottom stays at viewport bottom");
		await SaveFrame("hand-hover-bottom", 0, true);
		await Click(SpectatorPoint(handPoint)); await CheckInspectUpgrade("hand");
		type.GetMethod("CloseInspect", Instance)!.Invoke(view, null);
		var hovers = ((System.Collections.IEnumerable)Prop(snapshot, "HudHovers")).Cast<object>().ToList();
		Check(hovers.Count >= 6, "native health gold room boss map and potion descriptions captured");
		foreach (var hover in hovers)
		{
			float[] r = (float[])Prop(hover, "Rect"); var scale = ((Control)type.GetField("_page", Instance)!.GetValue(view)!).Scale;
			await Pointer(SpectatorPoint(new Vector2(r[0] + r[2] / 2, r[1] + r[3] / 2) * scale));
			Check(Descendants((Control)type.GetField("_preview", Instance)!.GetValue(view)!).OfType<VBoxContainer>().Any(), "native HUD target displays spectator hover description");
		}
		await Frames(90);
		Check(Descendants((Control)type.GetField("_preview", Instance)!.GetValue(view)!).OfType<VBoxContainer>().Any(), "stationary HUD hover survives timer and unchanged snapshot refreshes");
		await SaveFrame("hud-description");
		var relics = ((System.Collections.IEnumerable)Prop(snapshot, "Relics")).Cast<object>().ToList();
		Check(relics.Count == state.Players[0].Relics.Count, "relic detail data matches owned relics");
		float[] relicRect = (float[])Prop(relics[0], "Rect"); var pageScale = ((Control)type.GetField("_page", Instance)!.GetValue(view)!).Scale;
		await Click(SpectatorPoint(new Vector2(relicRect[0] + relicRect[2] / 2, relicRect[1] + relicRect[3] / 2) * pageScale));
		Check((int)type.GetField("_relicIndex", Instance)!.GetValue(view)! == 0, "real pointer opens owned relic native detail");
		type.GetMethod("NavigateRelic", Instance)!.Invoke(view, new object[] { -1 });
		Check((int)type.GetField("_relicIndex", Instance)!.GetValue(view)! == 0, "relic previous does not wrap");
		for (int i = 0; i < relics.Count + 2; i++) type.GetMethod("NavigateRelic", Instance)!.Invoke(view, new object[] { 1 });
		Check((int)type.GetField("_relicIndex", Instance)!.GetValue(view)! == relics.Count - 1, "relic next stops at final owned relic");
		await SaveFrame("relic-inspect"); type.GetMethod("CloseInspect", Instance)!.Invoke(view, null);
		string before = Fingerprint(state.Players[0]);
		var source = Field("_source")!;
		foreach (string kind in new[] { "Draw", "Discard", "Exhaust" })
		{
			snapshot = CaptureNow(state);
			var target = ((System.Collections.IEnumerable)Prop(snapshot, "Piles")).Cast<object>().Single(p => (string)Prop(p, "Kind") == kind);
			float[] r = (float[])Prop(target, "Rect");
			await Click(SpectatorPoint(new Vector2(r[0] + r[2] / 2, r[1] + r[3] / 2) * pageScale));
			await Frames(40); snapshot = CaptureNow(state);
			Check((string)Prop(view, "BrowsePile") == kind, "real pointer opens " + kind + " pile in spectator");
			var browse = (Control)type.GetField("_browse", Instance)!.GetValue(view)!;
			Check(Descendants(browse).Any(n => n.Name == "CardGrid"), "pile uses native card-pile grid shell");
			var cards = Descendants(browse).OfType<NCard>().ToList();
			await SaveFrame("pile-" + kind.ToLowerInvariant() + "-grid", 0);
			var grid = Descendants(browse).OfType<Control>().Single(c => c.Name == "CardGrid");
			var content = grid.GetNode<Control>("%ScrollContainer");
			Check(grid.GetNode<Control>("Scrollbar").Visible == (content.Size.Y > grid.Size.Y + 320), "pile scrollbar visibility follows native grid overflow threshold");
			Check(Math.Abs(content.Position.Y - Math.Max(0, (grid.Size.Y - content.Size.Y) * 0.5f)) < 1, "pile initial vertical centering matches native grid");
			System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "pile-" + kind + "-tree.txt"), Descendants(browse).OfType<Control>().Select(c => c.GetPath() + " " + c.GetGlobalRect() + " " + c.MouseFilter + " clip=" + c.ClipContents + " visible=" + c.IsVisibleInTree()));
			if (cards.Count > 0)
			{
				await Pointer(SpectatorPoint(cards[0].GetGlobalTransform().Origin));
				Check(Descendants((Control)type.GetField("_preview", Instance)!.GetValue(view)!).OfType<NCard>().Any(), "pile card hover enlarges card");
				await Click(SpectatorPoint(cards[0].GetGlobalTransform().Origin));
				Check((int)type.GetField("_inspectIndex", Instance)!.GetValue(view)! == 0, "pile card click opens first detail");
				await CheckInspectUpgrade(kind);
				type.GetMethod("NavigateInspect", Instance)!.Invoke(view, new object[] { -1 });
				Check((int)type.GetField("_inspectIndex", Instance)!.GetValue(view)! == 0, "pile first detail does not wrap");
				for (int i = 0; i < 30; i++) type.GetMethod("NavigateInspect", Instance)!.Invoke(view, new object[] { 1 });
				Check((int)type.GetField("_inspectIndex", Instance)!.GetValue(view)! == ((System.Collections.IEnumerable)Prop(((System.Collections.IEnumerable)Prop(snapshot, "Piles")).Cast<object>().Single(p => (string)Prop(p, "Kind") == kind), "Cards")).Cast<object>().Count() - 1, "pile detail navigation stops at final card");
				type.GetMethod("CloseInspect", Instance)!.Invoke(view, null);
			}
			await SaveFrame("pile-" + kind.ToLowerInvariant());
			Descendants(browse).OfType<Button>().Single(b => b.Name == "SpectatorDeckBack").EmitSignal(Button.SignalName.Pressed);
		}
		Check(before == Fingerprint(state.Players[0]), "pile inspection and relic details leave live game unchanged");
		await Key(Godot.Key.F5); await Frames(45);
		snapshot = CaptureNow(state);
		Check(((System.Collections.IEnumerable)Prop(snapshot, "ModalLabels")).Cast<object>().Any(l => ((string)Prop(l, "Text")).Contains("SL")), "real F5 dialog title captured outside NRun");
		Check(Descendants((Control)type.GetField("_modal", Instance)!.GetValue(view)!).OfType<Label>().Any(l => l.Text.Contains("SL")), "real F5 dialog renders above spectator page");
		await SaveFrame("f5-confirmation");
		NModalContainer.Instance!.Clear(); await Frames(30);
		snapshot = CaptureNow(state); Check(!((System.Collections.IEnumerable)Prop(snapshot, "ModalArt")).Cast<object>().Any(), "dismissed F5 dialog clears spectator overlay");
		source.GetType().GetMethod("SelectPreviewSource", Instance)!.Invoke(source, new object[] { "local-preview:4" }); await Frames(60);
		var panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!; var position = panel.Position; var size = panel.Size;
		await Key(Godot.Key.F9); await Key(Godot.Key.F9); await Frames(60);
		view = Field("_view")!; panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		Check((string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)! == "local-preview:4" && panel.Position == position && panel.Size == size, "close reopen preserves fourth source and geometry");
		_controller.GetMethod("Suspend", Static)!.Invoke(null, null); await Frames(60);
		view = Field("_view")!; panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		Check(panel.Position == position && panel.Size == size && (string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)! == "local-preview:4", "transient run teardown restores open state source and geometry");
	}
	private static async Task CheckRunRestoration(RunState state)
	{
		var view = Field("_view")!; var type = view.GetType();
		var panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		var position = panel.Position; var size = panel.Size;
		string selected = (string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)!;
		await MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SaveRun(null, false);
		Check(MegaCrit.Sts2.Core.Saves.SaveManager.Instance.LoadRunSave().Success, "isolated native checkpoint is available for real F5 reload");
		ulong oldRun = MegaCrit.Sts2.Core.Nodes.NRun.Instance!.GetInstanceId();
		await Key(Godot.Key.F5); await Frames(30);
		var popup = (Control)NModalContainer.Instance!.OpenModal!;
		await Click(popup.GetNode<Control>("VerticalPopup/YesButton").GetGlobalRect().GetCenter());
		for (int i = 0; i < 900 && (MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GetInstanceId() == oldRun || Field("_view") == null); i++) await Frames(1);
		await Frames(90);
		Check(MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GetInstanceId() != oldRun && Field("_view") != null, "real F5 reload restores spectator automatically");
		view = Field("_view")!; panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		Check(panel.Position == position && panel.Size == size && (string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)! == selected, "real F5 preserves saved source position and size: before=" + position + "/" + size + "/" + selected + " after=" + panel.Position + "/" + panel.Size + "/" + type.GetField("_selectedSourceId", Instance)!.GetValue(view));
		await SaveFrame("f5-restored");
		RunManager.Instance.CleanUp(); await Frames(10);
		var character = MegaCrit.Sts2.Core.Models.ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>();
		var next = RunState.CreateForNewRun(new[] { MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(character, MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress(), 1) }, MegaCrit.Sts2.Core.Models.ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), Array.Empty<MegaCrit.Sts2.Core.Models.ModifierModel>(), MegaCrit.Sts2.Core.Runs.GameMode.Standard, 0, "RMPNEXTTEST");
		RunManager.Instance.SetUpNewSingleplayer(next, false); RunManager.Instance.Launch();
		Game.RootSceneContainer.SetCurrentScene(MegaCrit.Sts2.Core.Nodes.NRun.Create(next));
		await RunManager.Instance.SetActInternal(0);
		RunManager.Instance.RunLocationTargetedBuffer.OnLocationChanged(next.RunLocation); RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(next.MapLocation);
		await RunManager.Instance.EnterRoomDebug(MegaCrit.Sts2.Core.Rooms.RoomType.Shop); await Frames(90);
		view = Field("_view")!; panel = (Control)type.GetField("_panel", Instance)!.GetValue(view)!;
		Check(panel.Position == position && panel.Size == size && (string)type.GetField("_selectedSourceId", Instance)!.GetValue(view)! == selected, "next run restores open spectator source position and size");
		var preferences = type.Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.SpectatorPreferences")!;
		preferences.GetField("_path", Static)!.SetValue(null, ""); await Frames(90);
		Check(Field("_view") != null, "reloading profile preferences from disk restores enabled spectator");
		_controller.GetMethod("Close", Static)!.Invoke(null, null); await Frames(10);
		preferences.GetField("_path", Static)!.SetValue(null, ""); await Frames(30);
		Check(Field("_view") == null, "explicit closed state survives preferences reload from disk");
		await Key(Godot.Key.F9);
	}
	private static async Task CheckMultipleRelics(RunState state)
	{
		var snapshot = CaptureNow(state); var view = Field("_view")!; var type = view.GetType();
		var relics = ((System.Collections.IEnumerable)Prop(snapshot, "Relics")).Cast<object>().ToList();
		Check(relics.Count >= 2, "multiple owned relics available for real detail navigation");
		var scale = ((Control)type.GetField("_page", Instance)!.GetValue(view)!).Scale;
		float[] r = (float[])Prop(relics[0], "Rect");
		await Click(SpectatorPoint(new Vector2(r[0] + r[2] / 2, r[1] + r[3] / 2) * scale));
		var inspect = (Control)type.GetField("_inspect", Instance)!.GetValue(view)!;
		for (int i = 1; i < relics.Count; i++)
		{
			var next = Descendants(inspect).OfType<Button>().Single(b => b.Name == "SpectatorRelicNext");
			await Click(SpectatorPoint(next.GetGlobalRect().GetCenter()));
			Check((int)type.GetField("_relicIndex", Instance)!.GetValue(view)! == i, "real pointer navigates to next owned relic");
		}
		Check(!Descendants(inspect).OfType<Button>().Single(b => b.Name == "SpectatorRelicNext").IsVisibleInTree(), "last relic hides next arrow");
		await SaveFrame("relic-multiple");
		for (int i = relics.Count - 2; i >= 0; i--)
		{
			var previous = Descendants(inspect).OfType<Button>().Single(b => b.Name == "SpectatorRelicPrevious");
			await Click(SpectatorPoint(previous.GetGlobalRect().GetCenter()));
			Check((int)type.GetField("_relicIndex", Instance)!.GetValue(view)! == i, "real pointer navigates to previous owned relic");
		}
		Check(!Descendants(inspect).OfType<Button>().Single(b => b.Name == "SpectatorRelicPrevious").IsVisibleInTree(), "first relic hides previous arrow");
		type.GetMethod("CloseInspect", Instance)!.Invoke(view, null);
	}
}
