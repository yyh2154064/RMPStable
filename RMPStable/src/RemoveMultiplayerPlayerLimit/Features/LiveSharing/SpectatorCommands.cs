using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Value-only requests. The receiver resolves opaque IDs; live models and nodes
// are never given to the spectator renderer or put in a request.
internal sealed class SpectatorCommand
{
	public string Session { get; set; } = "";
	public string SourceId { get; set; } = "";
	public string Context { get; set; } = "";
	public long Epoch { get; set; }
	public long RequestId { get; set; }
	public string ActionId { get; set; } = "";
	public string TargetId { get; set; } = "";
}

internal sealed class SpectatorCommandResult
{
	public long RequestId { get; set; }
	public bool Accepted { get; set; }
	public string Message { get; set; } = "";
}

internal sealed partial class LocalSpectatorSource
{
	private sealed record CommandBinding(Func<bool> Available, Func<string, bool> Execute);
	private readonly Dictionary<CardModel, string> _controlCardIds = new();
	private Dictionary<string, CommandBinding> _controlBindings = new();
	private readonly Dictionary<long, SpectatorCommandResult> _commandResults = new();
	private readonly Queue<long> _commandResultOrder = new();
	private long _controlCardCounter, _controlEpoch, _controlRevision, _lastCommandRequest;
	// Never reuse a permission generation when a panel/source is reconstructed
	// in the same run; delayed requests from the previous panel stay invalid.
	private static long _nextControlEpoch;
	private bool _controlEnabled;
	private string _publishedControlContext = "";
	private static readonly MethodInfo? CanPlayHand = typeof(NPlayerHand).GetMethod("CanPlayCards", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? CardClickable = typeof(NCardHolder).GetField("_isClickable", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? SelectionPreview = typeof(NDeckCardSelectScreen).GetField("_previewContainer", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? RewardCompletion = typeof(NCardRewardSelectionScreen).GetField("_completionSource", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? HandConfirm = typeof(NPlayerHand).GetField("_selectModeConfirmButton", BindingFlags.Instance | BindingFlags.NonPublic);
	private static bool ChooseNativeCard(Node selection, NCardHolder holder)
	{
		// Keep the native selector's connected handler, including deselecting
		// a selected hand holder and its original async completion flow.
		holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
		return true;
	}
	private static bool RewardAwaited(Node selection)
	{
		if (selection is not NCardRewardSelectionScreen) return true;
		var completion = RewardCompletion?.GetValue(selection);
		return completion?.GetType().GetProperty("Task")?.GetValue(completion) is Task task && !task.IsCompleted;
	}
	private static bool SelectorActive(Node selection, Node node)
	{
		if (!Ready(selection) || !Ready(node) || !RewardAwaited(selection)) return false;
		if (selection is NChooseACardSelectionScreen && !SelectionCooldownComplete(selection)) return false;
		var preview = ActiveSelectionPreview(selection);
		return preview?.IsVisibleInTree() != true || preview.IsAncestorOf(node);
	}
	private static readonly FieldInfo? ChooseOpenedTicks = typeof(NChooseACardSelectionScreen).GetField("_openedTicks", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly ulong ChooseCooldown = (ulong)(typeof(NChooseACardSelectionScreen).GetField("_noSelectionTimeMsec", BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue() ?? 350UL);
	private static bool SelectionCooldownComplete(Node selection) => ChooseOpenedTicks?.GetValue(selection) is ulong opened && Time.GetTicksMsec() - opened > ChooseCooldown;
	internal static bool NativeReplayReady(Node node)
	{
		if (!node.IsNodeReady() || node.IsQueuedForDeletion()) return false;
		if (NOverlayStack.Instance?.Peek() is Node overlay && (node == overlay || overlay.IsAncestorOf(node)) && !SelectorActive(overlay, node)) return false;
		if (node is NCardHolder holder) return holder.Hitbox.IsEnabled && CardClickable?.GetValue(holder) is true;
		return node is not NClickableControl button || button.IsEnabled;
	}
	private static Control? ActiveSelectionPreview(Node selection)
	{
		for (var type = selection.GetType(); type != null; type = type.BaseType)
			foreach (var name in new[] { "_previewContainer", "_upgradeSinglePreviewContainer", "_upgradeMultiPreviewContainer" })
				if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)?.GetValue(selection) is Control preview && Ready(preview)) return preview;
		return null;
	}

	private string CardControlId(CardModel card)
	{
		if (!_controlCardIds.TryGetValue(card, out var id)) _controlCardIds[card] = id = "card:" + ++_controlCardCounter;
		return id;
	}
	internal long SetControlMode(bool enabled)
	{
		if (!enabled) { StopRemoteMapStroke(); RestoreNativeInput(); }
		_controlEnabled = enabled; _controlEpoch = System.Threading.Interlocked.Increment(ref _nextControlEpoch);
		_commandResults.Clear(); _commandResultOrder.Clear();
		_lastCommandRequest = 0;
		return _controlEpoch;
	}
	private string CurrentControlContext()
	{
		var state = RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault()?.PlayerCombatState;
		return PageToken() + ":" + NativeRoom?.GetInstanceId() + ":" + ActiveNativeUi?.GetInstanceId() + ":" + NativePopup?.GetInstanceId() + ":" + NTargetManager.Instance?.IsInSelection + ":" + state?.TurnNumber + ":" + NPlayerHand.Instance?.CurrentMode + ":" + _controlRevision;
	}
	private static bool Ready(Node node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree() && node.IsNodeReady() && !node.IsQueuedForDeletion() && node is CanvasItem item && item.IsVisibleInTree();
	private static bool Unblocked() => NModalContainer.Instance?.OpenModal == null && NGame.Instance.InspectRelicScreen?.IsVisibleInTree() != true && NGame.Instance.InspectCardScreen?.IsVisibleInTree() != true;
	private bool HandAvailable(Player player) => Unblocked() && NMapScreen.Instance?.IsOpen != true && NOverlayStack.Instance?.Peek() == null && NPlayerHand.Instance is { } hand && Ready(hand) && hand.CurrentMode == NPlayerHand.Mode.Play && CanPlayHand?.Invoke(hand, null) is true && player.PlayerCombatState != null;

	private void CaptureControl(SpectatorSnapshot snapshot, Player player, Node? pageRoot)
	{
		var control = new ControlSnapshot { Context = CurrentControlContext() };
		var bindings = new Dictionary<string, CommandBinding>();
		void Add(ControlActionSnapshot action, Func<bool> available, Func<string, bool> execute)
		{
			if (action.Kind != "play" && action.NativePath.Length == 0 && ulong.TryParse(action.Id.Substring(action.Id.LastIndexOf(':') + 1), out var instance) &&
				GodotObject.InstanceFromId(instance) is Node node) action.NativePath = MirrorNativeUi.Key(node);
			if (action.MapAttached && NMapScreen.Instance?.GetNodeOrNull<Control>("TheMap") is { } map &&
				_capturedTransforms.TryGetValue(map.GetInstanceId().ToString(), out var capturedMap))
			{
				var r = action.Rect;
				action.Rect = Rect((capturedMap * map.GetGlobalTransform().AffineInverse()) * new Rect2(r[0], r[1], r[2], r[3]));
			}
			action.Enabled = available(); control.Actions.Add(action); bindings[action.Id] = new(available, execute);
		}
		if (snapshot.Page == "combat" && MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress && NCombatRoom.Instance is { } combat && Ready(combat) && combat.Ui?.Hand != null && player.PlayerCombatState is { } state)
		{
			var hand = combat.Ui.Hand;
			bool Selecting() => Unblocked() && Ready(hand) && NMapScreen.Instance?.IsOpen != true && NOverlayStack.Instance?.Peek() == null && hand.CurrentMode is NPlayerHand.Mode.SimpleSelect or NPlayerHand.Mode.UpgradeSelect;
			if (Selecting())
			{
				foreach (var holder in Descendants<NCardHolder>(hand).Where(h => Ready(h) && h.CardModel != null))
					Add(new() { Id = "hand-select:" + holder.GetInstanceId(), CardId = CardControlId(holder.CardModel!), Kind = "select" }, () => Selecting() && Ready(holder) && holder.Hitbox.IsEnabled && CardClickable?.GetValue(holder) is true,
						_ => ChooseNativeCard(hand, holder));
				if (HandConfirm?.GetValue(hand) is NButton confirm)
					Add(new() { Id = "hand-confirm:" + confirm.GetInstanceId(), Kind = "choice", Label = T("确认选牌", "Confirm selection"), Rect = GlobalRect(confirm) }, () => Selecting() && Ready(confirm) && confirm.IsEnabled,
						_ => { confirm.ForceClick(); return true; });
			}
			else
			{
			var creatures = combat.CreatureNodes.Where(n => Ready(n) && n.Entity.CombatState != null && n.Entity.IsAlive).ToArray();
			foreach (var node in creatures) control.Targets.Add(new() { NativeIndex = node.Entity.CombatState.Creatures.ToList().IndexOf(node.Entity), Id = "creature:" + node.GetInstanceId(), Enemy = node.Entity.IsEnemy, Rect = GlobalRect(node.Hitbox) });
			foreach (var card in state.Hand.Cards.ToArray())
			{
				bool targeted = card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly;
				var targets = creatures.Where(n => card.CanPlayTargeting(n.Entity)).Select(n => "creature:" + n.GetInstanceId()).ToList();
				var action = new ControlActionSnapshot { NativeIndex = state.Hand.Cards.ToList().IndexOf(card), Rect = GlobalRect(NPlayerHand.Instance?.GetCardHolder(card)?.Hitbox), Id = "play:" + CardControlId(card), CardId = CardControlId(card), Kind = "play", RequiresTarget = targeted, TargetIds = targets };
				Add(action, () => HandAvailable(player) && state.Hand.Cards.Contains(card) && (targeted ? creatures.Any(n => Ready(n) && n.Entity.IsAlive && card.CanPlayTargeting(n.Entity)) : card.CanPlayTargeting(null)), target =>
				{
					var node = targeted ? combat.CreatureNodes.FirstOrDefault(n => "creature:" + n.GetInstanceId() == target && Ready(n) && n.Entity.IsAlive && n.IsInteractable) : null;
					if (targeted && node == null) return false;
					return card.TryManualPlay(node?.Entity);
				});
			}
			if (combat.Ui.EndTurnButton is { } end)
				Add(new() { Id = "end:" + end.GetInstanceId(), Kind = "endTurn", Label = T("结束回合", "End turn"), Rect = GlobalRect(end) }, () => Unblocked() && Ready(end) && end.IsEnabled && HandAvailable(player), _ => { end.CallReleaseLogic(); return true; });
			}
		}
		else if (snapshot.Page is "reward" or "selection" && pageRoot != null)
		{
			// Only the supported native reward/deck selectors are exposed. Their
			// original handlers preserve min/max selections and async completion.
			Node? selection = snapshot.Page == "reward" ? NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen : pageRoot as NCardGridSelectionScreen;
			if (selection != null)
			{
				foreach (var holder in Descendants<NCardHolder>(selection).Where(h => Ready(h) && h.CardModel != null && !(SelectionPreview?.DeclaringType?.IsInstanceOfType(selection) == true && SelectionPreview.GetValue(selection) is Control preview && preview.IsVisibleInTree())))
				{
					var card = holder.CardModel!;
					Add(new() { Id = "select:" + holder.GetInstanceId(), CardId = CardControlId(card), Kind = "select" }, () => Unblocked() && SelectorActive(selection, holder) && holder.Hitbox.IsEnabled && CardClickable?.GetValue(holder) is true,
						_ => ChooseNativeCard(selection, holder));
				}
				// Card hitboxes also inherit NButton; exposing them as generic buttons
				// would cover the renderer's card interactions with a no-op release.
				foreach (var button in Descendants<NButton>(selection).Where(b => SelectorActive(selection, b) &&
					(selection is NCardRewardSelectionScreen ? b is NCardRewardAlternativeButton :
					b.Name.ToString() is "PreviewCancel" or "PreviewConfirm" or "Close" or "Confirm")))
				{
					Add(new() { Id = "button:" + button.GetInstanceId(), Kind = "choice", Label = T("选择此选项", "Choose this option"), Rect = GlobalRect(button) }, () => Unblocked() && SelectorActive(selection, button) && button.IsEnabled,
						_ => { button.ForceClick(); return true; });
				}
			}
		}
		CaptureNativeUi(control, Add);
		var publishedIds = new HashSet<string>(control.Actions.Select(a => a.Id));
		foreach (var id in bindings.Keys.Where(id => !publishedIds.Contains(id)).ToArray()) bindings.Remove(id);
		snapshot.Control = control;
		_controlBindings = bindings; _publishedControlContext = control.Context;
	}

	internal SpectatorCommandResult ExecuteCommand(SpectatorCommand command)
	{
		SpectatorCommandResult Reject(string message) => new() { RequestId = command.RequestId, Message = message };
		if (!_controlEnabled || command.Epoch != _controlEpoch) return Reject(T("控制已退出，请重新开启", "Control permission expired"));
		if (command.RequestId <= 0) return Reject(T("无效操作编号", "Invalid request ID"));
		if (_commandResults.TryGetValue(command.RequestId, out var previous)) return previous;
		if (command.RequestId <= _lastCommandRequest) return Reject(T("旧操作编号已失效", "Old request ID expired"));
		_lastCommandRequest = command.RequestId;
		var run = RunManager.Instance.DebugOnlyGetState();
		string source = _previewSourceId.Length > 0 ? _previewSourceId : _participant?.Id ?? "";
		if (run?.Players.Count != 1 || RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Client || NRun.Instance == null || command.Session != NRun.Instance.GetInstanceId().ToString() || command.SourceId != source) return Reject(T("对局或来源已改变", "Session or source changed"));
		if (command.Context != _publishedControlContext || command.Context != CurrentControlContext()) return Reject(T("页面已变化，请等待刷新", "Page changed; wait for refresh"));
		var result = Reject(T("当前无法执行此操作", "Action is unavailable"));
		try
		{
			if (_controlBindings.TryGetValue(command.ActionId, out var binding) && binding.Available() && binding.Execute(command.TargetId))
			{
				// Pointer streams and wheel ticks leave decision context intact, so
				// consecutive events are not discarded while a snapshot is in flight.
				if (!command.ActionId.StartsWith("scroll") && !command.ActionId.StartsWith("map-input:")) _controlRevision++;
				result.Accepted = true; result.Message = T("已提交，等待游戏更新", "Submitted; waiting for game update");
			}
		}
		catch (Exception ex) { result.Message = T("操作失败：", "Action failed: ") + ex.GetBaseException().Message; }
		_commandResults[command.RequestId] = result; _commandResultOrder.Enqueue(command.RequestId);
		while (_commandResultOrder.Count > 64) _commandResults.Remove(_commandResultOrder.Dequeue());
		return result;
	}
}
