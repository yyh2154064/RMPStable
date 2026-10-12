using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// A separate, source-owned journal fills the gaps in CombatReplay. It records
// original decisions, not input coordinates, and never edits the game's writer.
internal static partial class MultiplayerNoncombat
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static CombatReplay? Checkpoint;
    internal static byte[] Room = Array.Empty<byte>();
    internal static RunState? State;
    internal static AbstractRoom? SourceRoom;
    internal static readonly List<MirrorOperation> Operations = new();
    private static int _sharedDepth;
    private static bool _installed;
    internal static bool Replica => MirrorRenderer.Active?.Multiplayer == true;
    private static bool Source => !MirrorRenderer.IsRenderer && RunManager.Instance.NetService?.Type is NetGameType.Host or NetGameType.Client;
    internal static bool IsCurrent(RunState state) => Checkpoint != null && ReferenceEquals(State, state) &&
        (ReferenceEquals(SourceRoom, state.CurrentRoom) || SourceRoom is EventRoom parent && state.CurrentRoom is CombatRoom combat && Equals(combat.ParentEventId, parent.ModelId));
    private static bool Recording => Source && State == RunManager.Instance.DebugOnlyGetState() && State != null && IsCurrent(State) &&
        (!CombatManager.Instance.IsInProgress || SourceRoom is EventRoom);
    internal static object? Read(object o, string field) => MultiplayerPersonalUi.Read(o, field);
    internal static object? Invoke(object o, string name, params object?[] args)
    {
        for (Type? type = o.GetType(); type != null; type = type.BaseType)
            if (type.GetMethods(Any | BindingFlags.DeclaredOnly).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length) is { } method) return method.Invoke(o, args);
        throw new MissingMethodException(o.GetType().Name, name);
    }
    private static void Add(string kind, ulong player = 0, string value = "", byte[]? payload = null)
    { if (Recording) Operations.Add(new() { Kind = kind, NodeKey = player.ToString(), Value = value, Payload = payload ?? Array.Empty<byte>() }); }
    internal static void Initialize()
    {
        if (_installed) return; _installed = true;
        var h = new Harmony("RMPStable.MultiplayerNoncombat");
        void Patch(Type type, string method, string before, string? after = null, Func<MethodInfo, bool>? filter = null)
        {
            foreach (var m in type.GetMethods(Any).Where(m => m.Name == method && (filter?.Invoke(m) ?? true)))
                h.Patch(m, new HarmonyMethod(typeof(MultiplayerNoncombat).GetMethod(before, Any)), after == null ? null : new HarmonyMethod(typeof(MultiplayerNoncombat).GetMethod(after, Any)));
        }
        Patch(typeof(RunManager), "EnterRoomInternal", nameof(RoomEntering));
        Patch(typeof(CombatRoom), "OfferRoomEndRewards", nameof(RewardsEntering));
        Patch(typeof(CombatReplayWriter), "RecordInitialState", nameof(ReplayCheckpoint));
        Patch(typeof(ActionQueueSet), "EnqueueWithoutSynchronizing", nameof(Action));
        Patch(typeof(ActionQueueSet), "ResumeActionWithoutSynchronizing", nameof(Resume));
        Patch(typeof(PlayerChoiceSynchronizer), "SyncLocalChoice", nameof(LocalChoice));
        Patch(typeof(PlayerChoiceSynchronizer), "OnReceivePlayerChoice", nameof(ReceivedChoice));
        Patch(typeof(EventSynchronizer), "ChooseOptionForEvent", nameof(EventOption));
        Patch(typeof(EventSynchronizer), "ChooseOptionForSharedEvent", nameof(SharedOption), nameof(SharedEnded));
        Patch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex", nameof(EventVote));
        Patch(typeof(EventSynchronizer), "ChooseSharedEventOption", nameof(SourceDecisionOnly));
        Patch(typeof(MapSelectionSynchronizer), "MoveToMapCoord", nameof(SourceDecisionOnly));
        h.Patch(typeof(MapSelectionSynchronizer).GetMethod("PlayerVotedForMapCoord")!, postfix: new HarmonyMethod(typeof(MultiplayerNoncombat).GetMethod(nameof(MapVotePresented), Any)));
        Patch(typeof(RestSiteSynchronizer), "ChooseOption", nameof(RestOption));
#if STS2_0111
        Patch(typeof(RestSiteSynchronizer), "HandleRestSiteSkippedMessage", nameof(RestSkipped));
#endif
        Patch(typeof(RewardsSetSynchronizer), "SelectRewardForPlayer", nameof(RewardSelected), filter: m => m.GetParameters().Last().ParameterType == typeof(Reward));
        Patch(typeof(RewardsSetSynchronizer), "SkipRewardsSet", nameof(RewardSkipped));
        Patch(typeof(MerchantEntry), "OnTryPurchaseWrapper", nameof(Purchase));
        Patch(typeof(MerchantCardRemovalEntry), "OnTryPurchaseWrapper", nameof(PurchaseRemoval), filter: m => m.DeclaringType == typeof(MerchantCardRemovalEntry));
        Patch(typeof(RewardSynchronizer), "HandleRewardObtainedMessage", nameof(Obtained));
        Patch(typeof(RewardSynchronizer), "HandleGoldLostMessage", nameof(GoldLost));
        Patch(typeof(RewardSynchronizer), "HandleCardRemovedMessage", nameof(CardRemoved));
        Patch(typeof(OneOffSynchronizer), "HandleMerchantCardRemoval", nameof(MerchantRemoval));
        Patch(typeof(OneOffSynchronizer), "DoTreasureRoomRewards", nameof(TreasureGold));
        Patch(typeof(NTreasureRoom), "OpenChest", nameof(OpenChest));
        Patch(typeof(RunManager), "ProceedFromTerminalRewardsScreen", nameof(Proceed));
        InstallCrystal(h);
        h.Patch(typeof(NRestSiteRoom).GetMethod("_Ready")!, postfix: new HarmonyMethod(typeof(MultiplayerNoncombat).GetMethod(nameof(RestPortraits), Any)));
        Patch(typeof(NCardRewardSelectionScreen), "OptionSelected", nameof(RewardChoice));
        foreach (string name in new[] { "SelectCard", "OnAlternateRewardSelected", "OnSkipButtonPressed" })
            Patch(typeof(NCardRewardSelectionScreen), name, nameof(SourceDecisionOnly));
    }
    private static void ReplayCheckpoint()
    { if (Source) { Operations.Clear(); Checkpoint = null; Room = Array.Empty<byte>(); State = RunManager.Instance.DebugOnlyGetState(); SourceRoom = null; } }
    private static void RoomEntering(AbstractRoom room, bool isRestoringRoomStackBase)
    {
        if (!Source || isRestoringRoomStackBase || room is CombatRoom) return;
        CaptureRoom(room, false);
    }
    private static void RewardsEntering(CombatRoom __instance) { if (Source) CaptureRoom(__instance, true); }
    private static void CaptureRoom(AbstractRoom room, bool finishedCombat)
    {
        var m = RunManager.Instance; State = m.DebugOnlyGetState(); SourceRoom = room;
        Operations.Clear(); var descriptor = room.ToSerializable(); if (finishedCombat) descriptor.IsPreFinished = true;
        Room = MirrorState.Pack(descriptor);
        Checkpoint = new CombatReplay { version = "RMPNoncombat", gitCommit = "native", modelIdHash = ModelIdSerializationCache.Hash, serializableRun = m.ToSave(null), choiceIds = m.PlayerChoiceSynchronizer.ChoiceIds.ToList(), rewardIds = m.RewardsSetSynchronizer.GetNextRewardIds().ToList(),
            nextActionId = m.ActionQueueSet.NextActionId, nextHookId = m.ActionQueueSynchronizer.NextHookId, nextChecksumId = m.ChecksumTracker.NextId };
    }
    private static void Action(GameAction __0)
    {
        if (!Recording) return;
        // Travel is represented by the next room checkpoint. Never allow a
        // passive replica to decide a map tie or leave the source's room.
        if (__0 is MoveToMapCoordAction) return;
        CombatReplayEvent item;
        if (__0 is GenericHookGameAction hook) item = new() { eventType = CombatReplayEventType.HookAction, playerId = __0.OwnerId, hookId = hook.HookId, gameActionType = hook.ActionType };
        else if (__0.RecordableToReplay) item = new() { eventType = CombatReplayEventType.GameAction, playerId = __0.OwnerId, action = __0.ToNetAction() };
        else return;
        Add("multiplayer", payload: MirrorState.Pack(item));
    }
    private static void Resume(uint __0) => Add("multiplayer", payload: MirrorState.Pack(new CombatReplayEvent { eventType = CombatReplayEventType.ResumeAction, actionId = __0 }));
    private static bool LocalChoice(Player player, uint choiceId, PlayerChoiceResult result)
    { if (Replica) return false; ReceivedChoice(player, choiceId, result.ToNetData()); return true; }
    private static void ReceivedChoice(Player player, uint choiceId, NetPlayerChoiceResult result)
        => Add("multiplayer", payload: MirrorState.Pack(new CombatReplayEvent { eventType = CombatReplayEventType.PlayerChoice, playerId = player.NetId, choiceId = choiceId, playerChoiceResult = result }));
    private static void EventOption(Player __0, int __1) { if (_sharedDepth == 0) Add("event", __0.NetId, __1.ToString()); }
    private static void SharedOption(uint __0) { Add("sharedEvent", value: __0.ToString()); _sharedDepth++; }
    private static void SharedEnded() => _sharedDepth--;
    private static void EventVote(Player __0, uint __1, uint __2) => Add("eventVote", __0.NetId, __1 + ":" + __2);
    private static bool SourceDecisionOnly() => !Replica;
    private static void MapVotePresented(MapSelectionSynchronizer __instance, Player player)
    {
        // The original local map click normally paints our vote before its
        // action executes. Replay receives the authoritative action directly,
        // so paint that local vote through the same original UI method.
        if (!Replica || !LocalContext.IsMe(player) || MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance is not { } map) return;
        var previous = map.PlayerVoteDictionary.TryGetValue(player, out var coord) ? coord : null;
        Invoke(map, "OnPlayerVoteChangedInternal", player, previous, __instance.GetVote(player)?.coord);
    }
    private static void RestOption(Player __0, int __1) => Add("rest", __0.NetId, __1.ToString());
#if STS2_0111
    private static void RestSkipped(RestSiteSkippedMessage __0, ulong __1) => Add("restSkip", __1, payload: MirrorState.Pack(__0));
#endif
    private static RewardsSet Set(object state) => (RewardsSet)Read(state, "set")!;
    private static void RewardSelected(object __0, Reward __1) { var set = Set(__0); Add("reward", set.Player.NetId, set.Id + ":" + set.Rewards.IndexOf(__1)); }
    private static void RewardSkipped(object __0) { var set = Set(__0); Add("skipRewards", set.Player.NetId, set.Id.ToString()); }
    private static void Purchase(MerchantEntry __instance, MerchantInventory? inventory, bool ignoreCost)
    {
        if (!Recording || inventory == null) return;
        Add("purchase", inventory.Player.NetId, inventory.AllEntries.ToList().IndexOf(__instance) + ":" + ignoreCost);
    }
    private static void PurchaseRemoval(MerchantCardRemovalEntry __instance, MerchantInventory? inventory, bool ignoreCost, bool cancelable)
    {
        // The native removal slot is outside the merchant rug and passes null.
        // Identify its actual inventory by entry identity, preserving ownership.
        inventory ??= (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom as MerchantRoom)?.Inventories.FirstOrDefault(i => ReferenceEquals(i.CardRemovalEntry, __instance));
        if (inventory != null) Add("purchaseRemoval", inventory.Player.NetId, ignoreCost + ":" + cancelable);
    }
    private static void Obtained(RewardObtainedMessage __0, ulong __1) => Add("obtained", __1, payload: MirrorState.Pack(__0));
    private static void GoldLost(GoldLostMessage __0, ulong __1) => Add("goldLost", __1, payload: MirrorState.Pack(__0));
    private static void CardRemoved(CardRemovedMessage __0, ulong __1) => Add("cardRemoved", __1, payload: MirrorState.Pack(__0));
    private static void MerchantRemoval(MerchantCardRemovalMessage __0, ulong __1) => Add("merchantRemoval", __1, payload: MirrorState.Pack(__0));
    private static void TreasureGold(Player __0) { if (!LocalContext.IsMe(__0)) Add("treasureGold", __0.NetId); }
    private static void OpenChest() => Add("chest");
    private static void Proceed() => Add("proceed");
    private static void RestPortraits(NRestSiteRoom __instance)
    {
        if (!Source && !Replica) return;
        var state = RunManager.Instance.DebugOnlyGetState(); var me = LocalContext.GetMe(state); if (me == null) return;
        if (Read(__instance, "_characterContainers") is not List<Control> seats) return;
        int own = state!.GetPlayerSlotIndex(me); if (own < 0 || own >= seats.Count) return;
        if (own != 0)
        {
            (seats[0].Position, seats[own].Position) = (seats[own].Position, seats[0].Position);
            if (own % 2 != 0) { __instance.GetCharacterForPlayer(me)?.FlipX(); __instance.GetCharacterForPlayer(state.Players[0])?.FlipX(); }
        }
        seats[own].ZIndex = seats.Select(s => s.ZIndex).Max() + 1;
    }
    private static bool RewardChoice(NCardRewardSelectionScreen __instance, ref Task<int?> __result)
    {
        if (!Replica) return true;
        var m = RunManager.Instance; var player = LocalContext.GetMe(m.DebugOnlyGetState())!;
        var id = m.PlayerChoiceSynchronizer.ChoiceIds[m.DebugOnlyGetState()!.GetPlayerSlotIndex(player)] - 1;
        var completion = new TaskCompletionSource<int?>();
        typeof(NCardRewardSelectionScreen).GetField("_completionSource", Any)!.SetValue(__instance, completion);
        __result = AwaitReward(m.PlayerChoiceSynchronizer.WaitForRemoteChoice(player, id), completion); return false;
    }
    private static async Task<int?> AwaitReward(Task<PlayerChoiceResult> result, TaskCompletionSource<int?> completion)
    { var value = (await result).AsIndexOrNull(); completion.TrySetResult(value); return value; }
    internal static AbstractRoom RestoreRoom(byte[] payload, RunState state)
    {
        var room = MirrorState.Unpack<SerializableRoom>(payload);
        return room.RoomType switch { RoomType.Map => new MapRoom(), RoomType.Shop => new MerchantRoom(), RoomType.RestSite => new RestSiteRoom(), RoomType.Treasure => new MegaCrit.Sts2.Core.Rooms.TreasureRoom(state.CurrentActIndex),
            RoomType.Event => new EventRoom(room), RoomType.Monster or RoomType.Elite or RoomType.Boss when room.IsPreFinished => CombatRoom.FromSerializable(room, state), _ => throw new InvalidOperationException("Unsupported noncombat room") };
    }
    // Only a fixed set of native operations is accepted by the renderer. It
    // cannot invoke a type or method named by an untrusted viewer.
    internal static Task? Apply(MirrorOperation op, RunState state)
    {
        var m = RunManager.Instance; var player = ulong.TryParse(op.NodeKey, out var id) ? state.GetPlayer(id) : null;
        var values = op.Value.Split(':');
        switch (op.Kind)
        {
            case "event": Invoke(m.EventSynchronizer, "ChooseOptionForEvent", player, int.Parse(op.Value)); break;
            case "sharedEvent": Invoke(m.EventSynchronizer, "ChooseOptionForSharedEvent", uint.Parse(op.Value)); break;
            case "eventVote": Invoke(m.EventSynchronizer, "PlayerVotedForSharedOptionIndex", player, uint.Parse(values[0]), uint.Parse(values[1])); break;
            case "rest":
                if (LocalContext.IsMe(player))
                {
                    var option = m.RestSiteSynchronizer.GetOptionsForPlayer(player!).ElementAt(int.Parse(op.Value));
                    return (Task)Invoke(NRun.Instance.RestSiteRoom.GetButtonForOption(option)!, "SelectOption", option)!;
                }
                return (Task)Invoke(m.RestSiteSynchronizer, "ChooseOption", player, int.Parse(op.Value))!;
#if STS2_0111
            case "restSkip": Invoke(m.RestSiteSynchronizer, "HandleRestSiteSkippedMessage", MirrorState.Unpack<RestSiteSkippedMessage>(op.Payload), id); break;
#endif
            case "reward":
                if (LocalContext.IsMe(player) && NOverlayStack.Instance.Peek() is NRewardsScreen rewardScreen &&
                    Read(rewardScreen, "_rewardsSet") is RewardsSet localSet && localSet.Id == int.Parse(values[0]))
                {
                    // The native button awaits selection, emits RewardClaimed
                    // and removes/reflows its row. Applying only the network
                    // selection updates the deck but leaves stale local rows.
                    var reward = localSet.Rewards.ElementAt(int.Parse(values[1]));
                    var button = LocalSpectatorSource.Descendants<NRewardButton>(rewardScreen).FirstOrDefault(b => ReferenceEquals(b.Reward, reward));
                    if (button == null) throw new InvalidOperationException("Authoritative local reward button is absent");
                    return (Task)Invoke(button, "GetReward")!;
                }
                m.RewardsSetSynchronizer.HandleRewardSelectedMessage(new() { location = state.RunLocation, setId = int.Parse(values[0]), rewardIndex = int.Parse(values[1]) }, id); break;
            case "skipRewards":
                if (LocalContext.IsMe(player) && NOverlayStack.Instance.Peek() is NRewardsScreen rewardsScreen && Read(rewardsScreen, "_isTerminal") is false &&
                    Read(rewardsScreen, "_rewardsSet") is RewardsSet displayed && displayed.Id == int.Parse(op.Value))
                    Invoke(rewardsScreen, "OnProceedButtonPressed", Read(rewardsScreen, "_proceedButton"));
                else m.RewardsSetSynchronizer.HandleRewardSetSkippedMessage(new() { location = state.RunLocation, setId = int.Parse(op.Value) }, id);
                break;
            case "purchase":
                var inventory = ((MerchantRoom)state.CurrentRoom!).Inventories[state.GetPlayerSlotIndex(player!)];
                if (LocalContext.IsMe(player) && !bool.Parse(values[1]))
                {
                    var entry = inventory.AllEntries.ElementAt(int.Parse(values[0]));
                    return (Task)Invoke(LocalSpectatorSource.Descendants<NMerchantSlot>(NRun.Instance.MerchantRoom).First(s => ReferenceEquals(s.Entry, entry)), "OnSelected")!;
                }
                return inventory.AllEntries.ElementAt(int.Parse(values[0])).OnTryPurchaseWrapper(inventory, bool.Parse(values[1]));
            case "purchaseRemoval":
                var removalInventory = ((MerchantRoom)state.CurrentRoom!).Inventories[state.GetPlayerSlotIndex(player!)];
                if (LocalContext.IsMe(player) && !bool.Parse(values[0]) && bool.Parse(values[1]))
                    return (Task)Invoke(LocalSpectatorSource.Descendants<NMerchantSlot>(NRun.Instance.MerchantRoom).First(s => ReferenceEquals(s.Entry, removalInventory.CardRemovalEntry)), "OnSelected")!;
                return removalInventory.CardRemovalEntry!.OnTryPurchaseWrapper(removalInventory, bool.Parse(values[0]), bool.Parse(values[1]));
            case "obtained": Invoke(m.RewardSynchronizer, "HandleRewardObtainedMessage", UnpackMessage<RewardObtainedMessage>(op.Payload), id); break;
            case "goldLost": Invoke(m.RewardSynchronizer, "HandleGoldLostMessage", UnpackMessage<GoldLostMessage>(op.Payload), id); break;
            case "cardRemoved": Invoke(m.RewardSynchronizer, "HandleCardRemovedMessage", MirrorState.Unpack<CardRemovedMessage>(op.Payload), id); break;
            case "merchantRemoval": Invoke(m.OneOffSynchronizer, "HandleMerchantCardRemoval", MirrorState.Unpack<MerchantCardRemovalMessage>(op.Payload), id); break;
            case "treasureGold": return (Task)Invoke(m.OneOffSynchronizer, "DoTreasureRoomRewards", player)!;
            case "chest": return (Task)Invoke(NRun.Instance.TreasureRoom, "OpenChest")!;
            case "proceed": return m.ProceedFromTerminalRewardsScreen();
            case "crystalCell": case "crystalTool": return ApplyCrystal(op, state);
            case "crystalRewards": Invoke(m.OneOffSynchronizer, "HandleCrystalSphereRewardsMessage", MirrorState.Unpack<CrystalSphereRewardsMessage>(op.Payload), id); break;
            default: throw new InvalidOperationException("Unknown noncombat operation: " + op.Kind);
        }
        return null;
    }
    private static T UnpackMessage<T>(byte[] payload) where T : struct, IPacketSerializable
    { var reader = new PacketReader(); reader.Reset(payload); T value = default; value.Deserialize(reader); return value; }
}
