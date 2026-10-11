using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    internal bool Multiplayer { get; private set; }
    internal ulong ViewSource => _wire.Identity.Source;
    internal long MultiplayerGeneration => _generation;
    private bool _multiplayerRoomWaiting;
    private async Task LoadMultiplayer(MirrorMessage message)
    {
        try
        {
            Multiplayer = true; _control = false;
            MultiplayerPersonalUi.Reset();
            MirrorJournal.ResetReplicaRewards();
            _generation = message.Generation; _wire.Identity.Generation = _generation; _events = 0; _error = "";
            _sceneReady = false; _finalSave = null; _finalHash = "";
            Stage("重建多人原版房间"); CheckGeneration(_generation);
            RunManager.Instance.CleanUp(); _state = null; _replay = null;
            MirrorJournal.ReplicaActive = true;
            var replay = MirrorState.Unpack<CombatReplay>(message.Payload);
            if (replay.serializableRun.Players.All(p => p.NetId != _wire.Identity.Source)) throw new InvalidDataException("Selected player is absent from checkpoint");
            _state = RunState.FromSerializable(replay.serializableRun); _replay = replay;
            var manager = RunManager.Instance;
#if STS2_0111
            manager.SetUpReplay(_state, replay, _wire.Identity.Source);
#else
            // The constructor patch binds the replay service before its
            // synchronizers are initialized, preserving every player slot.
            manager.SetUpReplay(_state, replay);
#endif
            manager.CombatStateSynchronizer.IsDisabled = true;
            await PreloadManager.LoadRunAssets(_state.Players.Select(p => p.Character));
            await PreloadManager.LoadActAssets(_state.Act); CheckGeneration(_generation);
            manager.Launch(); NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(_state));
            await manager.GenerateMap(); CheckGeneration(_generation);
            manager.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            manager.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            manager.ChecksumTracker.LoadReplayChecksums(null!, replay.nextChecksumId);
            manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            manager.RewardsSetSynchronizer.FastForwardRewardIds(replay.rewardIds);
            if (message.NoncombatRoom.Length > 0) await manager.EnterRoom(MultiplayerNoncombat.RestoreRoom(message.NoncombatRoom, _state));
            else await manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, _state));
            while (manager.ActionExecutor.IsPaused) await Frame(_generation);
            await ApplyMultiplayer(message);
            await NGame.Instance.Transition.FadeIn(.03f);
            CheckGeneration(_generation);
            if (_multiplayerRoomWaiting) return;
            _sceneReady = true; Stage("多人原版房间已同步（只读）");
        }
        catch (OperationCanceledException) { Stage("旧多人检查点已撤销"); }
        catch (Exception e) { _error = e.ToString(); GD.PrintErr("[RMP:MultiplayerReplay] " + e); }
        finally { _busy = false; _statusTimer = 0; }
    }
    private async Task ApplyMultiplayer(MirrorMessage message)
    {
        if (!message.Multiplayer || message.Index != _events || message.Events != message.Index + message.Operations.Count)
            throw new InvalidDataException("Multiplayer replica event gap");
        foreach (var operation in message.Operations)
        {
            CheckGeneration(_generation);
            if (operation.Kind != "multiplayer")
            {
                var task = MultiplayerNoncombat.Apply(operation, _state!);
                // Some native room decisions await another player's choice.
                // Continue consuming the authoritative stream that resolves it.
                if (task != null) TrackNoncombatTask(task, _generation);
                _events++; await Frame(_generation); continue;
            }
            var item = MirrorState.Unpack<CombatReplayEvent>(operation.Payload); var manager = RunManager.Instance;
            switch (item.eventType)
            {
                case CombatReplayEventType.GameAction:
                    while (CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo) await Frame(_generation);
                    var action = item.action!.ToGameAction(_state!.GetPlayer(item.playerId!.Value));
                    if (action.ActionType == GameActionType.CombatPlayPhaseOnly)
                        while (CombatManager.Instance.DebugOnlyGetState().CurrentSide == CombatSide.Enemy) await Frame(_generation);
                    manager.ActionQueueSet.EnqueueWithoutSynchronizing(action);
                    break;
                case CombatReplayEventType.HookAction:
                    manager.ActionQueueSet.EnqueueWithoutSynchronizing(manager.ActionQueueSynchronizer.GetHookActionForId(item.hookId!.Value, item.playerId!.Value, item.gameActionType!.Value)); break;
                case CombatReplayEventType.ResumeAction:
                    manager.ActionQueueSet.ResumeActionWithoutSynchronizing(item.actionId!.Value); break;
                case CombatReplayEventType.PlayerChoice:
                    manager.PlayerChoiceSynchronizer.ReceiveReplayChoice(_state!.GetPlayer(item.playerId!.Value), item.choiceId!.Value, item.playerChoiceResult!.Value); break;
                default: throw new InvalidDataException("Unknown native replay event");
            }
            _events++; await Frame(_generation);
        }
    }
    private async void TrackNoncombatTask(Task task, long generation)
    { try { await task; } catch (Exception e) { if (_generation == generation) { _error = e.ToString(); GD.PrintErr("[RMP:NoncombatReplay] " + e); } } }
    private void ApplyNoncombatPresentation(MirrorMessage message)
    {
        var map = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance;
        if (map != null && map.IsTravelEnabled != message.MapTravelEnabled) map.SetTravelEnabled(message.MapTravelEnabled);
        if (map != null && map.IsOpen != message.MapOpen) { if (message.MapOpen) map.Open(); else map.Close(false); }
        if (NRun.Instance?.MerchantRoom is { } shop)
            foreach (var inventory in LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory>(shop))
                if (inventory.IsOpen != message.ShopOpen) { if (message.ShopOpen) inventory.Open(); else MultiplayerNoncombat.Invoke(inventory, "Close"); }
    }
}
