using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class LocalSpectatorSource
{
	private static readonly MethodInfo? MerchantSelect = typeof(NMerchantSlot).GetMethod("OnSelected", BindingFlags.NonPublic | BindingFlags.Instance);
	private static readonly PropertyInfo? MapTravelable = typeof(NMapPoint).GetProperty("IsTravelable", BindingFlags.NonPublic | BindingFlags.Instance);
	private static readonly FieldInfo? NativeTargetType = typeof(NTargetManager).GetField("_validTargetsType", BindingFlags.NonPublic | BindingFlags.Instance);
	private readonly HashSet<Node> _pausedNativeInput = new();
	private NPotionPopup? NativePopup => _top == null ? null : Descendants<NPotionPopup>(_top).FirstOrDefault(p => Ready(p) && !p.IsMarkedForRemoval);
	private void PauseNativeInput(Node? node)
	{
		// These native _Input callbacks otherwise consume the viewer's window
		// coordinates as source clicks before the panel can submit a command.
		if (!_controlEnabled || !_mapInputInPanel || node == null || !node.IsProcessingInput()) return;
		_pausedNativeInput.Add(node); node.SetProcessInput(false);
	}
	private void RestoreNativeInput()
	{
		foreach (var node in _pausedNativeInput)
			if (GodotObject.IsInstanceValid(node) && node.IsInsideTree() &&
				(node is NMapDrawingInput || node is NTargetManager { IsInSelection: true } || node is NPotionPopup { IsMarkedForRemoval: false })) node.SetProcessInput(true);
		_pausedNativeInput.Clear();
	}
	private static Control? NativeRoom => new Control?[] { NRun.Instance?.EventRoom, NRun.Instance?.MerchantRoom, NRun.Instance?.RestSiteRoom, NRun.Instance?.TreasureRoom, NRun.Instance?.MapRoom, NRun.Instance?.CombatRoom }.FirstOrDefault(n => n != null && Ready(n));
	internal static bool PendingNativeChoice => NTargetManager.Instance?.IsInSelection == true || NOverlayStack.Instance?.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardGridSelectionScreen ||
		NModalContainer.Instance?.OpenModal != null || NPlayerHand.Instance?.CurrentMode is NPlayerHand.Mode.SimpleSelect or NPlayerHand.Mode.UpgradeSelect;
	internal static Control? ActiveNativeUi => NModalContainer.Instance?.OpenModal as Control
		?? (NGame.Instance?.InspectCardScreen?.IsVisibleInTree() == true ? NGame.Instance.InspectCardScreen : null)
		?? (NGame.Instance?.InspectRelicScreen?.IsVisibleInTree() == true ? NGame.Instance.InspectRelicScreen : null)
		?? NCapstoneContainer.Instance?.CurrentCapstoneScreen as Control
		?? (NMapScreen.Instance?.IsOpen == true ? NMapScreen.Instance : null)
		?? NOverlayStack.Instance?.Peek() as Control ?? NativeRoom;
	private static T? ParentOf<T>(Node node) where T : Node
	{
		for (var parent = node.GetParent(); parent != null; parent = parent.GetParent()) if (parent is T found) return found;
		return null;
	}
	private static Rect2 ClickRect(Control node)
	{
		var rect = node.GetGlobalRect().Intersection(node.GetViewportRect());
		for (var parent = node.GetParent(); parent != null; parent = parent.GetParent())
			if (parent is Control { ClipContents: true } clip) rect = rect.Intersection(clip.GetGlobalRect());
		return rect;
	}
	private static bool Clickable(Control node) => Ready(node) && ClickRect(node).HasArea() && node.MouseFilter != Control.MouseFilterEnum.Ignore;

	private void CaptureNativeUi(ControlSnapshot control, Action<ControlActionSnapshot, Func<bool>, Func<string, bool>> add)
	{
		_pausedNativeInput.RemoveWhere(n => !GodotObject.IsInstanceValid(n) || !n.IsInsideTree());
		RouteNativeMapInput(_mapInputInPanel);
		var root = ActiveNativeUi;
		if (root == null) return;
		bool Active(Node node) => ActiveNativeUi == root && Ready(root) && Ready(node);
		var targeting = NTargetManager.Instance;
		if (targeting?.IsInSelection == true)
		{
			PauseNativeInput(targeting);
			// Complete the game's pending selection task, including targeted potions.
			control.Actions.Clear();
			foreach (var node in Descendants<Control>(NRun.Instance).Where(n => n is NCreature || NativeTargetType?.GetValue(targeting) is TargetType.TargetedNoCreature && n.GetType().Name is "NMerchantButton" or "NRestSiteCharacter"))
			{
				var hitbox = node is NCreature creature ? creature.Hitbox : node;
				if (!Clickable(hitbox) || !targeting.AllowedToTargetNode(node)) continue;
				add(new() { Id = "target:" + node.GetInstanceId(), Kind = "target", Rect = Rect(ClickRect(hitbox)), Label = T("选择目标", "Choose target") },
					() => Active(node) && targeting.IsInSelection && targeting.AllowedToTargetNode(node), _ =>
					{ targeting.OnNodeHovered(node); targeting._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }); return !targeting.IsInSelection; });
			}
            add(new() { Id = "cancel-target", Kind = "cancel", NativePath = MirrorNativeUi.Key(targeting), Label = T("取消目标", "Cancel targeting") }, () => targeting.IsInSelection,
				_ => { targeting.CancelTargeting(); return true; });
			return;
		}
		var roots = new List<Node> { root };
		bool globalUi = root == NativeRoom || root == NMapScreen.Instance || root == NOverlayStack.Instance?.Peek();
		if (globalUi && _top != null) roots.Add(_top);
		if (globalUi) roots.Add(NRun.Instance.GlobalUi.RelicInventory);
		var popup = NativePopup;
		if (popup != null && globalUi)
		{
			PauseNativeInput(popup);
			roots = new() { popup }; control.Actions.Clear();
			add(new() { Id = "cancel-popup:" + popup.GetInstanceId(), Kind = "cancel", NativePath = MirrorNativeUi.Key(popup) }, () => Ready(popup), _ => { popup.Remove(); return true; });
		}
		foreach (var ui in roots)
		{
			foreach (var button in Descendants<NClickableControl>(ui).Where(Clickable))
			{
				if (ParentOf<NCardHolder>(button) != null || ParentOf<NMerchantSlot>(button) != null || ParentOf<NCreature>(button) != null) continue;
				if (control.Actions.Any(a => a.Id.EndsWith(":" + button.GetInstanceId()))) continue;
				bool Enabled() => Active(button) && SelectorActive(ui, button) && button.IsEnabled && Clickable(button) && (popup == null || Ready(popup)) &&
					(button is not MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton option || !option.Option.IsLocked) &&
					(button is not NMapPoint point || MapTravelable?.GetValue(point) is true && NMapScreen.Instance.Drawings.GetLocalDrawingMode() == DrawingMode.None && !NMapScreen.Instance.Drawings.IsLocalDrawing());
				add(new() { Id = "native:" + button.GetInstanceId(), Kind = "native", Label = T("执行游戏内操作", "Activate game control"), MapAttached = button is NMapPoint, Rect = Rect(ClickRect(button)) }, Enabled,
					_ => { button.ForceClick(); RouteNativeMapInput(_mapInputInPanel); return true; });
			}
			// Different scenes connect different handlers to holder.Pressed. Keep
			// those original handlers (deck inspection, event/upgrade selection).
			if (ui != NCombatRoom.Instance && !(ui is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NDeckCardSelectScreen deckSelector && SelectionPreview?.GetValue(deckSelector) is Control { Visible: true }))
			foreach (var holder in Descendants<NCardHolder>(ui).Where(h => Ready(h) && h.CardModel != null))
			{
				string id = CardControlId(holder.CardModel!);
				if (control.Actions.Any(a => a.CardId == id) || !SelectorActive(ui, holder)) continue;
				add(new() { Id = "holder:" + holder.GetInstanceId(), Kind = "select", CardId = id },
					() => Active(holder) && SelectorActive(ui, holder) && Clickable(holder.Hitbox) && holder.Hitbox.IsEnabled && CardClickable?.GetValue(holder) is true,
					_ => ChooseNativeCard(ui, holder));
			}
			foreach (var scroll in Descendants<NScrollableContainer>(ui).Where(Ready))
				add(new() { Id = "scroll:" + scroll.GetInstanceId(), Kind = "scroll", Rect = GlobalRect(scroll) }, () => Active(scroll), direction => ScrollNative(scroll, direction));
		}
		if (root is NMapScreen map)
		{
			add(new() { Id = "scroll-map:" + map.GetInstanceId(), Kind = "scroll", Rect = GlobalRect(map) }, () => Active(map), direction => ScrollNative(map, direction));
			add(new() { Id = "map-input:" + map.GetInstanceId(), Kind = "mapInput", Rect = GlobalRect(map) }, () => Active(map), payload => ExecuteMapInput(map, payload));
		}
		if (popup == null)
		foreach (var inventory in Descendants<NMerchantInventory>(root).Where(i => Ready(i) && i.IsOpen))
			foreach (var slot in Descendants<NMerchantSlot>(inventory).Where(s => Clickable(s.Hitbox)))
			{
				string cardId = slot.Entry is MerchantCardEntry entry && entry.CreationResult?.Card is { } card ? CardControlId(card) : "";
				add(new() { Id = "buy:" + slot.GetInstanceId(), Kind = "buy", CardId = cardId, Rect = Rect(ClickRect(slot.Hitbox)), Label = T("购买", "Purchase") },
					() => Active(slot) && inventory.IsOpen && slot.Hitbox.IsEnabled && slot.Entry.IsStocked && slot.Entry.EnoughGold && MerchantSelect != null,
					_ => { if (MerchantSelect!.Invoke(slot, null) is not Task task) return false; TaskHelper.RunSafely(task); return true; });
			}
	}
	internal static bool ScrollNative(Control node, string direction)
	{
		var parts = direction.Split(':');
		if (parts[0] is not "up" and not "down" || parts.Length > 2) return false;
		float steps = 1;
		if (parts.Length == 2 && (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out steps) || !float.IsFinite(steps) || steps is <= 0 or > 64)) return false;
		// Native wheel helper ignores Factor. Its pan path preserves fractional
		// ticks while producing exactly the original 40 source pixels per tick.
		using var input = new InputEventPanGesture { Delta = new Vector2(0, (parts[0] == "up" ? -1 : 1) * steps * 0.8f) };
		node.Call("ProcessScrollEvent", input);
		return true;
	}
}
