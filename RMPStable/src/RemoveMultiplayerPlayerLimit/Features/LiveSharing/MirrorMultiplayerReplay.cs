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
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    internal bool Multiplayer { get; private set; }
    private ulong _viewSource;
    internal ulong ViewSource => _viewSource == 0 ? _wire.Identity.Source : _viewSource;
    internal long MultiplayerGeneration => _generation;
    private bool _multiplayerRoomWaiting;
    private async Task LoadMultiplayer(MirrorMessage message)
    {
        var presentationSpeed = SaveManager.Instance.PrefsSave.FastMode;
        var presentationFps = Engine.MaxFps;
        bool rendering = RenderingServer.RenderLoopEnabled;
        try
        {
            Multiplayer = true; _control = false;
            MultiplayerPersonalUi.Reset();
            MirrorJournal.ResetReplicaRewards();
            _generation = message.Generation; _wire.Identity.Generation = _generation; _events = 0; _error = "";
            _sceneReady = false; _finalSave = null; _finalHash = "";
            Stage("重建多人原版房间"); CheckGeneration(_generation);
            // This reconstruction is hidden and cannot accept input. Reuse
            // the existing checkpoint restore path's presentation-only speedup.
            MirrorFastRestore.Replica = !message.NormalOpening;
            // Native pile movements also select durations directly from this
            // presentation preference. Use the game's instant mode only in the
            // hidden replica reconstruction, and restore its user's setting.
            if (!message.NormalOpening)
            {
                SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant; Engine.MaxFps = 120;
                // The native scene still processes every action, hook, choice
                // and animation. Its hidden intermediate frames need no GPU
                // draws; restore rendering before revealing the native room.
                RenderingServer.RenderLoopEnabled = false;
            }
            RunManager.Instance.CleanUp(); _state = null; _replay = null;
            MirrorJournal.ReplicaActive = true;
            var replay = MirrorState.Unpack<CombatReplay>(message.Payload);
            if (message.ViewSource == 0 || replay.serializableRun.Players.All(p => p.NetId != message.ViewSource)) throw new InvalidDataException("Selected player is absent from checkpoint");
            _viewSource = message.ViewSource;
            _state = RunState.FromSerializable(replay.serializableRun); _replay = replay;
            var manager = RunManager.Instance;
#if STS2_0111
            manager.SetUpReplay(_state, replay, ViewSource);
#else
            // The constructor patch binds the replay service before its
            // synchronizers are initialized, preserving every player slot.
            manager.SetUpReplay(_state, replay);
#endif
            // Replicas consume the source's stream, never record another
            // replay. Direct event entry does not
            // initialize a native writer before its later nested combat.
            manager.CombatReplayWriter.IsEnabled = false;
            manager.CombatStateSynchronizer.IsDisabled = true;
            string characters = string.Join(",", _state.Players.Select(p => p.Character.Id.ToString()));
            if (_assetCharacters != characters)
            {
                await PreloadManager.LoadRunAssets(_state.Players.Select(p => p.Character));
                _assetCharacters = characters; _assetAct = "";
            }
            string act = _state.Act.Id.ToString();
            if (_assetAct != act) { await PreloadManager.LoadActAssets(_state.Act); _assetAct = act; }
            CheckGeneration(_generation);
            manager.Launch(); NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(_state));
            await manager.GenerateMap(); CheckGeneration(_generation);
            manager.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            manager.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            manager.ChecksumTracker.LoadReplayChecksums(null!, replay.nextChecksumId);
            manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            manager.RewardsSetSynchronizer.FastForwardRewardIds(replay.rewardIds);
            if (message.NoncombatRoom.Length > 0)
            {
                // FromSerializable does not restore ActFloor. Native map entry
                // derives it from row + 1; direct room restoration bypasses that
                // step, otherwise an ancient's map back button stays disabled.
                _state.ActFloor = _state.CurrentMapCoord is { } coordinate ? coordinate.row + 1 : 0;
                await manager.EnterRoom(MultiplayerNoncombat.RestoreRoom(message.NoncombatRoom, _state));
            }
            else await manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, _state));
            // Native multiplayer opening keeps the executor paused until all
            // automatic effects finish. A fresh SL must show that opening now,
            // not wait behind its entire animation before exposing the room.
            while (!message.NormalOpening && manager.ActionExecutor.IsPaused) await Frame(_generation);
            await ApplyMultiplayer(message, reconstructing: !message.NormalOpening);
            // Enqueuing the checkpoint's actions does not await their native
            // executor. Keep reconstruction's presentation speedup until that
            // executor catches up (a real suspended choice leaves it idle).
            // Otherwise the tail plays ordinary animations after the hidden
            // load, making source switches grow with the battle history.
            while (!message.NormalOpening && (manager.ActionExecutor.IsRunning || CombatManager.Instance.IsStarting ||
                CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo || CombatManager.Instance.IsEnemyTurnStarted))
            {
                // Opening combat and end-turn hooks can legitimately stop for
                // the selected player's input. Do not wait for that choice to
                // be completed before showing its native controls.
                var choice = MultiplayerPersonalUi.Capture();
                var expected = _personalMessage?.Choice;
                if (!manager.ActionExecutor.IsRunning && choice != null && expected != null &&
                    choice.Id == expected.Id && choice.Kind == expected.Kind) break;
                await Frame(_generation);
            }
            RenderingServer.RenderLoopEnabled = rendering;
            await NGame.Instance.Transition.FadeIn(.03f);
            CheckGeneration(_generation);
            if (_multiplayerRoomWaiting) return;
            _sceneReady = true; Stage("多人原版房间已同步（只读）");
        }
        catch (OperationCanceledException) { Stage("旧多人检查点已撤销"); }
        catch (Exception e) { _error = e.ToString(); GD.PrintErr("[RMP:MultiplayerReplay] " + e); }
        finally { RenderingServer.RenderLoopEnabled = rendering; Engine.MaxFps = presentationFps; SaveManager.Instance.PrefsSave.FastMode = presentationSpeed; MirrorFastRestore.Replica = false; _busy = false; _statusTimer = 0; }
    }
    private async Task ApplyMultiplayer(MirrorMessage message, bool reconstructing = false)
    {
        if (!message.Multiplayer || message.Index != _events || message.Events != message.Index + message.Operations.Count)
            throw new InvalidDataException("Multiplayer replica event gap");
        foreach (var operation in message.Operations)
        {
            CheckGeneration(_generation);
            if (operation.Kind == "ended")
            { await PresentEnding(operation, _generation); _events++; await Frame(_generation); continue; }
            if (operation.Kind == "finalButton")
            {
                MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl? button = null;
                for (int wait = 0; wait < 600; wait++)
                {
                    button = MirrorNativeUi.Resolve(operation.NodeKey) as MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl;
                    if (button?.IsEnabled == true && button.IsVisibleInTree()) break;
                    await Frame(_generation);
                }
                if (button?.IsEnabled != true || !button.IsVisibleInTree()) throw new InvalidDataException("Native ending button did not become ready");
                button.ForceClick(); _events++; await Frame(_generation); continue;
            }
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
            _events++;
            // Native queues buffer future choices, hooks and resumptions in
            // source order. Yield in small batches during hidden reconstruction
            // rather than adding one display frame per historical record.
            // Ordinary live deltas keep their existing pacing.
            if (!reconstructing || (_events - message.Index) % 8 == 0) await Frame(_generation);
        }
    }
    private async void TrackNoncombatTask(Task task, long generation)
    { try { await task; } catch (Exception e) { if (_generation == generation) { _error = e.ToString(); GD.PrintErr("[RMP:NoncombatReplay] " + e); } } }
    private void ApplyNoncombatPresentation(MirrorMessage message)
    {
        var map = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance;
        if (map != null && map.IsTravelEnabled != message.MapTravelEnabled) map.SetTravelEnabled(message.MapTravelEnabled);
        bool mapOpen = _localMapOpen ?? message.MapOpen;
        if (map != null && map.IsOpen != mapOpen) { if (mapOpen) map.Open(); else map.Close(false); }
        if (NRun.Instance?.MerchantRoom is { } shop)
            foreach (var inventory in LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantInventory>(shop))
                if (inventory.IsOpen != message.ShopOpen) { if (message.ShopOpen) inventory.Open(); else MultiplayerNoncombat.Invoke(inventory, "Close"); }
    }
}
