using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

public static partial class Smoke
{
	private static object ControlView => Field("_view")!;
	private static object ControlData => Prop(ControlView.GetType().GetField("_snapshot", Instance)!.GetValue(ControlView)!, "Control");
	private static object[] ControlActions => ((IEnumerable)Prop(ControlData, "Actions")).Cast<object>().ToArray();
	private static async Task Until(Func<bool> predicate, string label, double timeout = 12)
	{
		var watch = System.Diagnostics.Stopwatch.StartNew();
		while (!predicate() && watch.Elapsed.TotalSeconds < timeout) await Frames(1);
		Check(predicate(), label);
	}
	private static async Task ToggleControl(bool enabled)
	{
		var view = ControlView; var panel = (Control)view.GetType().GetField("_panel", Instance)!.GetValue(view)!;
		if ((bool)view.GetType().GetField("_controlEnabled", Instance)!.GetValue(view)! != enabled) await Click(panel.GetNode<Button>("SpectatorControlToggle").GetGlobalRect().GetCenter());
		Check((bool)view.GetType().GetField("_controlEnabled", Instance)!.GetValue(view)! == enabled, "real pointer switches control=" + enabled);
	}
	private static Vector2 CardControlPoint(object card)
	{
		var t = (float[])Prop(card, "Transform"); return new Vector2(t[4], Math.Min(t[5], 1020));
	}
	private static object SnapshotCard(string id)
	{
		var snapshot = ControlView.GetType().GetField("_snapshot", Instance)!.GetValue(ControlView)!;
		return ((IEnumerable)Prop(snapshot, "Cards")).Cast<object>().First(c => (string)Prop(c, "ControlId") == id);
	}
	private static async Task RefreshControl()
	{
		_controller.GetMethod("CancelCapture", Static)!.Invoke(null, null);
		_controller.GetField("_captureTimer", Static)!.SetValue(null, 0d); await Seconds(0.8);
	}
	private static async Task CheckLocalControls(RunState state)
	{
		var player = state.Players[0];
		Check(!(bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)!, "control defaults off");
		await ToggleControl(true);
		var reward = new CardReward(new CardModel[] { state.CreateCard<StrikeIronclad>(player), state.CreateCard<DefendIronclad>(player), state.CreateCard<Bash>(player) }, CardCreationSource.Other, player, null!);
		int originalDeck = player.Deck.Cards.Count; var rewardTask = reward.SelectUnsynchronized();
		await Until(() => NOverlayStack.Instance?.Peek() is NCardRewardSelectionScreen, "native reward selection opened"); await RefreshControl();
		var choose = ControlActions.First(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled"));
		await SaveFrame("control-reward-ready", 0);
		await Click(SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(choose, "CardId")))));
		await Until(() => rewardTask.IsCompleted, "spectator pointer completes original card reward flow");
		Check(await rewardTask && player.Deck.Cards.Count == originalDeck + 1, "reward choice obtains one real deck card");
		await RefreshControl();
		var skipReward = new CardReward(new[] { state.CreateCard<StrikeIronclad>(player) }, CardCreationSource.Other, player, null!);
		var skipTask = skipReward.SelectUnsynchronized(); await Until(() => NOverlayStack.Instance?.Peek() is NCardRewardSelectionScreen, "second native reward opened"); await RefreshControl();
		var skip = ControlActions.First(a => (string)Prop(a, "Kind") == "choice" && (bool)Prop(a, "Enabled")); var skipRect = (float[])Prop(skip, "Rect");
		await Click(SpectatorPoint(new Vector2(skipRect[0] + skipRect[2] / 2, skipRect[1] + skipRect[3] / 2)));
		await Until(() => skipTask.IsCompleted, "spectator pointer skips original reward flow"); Check(!await skipTask && player.Deck.Cards.Count == originalDeck + 1, "skip does not add a card");
		var selector = NDeckCardSelectScreen.Create(player.Deck.Cards.Take(4).ToArray(), new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 2) { RequireManualConfirmation = true });
		NOverlayStack.Instance!.Push(selector); var selectTask = selector.CardsSelected(); await RefreshControl();
		var selections = ControlActions.Where(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled")).Take(2).ToArray();
		Check(selections.Length == 2, "deck selector exposes distinct card choices");
		foreach (var choice in selections) { await Click(SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(choice, "CardId"))))); await RefreshControl(); }
		Check(selector.GetNode<Control>("%PreviewContainer").Visible, "two spectator choices open native confirmation preview");
		Check(!ControlActions.Any(a => (string)Prop(a, "Kind") == "select"), "preview prevents changing underlying selections");
		var nativeConfirm = selector.GetNode<Control>("%PreviewConfirm");
		await Click(SpectatorPoint(nativeConfirm.GetGlobalRect().GetCenter())); await Until(() => selectTask.IsCompleted, "spectator pointer confirms original multi-card selector");
		Check((await selectTask).Count() == 2 && player.Deck.Cards.Count == originalDeck + 1, "selector returns two real cards without extra deck mutation"); NOverlayStack.Instance.Remove(selector);
		await ToggleControl(false);
		await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<ExoskeletonsWeak>().ToMutable()); await Seconds(2); await RefreshControl();
		var snapshot = ControlView.GetType().GetField("_snapshot", Instance)!.GetValue(ControlView)!;
		var cards = ((IEnumerable)Prop(snapshot, "Cards")).Cast<object>().ToArray();
		Check(cards.Select(c => (string)Prop(c, "ControlId")).Distinct().Count() == cards.Length, "identical hand cards have distinct control IDs");
		string fingerprint = Fingerprint(player); await Click(SpectatorPoint(CardControlPoint(cards[0])));
		Check(fingerprint == Fingerprint(player), "watch mode hand click remains read only"); ControlView.GetType().GetMethod("CloseInspect", Instance)!.Invoke(ControlView, null);
		await ToggleControl(true); await RefreshControl();
		var handSelection = NPlayerHand.Instance!.SelectCards(new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1) { RequireManualConfirmation = true }, null, null);
		await RefreshControl();
		Check(!ControlActions.Any(a => (string)Prop(a, "Kind") == "play"), "native hand selection replaces play commands");
		var handChoice = ControlActions.First(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled"));
		await Click(SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(handChoice, "CardId"))))); await RefreshControl();
		await Click(SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(handChoice, "CardId"))))); await RefreshControl();
		Check(!ControlActions.Any(a => ((string)Prop(a, "Id")).StartsWith("hand-confirm:") && (bool)Prop(a, "Enabled")) && !handSelection.IsCompleted, "deselecting hand card preserves native minimum requirement");
		handChoice = ControlActions.First(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled"));
		await Click(SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(handChoice, "CardId"))))); await RefreshControl();
		var handConfirm = ControlActions.First(a => ((string)Prop(a, "Id")).StartsWith("hand-confirm:") && (bool)Prop(a, "Enabled")); var handRect = (float[])Prop(handConfirm, "Rect");
		await SaveFrame("control-hand-selection", 0);
		await Click(SpectatorPoint(new Vector2(handRect[0] + handRect[2] / 2, handRect[1] + handRect[3] / 2)));
		await Until(() => handSelection.IsCompleted, "spectator confirms native combat hand selection"); Check((await handSelection).Count() == 1, "native hand selector returns exactly one card"); await RefreshControl();
		var attack = ControlActions.First(a => (string)Prop(a, "Kind") == "play" && (bool)Prop(a, "RequiresTarget") && (bool)Prop(a, "Enabled"));
		var attackCard = SnapshotCard((string)Prop(attack, "CardId")); var start = SpectatorPoint(CardControlPoint(attackCard));
		fingerprint = Fingerprint(player);
		await Pointer(start); Game.GetViewport().PushInput(new InputEventMouseButton { Position = start, GlobalPosition = start, ButtonIndex = MouseButton.Right, Pressed = true }, true); await Frames(3);
		Game.GetViewport().PushInput(new InputEventMouseButton { Position = start, GlobalPosition = start, ButtonIndex = MouseButton.Right, Pressed = false }, true); await Frames(3);
		Check((int)ControlView.GetType().GetField("_inspectIndex", Instance)!.GetValue(ControlView)! >= 0 && fingerprint == Fingerprint(player), "control mode right-click inspects without playing");
		ControlView.GetType().GetMethod("CloseInspect", Instance)!.Invoke(ControlView, null);
		await Pointer(start); await Pointer(start, true); await Pointer(SpectatorPoint(new Vector2(20, 20))); await Pointer(SpectatorPoint(new Vector2(20, 20)), false);
		Check(fingerprint == Fingerprint(player), "invalid target release cancels without spending resources");
		await Pointer(start); await Pointer(start, true); await Key(Godot.Key.Escape); await Pointer(start, false);
		Check(fingerprint == Fingerprint(player) && ControlView.GetType().GetField("_dragCard", Instance)!.GetValue(ControlView) == null, "Escape cancels dragged card without playing");
		await ToggleControl(true); await RefreshControl();
		var source = Field("_source")!; var commandType = source.GetType().Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing.SpectatorCommand")!;
		object Request(string context, string target)
		{
			var request = Activator.CreateInstance(commandType)!; var view = ControlView; var current = view.GetType().GetField("_snapshot", Instance)!.GetValue(view)!;
			long number = (long)view.GetType().GetField("_requestCounter", Instance)!.GetValue(view)! + 1; view.GetType().GetField("_requestCounter", Instance)!.SetValue(view, number);
			foreach (var pair in new[] { ("Session", Prop(current, "Session")), ("SourceId", Prop(current, "SourceId")), ("Context", (object)context), ("Epoch", view.GetType().GetField("_controlEpoch", Instance)!.GetValue(view)!), ("RequestId", (object)number), ("ActionId", Prop(attack, "Id")), ("TargetId", (object)target) }) commandType.GetProperty(pair.Item1)!.SetValue(request, pair.Item2);
			return request;
		}
		var stale = source.GetType().GetMethod("ExecuteCommand", Instance)!.Invoke(source, new[] { Request("old-context", "missing") })!;
		Check(!(bool)Prop(stale, "Accepted") && fingerprint == Fingerprint(player), "stale page command is rejected without changing game");
		var targets = ((IEnumerable)Prop(ControlData, "Targets")).Cast<object>().ToArray(); var ids = ((IEnumerable)Prop(attack, "TargetIds")).Cast<string>().ToArray(); var targetData = targets.First(t => ids.Contains((string)Prop(t, "Id"))); var rect = (float[])Prop(targetData, "Rect");
		var enemy = NCombatRoom.Instance!.CreatureNodes.First(n => "creature:" + n.GetInstanceId() == (string)Prop(targetData, "Id")).Entity; int hp = enemy.CurrentHp, energy = player.PlayerCombatState!.Energy;
		await Pointer(start); await Pointer(start, true); await Pointer(SpectatorPoint(new Vector2(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2)));
		await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		((SubViewport)ControlView.GetType().GetField("_viewport", Instance)!.GetValue(ControlView)!).GetTexture().GetImage().SavePng(System.IO.Path.Combine(Output, "control-targeting.png"));
		await Pointer(SpectatorPoint(new Vector2(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2)), false);
		await Until(() => player.PlayerCombatState!.Energy < energy && enemy.CurrentHp < hp, "real spectator drag spends native energy and damages target");
		var command = ControlView.GetType().GetField("_lastControlCommand", Instance)!.GetValue(ControlView)!; fingerprint = Fingerprint(player);
		var replay = source.GetType().GetMethod("ExecuteCommand", Instance)!.Invoke(source, new[] { command })!;
		Check((bool)Prop(replay, "Accepted") && fingerprint == Fingerprint(player), "duplicate command returns cached result without second play");
		await RefreshControl();
		var defend = ControlActions.FirstOrDefault(a => (string)Prop(a, "Kind") == "play" && !(bool)Prop(a, "RequiresTarget") && (bool)Prop(a, "Enabled"));
		if (defend != null)
		{
			int block = player.Creature.Block; var point = SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(defend, "CardId")))); await Pointer(point); await Pointer(point, true); await Pointer(SpectatorPoint(new Vector2(900, 500))); await Pointer(SpectatorPoint(new Vector2(900, 500)), false);
			await Until(() => player.Creature.Block > block, "untargeted spectator drag plays native block card");
		}
		await RefreshControl(); int turn = player.PlayerCombatState!.TurnNumber; var end = ControlActions.First(a => (string)Prop(a, "Kind") == "endTurn" && (bool)Prop(a, "Enabled")); var endRect = (float[])Prop(end, "Rect");
		await Click(SpectatorPoint(new Vector2(endRect[0] + endRect[2] / 2, endRect[1] + endRect[3] / 2))); await Until(() => player.PlayerCombatState!.TurnNumber > turn, "spectator end-turn button executes native enemy turn and next draw", 20);
		await ToggleControl(false); var expired = source.GetType().GetMethod("ExecuteCommand", Instance)!.Invoke(source, new[] { command })!;
		Check(!(bool)Prop(expired, "Accepted"), "revoked control epoch rejects old command");
		await ToggleControl(true); await Key(Godot.Key.F8); await Key(Godot.Key.F8); await RefreshControl();
		Check(!(bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)!, "close reopen never restores control permission");
		await ToggleControl(true);
		var newSource = Field("_source")!;
		var oldPanelCommand = newSource.GetType().GetMethod("ExecuteCommand", Instance)!.Invoke(newSource, new[] { command })!;
		Check(!(bool)Prop(oldPanelCommand, "Accepted") && (long)Prop(command, "Epoch") != (long)ControlView.GetType().GetField("_controlEpoch", Instance)!.GetValue(ControlView)!, "reconstructed panel never reuses old control permission generation");
		await Click(new Vector2(20, 500));
		Check(!(bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)!, "local game pointer takes priority and exits spectator control");
		await ToggleControl(true); var panel = (Control)ControlView.GetType().GetField("_panel", Instance)!.GetValue(ControlView)!;
		await Click(panel.GetNode<Button>("SpectatorTitlebar/SpectatorSourceSelector/SpectatorSource1").GetGlobalRect().GetCenter());
		Check(!(bool)ControlView.GetType().GetField("_controlEnabled", Instance)!.GetValue(ControlView)!, "source request immediately revokes control before new snapshot"); await RefreshControl();
		await SaveFrame("control-completed");
	}
}
