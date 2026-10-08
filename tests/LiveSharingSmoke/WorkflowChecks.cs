using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

public static partial class Smoke
{
	private static object CurrentSnapshot => ControlView.GetType().GetField("_snapshot", Instance)!.GetValue(ControlView)!;
	private static async Task ClickAction(object action)
	{
		string id = (string)Prop(action, "CardId");
		var rect = (float[])Prop(action, "Rect");
		Vector2 point = id.Length > 0 ? CardControlPoint(SnapshotCard(id)) : new Vector2(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2);
		if (DisplayServer.GetName() == "headless")
			ControlView.GetType().GetMethod("SubmitControl", Instance)!.Invoke(ControlView, new object?[] { Prop(action, "Id"), "", null });
		else await Click(SpectatorPoint(point));
		await RefreshControl();
		var command = ControlView.GetType().GetField("_lastControlCommand", Instance)!.GetValue(ControlView)!;
		if (command == null || (string)Prop(command, "ActionId") != (string)Prop(action, "Id"))
		{ GD.Print("[LiveSharingSmoke] MISSED expected=" + Prop(action, "Id") + " actual=" + (command == null ? "none" : Prop(command, "ActionId")) + " pending=" + ControlView.GetType().GetField("_pendingControlContext", Instance)!.GetValue(ControlView)); await SaveFrame("action-missed", 0); }
		Check(command != null && (string)Prop(command, "ActionId") == (string)Prop(action, "Id"), (DisplayServer.GetName() == "headless" ? "window command reaches " : "pointer reaches intended ") + Prop(action, "Id"));
		var result = ControlView.GetType().GetField("_lastControlResult", Instance)!.GetValue(ControlView)!;
		Check(result != null && (bool)Prop(result, "Accepted"), "spectator submits " + Prop(action, "Id"));
	}
	private static async Task ClickNative(Control node)
	{
		await RefreshControl();
		var action = ControlActions.FirstOrDefault(a => ((string)Prop(a, "Id")).EndsWith(":" + node.GetInstanceId()) && (bool)Prop(a, "Enabled"));
		if (action == null) throw new Exception("Missing enabled native action for " + node.GetPath() + " / " + string.Join(", ", ControlActions.Select(a => Prop(a, "Id") + "=" + Prop(a, "Enabled"))));
		await ClickAction(action);
	}
	private static async Task CheckShopControls(RunState state)
	{
		await ToggleControl(true);
		var status = (Label)ControlView.GetType().GetField("_status", Instance)!.GetValue(ControlView)!;
		string statusText = status.Text;
		for (int i = 0; i < 30; i++)
		{ await Frames(1); if (status.Text != statusText) throw new Exception("Control status flickered across capture/process callbacks"); }
		Check(statusText.Contains("本机控制") || statusText.Contains("Local control"), "control status stays stable through repeated snapshot and frame updates");
		var room = NMerchantRoom.Instance!; var player = state.Players[0];
		var slot = Descendants(room.Inventory).OfType<NMerchantCard>().First(s => s.Entry.IsStocked && s.Entry.EnoughGold);
		int gold = player.Gold, deck = player.Deck.Cards.Count, cost = slot.Entry.Cost;
		await SaveFrame("workflow-shop-open", 0);
		await ClickNative(slot);
		await Until(() => player.Gold == gold - cost && player.Deck.Cards.Count == deck + 1, "shop left click purchases real card at native price");
		Check((int)ControlView.GetType().GetField("_inspectIndex", Instance)!.GetValue(ControlView)! < 0, "shop purchase never opens local card inspection");
		// Replay the original purchase dialogue long enough for a screenshot;
		// this changes only the disposable fixture's dialogue animation.
		var dialogue = Descendants(room).OfType<NMerchantDialogue>().First();
		dialogue.ShowOnInventoryOpen(); await Seconds(0.5);
		dialogue.Get("_tween").As<Tween>().Kill(); dialogue.Modulate = Colors.White;
		await RefreshControl();
		Check(((IEnumerable)Prop(CurrentSnapshot, "ForegroundArt")).Cast<object>().Any() && ((IEnumerable)Prop(CurrentSnapshot, "ForegroundLabels")).Cast<object>().Any(), "merchant speech bubble and text captured above stock");
		await SaveFrame("workflow-shop-dialogue", 0); dialogue.Modulate = Colors.Transparent;
		await CheckShopInventoryVariants(state, room);
		await ClickNative(Descendants(room.Inventory).OfType<NBackButton>().First());
		await Until(() => !room.Inventory.IsOpen, "shop back button closes native inventory");
		await Seconds(0.8); await RefreshControl();
		Check(((IEnumerable)Prop(CurrentSnapshot, "Cards")).Cast<object>().Count() == 0 && ((IEnumerable)Prop(CurrentSnapshot, "Items")).Cast<object>().Count() == 0, "closed shop has no stale stock or sold-out cards");
		await SaveFrame("workflow-shop-closed", 0);
		await ClickNative(Descendants(room).OfType<NMerchantButton>().First());
		await Until(() => room.Inventory.IsOpen, "spectator reopens native merchant rug"); await Seconds(0.8); await RefreshControl();
		Check(((IEnumerable)Prop(CurrentSnapshot, "Background")).Cast<object>().Any(a => ((string)Prop(a, "Texture")).Contains("rug")), "open shop captures native rug after its animation");
		await ClickNative(Descendants(room.Inventory).OfType<NBackButton>().First()); await Seconds(0.8);
		await ClickNative(room.ProceedButton); await Until(() => NMapScreen.Instance!.IsOpen, "shop proceed opens actual map"); await Seconds(1);
		await Until(() => Descendants(NMapScreen.Instance!).OfType<NMapPoint>().Any(p => p.IsEnabled && p.IsVisibleInTree() && p.GetGlobalRect().Intersects(p.GetViewportRect())), "native map opening animation reveals a legal point", 20);
		var point = Descendants(NMapScreen.Instance!).OfType<NMapPoint>().First(p => p.IsEnabled && p.IsVisibleInTree() && p.GetGlobalRect().Intersects(p.GetViewportRect()));
		var previous = state.MapLocation; await ClickNative(point);
		GD.Print("[LiveSharingSmoke] MAP " + point.GetType().Name + " coord=" + point.Point.coord + " state=" + point.State + " travel=" + NMapScreen.Instance!.IsTravelEnabled + " ftue=" + MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SeenFtue("map_select_ftue") + " modal=" + NModalContainer.Instance?.OpenModal);
		await Until(() => !NMapScreen.Instance!.IsOpen && state.MapLocation != previous, "map click travels through native run manager to next floor", 20);
		NMapScreen.Instance!.Close(false); await Seconds(2); await RefreshControl(); await ToggleControl(false);
	}
	private static async Task CheckShopInventoryVariants(RunState state, NMerchantRoom room)
	{
		var player = state.Players[0];
		var expensive = Descendants(room.Inventory).OfType<NMerchantRelic>().First(s => s.Entry.IsStocked && !s.Entry.EnoughGold);
		await RefreshControl(); string expensiveId = "buy:" + expensive.GetInstanceId();
		Check(ControlActions.Any(a => (string)Prop(a, "Id") == expensiveId && !(bool)Prop(a, "Enabled")), "insufficient gold disables merchant purchase action");
		int gold = player.Gold, relics = player.Relics.Count;
		ControlView.GetType().GetMethod("SubmitControl", Instance)!.Invoke(ControlView, new object?[] { expensiveId, "", null });
		var denied = ControlView.GetType().GetField("_lastControlResult", Instance)!.GetValue(ControlView)!;
		Check(!(bool)Prop(denied, "Accepted") && player.Gold == gold && player.Relics.Count == relics, "receiver rejects unaffordable purchase without changing gold or relics");
		await PlayerCmd.GainGold(1000, player); await RefreshControl();
		gold = player.Gold; int cost = expensive.Entry.Cost; await ClickNative(expensive);
		await Until(() => player.Gold == gold - cost && player.Relics.Count == relics + 1, "merchant relic purchase uses native price and obtains real relic");
		var potion = Descendants(room.Inventory).OfType<NMerchantPotion>().First(s => s.Entry.IsStocked && s.Entry.EnoughGold);
		gold = player.Gold; cost = potion.Entry.Cost; int potions = player.PotionSlots.Count(p => p != null);
		await ClickNative(potion); await Until(() => player.Gold == gold - cost && player.PotionSlots.Count(p => p != null) == potions + 1, "merchant potion purchase obtains actual belt item");
		var removal = Descendants(room.Inventory).OfType<NMerchantCardRemoval>().First();
		gold = player.Gold; cost = removal.Entry.Cost; int deck = player.Deck.Cards.Count;
		await ClickNative(removal); await Until(() => NOverlayStack.Instance!.Peek() is NCardGridSelectionScreen, "merchant removal opens original selector"); await RefreshControl();
		await ClickAction(ControlActions.First(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled")));
		if (NOverlayStack.Instance!.Peek() is NDeckCardSelectScreen select && select.GetNode<Control>("%PreviewContainer").Visible)
			await ClickNative(select.GetNode<Control>("%PreviewConfirm"));
		await Until(() => player.Deck.Cards.Count == deck - 1 && player.Gold == gold - cost && NOverlayStack.Instance.Peek() == null, "merchant removal returns to inventory with one real card removed");
	}
	private static async Task CheckMerchantPortraits()
	{
		var room = NMerchantRoom.Instance!;
		var original = Descendants(room).OfType<NMerchantCharacter>().First(); var parent = original.GetParent(); original.Hide();
		foreach (var character in new CharacterModel[] { ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(), ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>() })
		{
			await PreloadManager.LoadRunAssets(new[] { character });
			var visual = PreloadManager.Cache.GetScene(character.MerchantAnimPath).Instantiate<NMerchantCharacter>(); parent.AddChild(visual); visual.Position = original.Position;
			await Seconds(0.6); await RefreshControl();
			string name = character.Id.Entry.ToLowerInvariant();
			foreach (var slot in Descendants(visual).Where(n => n.GetClass() is "SpineSlotNode" or "SpineBoneNode"))
				System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "merchant-" + name + "-attachment-properties.txt"), slot.GetPropertyList().Select(p => p["name"].AsString() + "=" + slot.Get(p["name"].AsString()).ToString()));
			System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "merchant-" + name + "-tree.txt"), Descendants(visual).Where(n => n.GetClass() != "SpineMesh2D").Select(n => n.GetPath() + " " + n.GetClass() + " " + (n is CanvasItem c ? "visible=" + c.IsVisibleInTree() + " tint=" + c.Modulate : "") + " " + (n is Sprite2D s ? s.Texture?.ResourcePath : "")));
			System.IO.File.WriteAllText(System.IO.Path.Combine(Output, "merchant-" + name + ".json"), System.Text.Json.JsonSerializer.Serialize(CurrentSnapshot));
			foreach (var spine in Descendants(visual).Where(n => n.GetClass() == "SpineSprite"))
			{
				var native = new MegaSprite(spine).GetSkeleton()!.BoundObject;
				System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "merchant-" + name + "-spine-methods.txt"), native.GetMethodList().Select(m => m["name"].AsString()));
			}
			Check(((IEnumerable)Prop(CurrentSnapshot, "BackgroundLabels")).Cast<object>().Any(t => ((string)Prop(t, "Text")).Contains("前进") || ((string)Prop(t, "Text")).Contains("Proceed")), name + " closed merchant includes native proceed text");
			await SaveFrame("merchant-" + name, 0);
			parent.RemoveChild(visual); visual.QueueFree(); await Frames(2);
		}
		original.Show(); await RefreshControl();
	}
	private static async Task CheckWorkflowControls(RunState state)
	{
		var player = state.Players[0]; await ToggleControl(true);
		await PotionCmd.TryToProcure<MegaCrit.Sts2.Core.Models.Potions.FirePotion>(player); await Seconds(0.6); await RefreshControl();
		var potionHolder = Descendants(NRun.Instance!.GlobalUi.TopBar.PotionContainer).OfType<NPotionHolder>().First(h => h.HasPotion && h.Potion!.Model is MegaCrit.Sts2.Core.Models.Potions.FirePotion);
		await ClickNative(potionHolder); await RefreshControl();
		var popup = Descendants(NRun.Instance.GlobalUi.TopBar).OfType<NPotionPopup>().First(p => !p.IsMarkedForRemoval);
		_controller.GetMethod("RouteMapInput", Static)!.Invoke(null, new object[] { true });
		Check(!popup.IsProcessingInput(), "native potion popup cannot consume viewer coordinates inside panel");
		_controller.GetMethod("RouteMapInput", Static)!.Invoke(null, new object[] { false });
		Check(popup.IsProcessingInput(), "original popup input is restored outside panel without changing selected mode");
		if (DisplayServer.GetName() == "headless") await ClickAction(ControlActions.First(a => ((string)Prop(a, "Id")).StartsWith("cancel-popup:")));
		else { await Key(Godot.Key.Escape); await RefreshControl(); }
		Check(!Descendants(NRun.Instance.GlobalUi.TopBar).OfType<NPotionPopup>().Any(p => !p.IsMarkedForRemoval) && potionHolder.HasPotion, "spectator Escape cancels popup without consuming potion");
		await ClickNative(potionHolder); await RefreshControl();
		popup = Descendants(NRun.Instance.GlobalUi.TopBar).OfType<NPotionPopup>().First(p => !p.IsMarkedForRemoval);
		await ClickNative(Descendants(popup).OfType<NButton>().First(b => b.Name.ToString().Contains("Use")));
		await Until(() => MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager.Instance!.IsInSelection, "native potion use opens target selection"); await RefreshControl();
		var targetAction = ControlActions.First(a => (string)Prop(a, "Kind") == "target" && (bool)Prop(a, "Enabled"));
		var potionTarget = NCombatRoom.Instance!.CreatureNodes.First(n => "target:" + n.GetInstanceId() == (string)Prop(targetAction, "Id")); int potionHp = potionTarget.Entity.CurrentHp;
		await ClickAction(targetAction);
		await Until(() => !potionHolder.HasPotion && potionTarget.Entity.CurrentHp < potionHp, "spectator target uses real fire potion on selected enemy");
		await CheckCombatAnimations();
		await CheckEffects();
		// Disposable fixtures reduce combat duration; every actual play/reward
		// decision still goes through a real mouse click in the spectator panel.
		foreach (var creature in NCombatRoom.Instance!.CreatureNodes.Where(n => n.Entity.IsEnemy && n.Entity.IsAlive)) creature.Entity.SetCurrentHpInternal(1);
		await PlayerCmd.SetEnergy(20, player); await RefreshControl();
		for (int count = 0; count < 12 && NOverlayStack.Instance?.Peek() is not NRewardsScreen; count++)
		{
			var attack = ControlActions.FirstOrDefault(a => (string)Prop(a, "Kind") == "play" && (bool)Prop(a, "RequiresTarget") && (bool)Prop(a, "Enabled"));
			if (attack == null) { await Seconds(0.5); await RefreshControl(); continue; }
			var ids = ((IEnumerable)Prop(attack, "TargetIds")).Cast<string>().ToArray();
			var target = ((IEnumerable)Prop(ControlData, "Targets")).Cast<object>().First(t => ids.Contains((string)Prop(t, "Id"))); var rect = (float[])Prop(target, "Rect");
			var start = SpectatorPoint(CardControlPoint(SnapshotCard((string)Prop(attack, "CardId")))); var end = SpectatorPoint(new Vector2(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2));
			if (DisplayServer.GetName() == "headless")
				ControlView.GetType().GetMethod("SubmitControl", Instance)!.Invoke(ControlView, new object?[] { Prop(attack, "Id"), Prop(target, "Id"), null });
			else { await Pointer(start); await Pointer(start, true); await Pointer(end); await Pointer(end, false); }
			await Seconds(1); await RefreshControl();
		}
		await Until(() => NOverlayStack.Instance?.Peek() is NRewardsScreen, "spectator combat victory opens original rewards screen", 20);
		var loot = (NRewardsScreen)NOverlayStack.Instance!.Peek()!; int beforeGold = player.Gold;
		await ClickNative(Descendants(loot).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>().First(b => b.IsEnabled));
		Check(player.Gold > beforeGold, "reward button collects actual battle gold");
		var rewardButton = Descendants(loot).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>().FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree());
		if (rewardButton != null)
		{
			await ClickNative(rewardButton);
			if (NOverlayStack.Instance.Peek() is NCardRewardSelectionScreen)
			{
				await CheckRewardRetention((NCardRewardSelectionScreen)NOverlayStack.Instance.Peek()!);
				int beforeDeck = player.Deck.Cards.Count;
				await ClickAction(ControlActions.First(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled")));
				await Until(() => player.Deck.Cards.Count == beforeDeck + 1 && NOverlayStack.Instance.Peek() == loot, "battle reward card returns to original reward list");
			}
		}
		await ClickNative(Descendants(loot).OfType<NProceedButton>().First()); await Until(() => NMapScreen.Instance!.IsOpen, "battle reward proceed reaches native map");
		NMapScreen.Instance!.Close(false); NOverlayStack.Instance.Remove(loot);
		await RunManager.Instance.EnterRoomDebug(RoomType.RestSite); await Seconds(1); await RefreshControl();
		Check((string)Prop(CurrentSnapshot, "Page") == "rest" && ((IEnumerable)Prop(CurrentSnapshot, "PageArt")).Cast<object>().Any(), "rest room renders actual scene rather than fake deck browser");
		var smith = Descendants(NRestSiteRoom.Instance!).OfType<NRestSiteButton>().First(b => b.Option.GetType().Name.Contains("Smith"));
		await ClickNative(smith); await Until(() => NOverlayStack.Instance.Peek() is NCardGridSelectionScreen, "rest upgrade opens original deck selector"); await RefreshControl();
		await ClickAction(ControlActions.First(a => (string)Prop(a, "Kind") == "select" && (bool)Prop(a, "Enabled")));
		for (int i = 0; i < 3 && NOverlayStack.Instance.Peek() is NCardGridSelectionScreen selector; i++)
		{
			var confirm = Descendants(selector).OfType<NButton>().FirstOrDefault(b => b.IsEnabled && b.IsVisibleInTree() && b.Name.ToString().Contains("Confirm"));
			if (confirm != null) await ClickNative(confirm); else await Seconds(0.8);
		}
		await Until(() => player.Deck.Cards.Any(c => c.IsUpgraded) && NOverlayStack.Instance.Peek() == null, "rest upgrade modifies real deck and returns to campfire");
		await ClickNative(NRestSiteRoom.Instance!.ProceedButton); await Until(() => NMapScreen.Instance!.IsOpen, "rest proceed reaches native map"); NMapScreen.Instance!.Close(false);
		await RunManager.Instance.EnterRoomDebug(RoomType.Treasure); await Seconds(1); await RefreshControl();
		Check((string)Prop(CurrentSnapshot, "Page") == "treasure", "treasure room is tracked");
		var treasure = NRun.Instance!.TreasureRoom!; await ClickNative(treasure.GetNode<Control>("Chest")); await Seconds(1.5); await RefreshControl();
		int relics = player.Relics.Count;
		await ClickNative(Descendants(treasure).OfType<NTreasureRoomRelicHolder>().First(h => h.IsEnabled && h.IsVisibleInTree()));
		await Until(() => player.Relics.Count > relics && treasure.ProceedButton.IsEnabled, "treasure selection obtains native relic");
		await ClickNative(treasure.ProceedButton); await Until(() => NMapScreen.Instance!.IsOpen, "treasure proceed reaches native map"); NMapScreen.Instance!.Close(false);
		var canonical = ModelDb.AncientEvent<Neow>(); state.AppendToMapPointHistory(MapPointType.Ancient, RoomType.Event, canonical.Id);
		await RunManager.Instance.EnterRoom(new EventRoom(canonical) { OnStart = e => ((AncientEventModel)e).DebugOption = "LEAFY_POULTICE" }); await Seconds(1.5); await RefreshControl();
		relics = player.Relics.Count; await ClickNative(Descendants(NEventRoom.Instance!).OfType<NEventOptionButton>().First(b => b.IsEnabled && !b.Option.IsProceed));
		await Until(() => player.Relics.Count > relics, "event option executes original relic decision"); await RefreshControl();
		await ClickNative(Descendants(NEventRoom.Instance!).OfType<NEventOptionButton>().First(b => b.IsEnabled && b.Option.IsProceed));
		await Until(() => NMapScreen.Instance!.IsOpen, "event proceed reaches native map");
		await SaveFrame("workflow-event-map", 0); await CheckFeedbackMapAndEvents(state); await ToggleControl(false);
	}
}
