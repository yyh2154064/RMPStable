using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Validation;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MirrorFinalMetadata
{
    public string History { get; set; } = "";
    public string Progress { get; set; } = "";
}

internal static partial class MirrorJournal
{
    private static RunState? _endedState;
    private static MirrorOperation? _ending;
    // Original summary still creates and animates badges. Only the authority
    // awards them; replay profiles have no played-character statistics.
    private static bool ReplicaBadgeSave() => !MirrorRenderer.IsRenderer;
    internal static string? EndHash(RunState state) => ReferenceEquals(state, _endedState) && _ending != null ?
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_ending.Payload.Concat(System.Text.Encoding.UTF8.GetBytes(_ending.Value)).ToArray())) : null;
    private static bool RunEnded(SerializableRun serializableRun)
    {
        // Native lethal actions can also request an ending during replay. Only
        // the authority's immutable final record may create that overlay; a
        // second overlay would destroy the visuals reparented into the first.
        if (MirrorRenderer.IsRenderer) return false;
        if (RunManager.Instance.NetService?.Type is not (MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer or MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host or MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client)) return true;
        var state = RunManager.Instance.DebugOnlyGetState();
        bool multiplayer = RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer;
        if (state == null || !multiplayer && Checkpoint == null || ReferenceEquals(_endedState, state)) return true;
        // The original final record is immutable presentation data. It includes
        // the authoritative score/history; no replica achievements are awarded.
        var saved = MirrorState.Unpack<SerializableRun>(MirrorState.Pack(serializableRun));
        saved.PreFinishedRoom = state.CurrentRoom?.ToSerializable();
        _endedState = state;
        _ending = new MirrorOperation { Kind = "ended", Payload = MirrorState.Pack(saved),
            Value = System.Text.Json.JsonSerializer.Serialize(new MirrorFinalMetadata {
                History = RunManager.Instance.History == null ? "" : JsonSerializationUtility.ToJson(RunManager.Instance.History),
                Progress = JsonSerializationUtility.ToJson(SaveManager.Instance.Progress.ToSerializable()) }) };
        if (multiplayer) MultiplayerMirrorSession.RecordFinal(_ending, state);
        else Operations.Add(_ending);
        GD.Print("[RMP:Mirror:Ending] authoritative native results recorded");
        return true;
    }
}

internal sealed partial class MirrorRenderer
{
    private SerializableRun? _finalSave;
    private string _finalHash = "";
    private string CurrentHash => _finalSave != null ? _finalHash : Multiplayer ? MirrorState.MultiplayerHash(_state!) : MirrorState.Hash(_state!);
    private async Task PresentEnding(MirrorOperation ending, long generation)
    {
        CheckGeneration(generation);
        // Enqueued replay actions run asynchronously. Let the final lethal
        // action finish its cast/damage animation before setting the terminal
        // pose and handing the visuals to the native results screen.
        while (RunManager.Instance.ActionExecutor.IsRunning) await Frame(generation);
        _finalSave = MirrorState.Unpack<SerializableRun>(ending.Payload);
        var presentationState = RunState.FromSerializable(_finalSave);
        // Native final UI only needs the victory-room identity. FromSerializable
        // accepts combat/event checkpoints, not map/shop/rest/treasure rooms.
        if (_finalSave.PreFinishedRoom?.RoomType == RoomType.Event) presentationState.PushRoom(new EventRoom(_finalSave.PreFinishedRoom));
        foreach (var player in presentationState.Players) player.Creature.SetCurrentHpInternal(0);
        // The native combat ending reparents the existing visuals; it never
        // starts their death animation. Victory also kills players outside the
        // combat replay. Apply the terminal presentation to the live nodes
        // before the native overlay takes ownership, without running gameplay
        // death hooks or generating a second result in the replay process.
        if (NCombatRoom.Instance is { } combatRoom)
            foreach (var player in _state!.Players)
            {
                player.Creature.SetCurrentHpInternal(0);
                var creature = combatRoom.GetCreatureNode(player.Creature);
                if (creature != null && !creature.IsPlayingDeathAnimation)
                    creature.StartDeathAnim(false);
            }
        var metadata = System.Text.Json.JsonSerializer.Deserialize<MirrorFinalMetadata>(ending.Value)!;
        RunManager.Instance.History = metadata.History.Length == 0 ? null : JsonSerializationUtility.FromJson<RunHistory>(metadata.History).SaveData;
        // Native score/unlock presentation starts from the authority's progress,
        // not the replica's fresh profile. All subsequent native writes remain
        // inside that isolated profile, never the authority's save directory.
        SaveManager.Instance.Progress = ProgressState.FromSerializable(JsonSerializationUtility.FromJson<SerializableProgress>(metadata.Progress).SaveData!, new DeserializationContext());
        CancelDrag(); _pendingCards.Clear(); _pressedAction = null; _pressedUi = null;
        NGame.Instance.InspectCardScreen?.Close(); NGame.Instance.InspectRelicScreen?.Close();
        NCapstoneContainer.Instance.Close(); NMapScreen.Instance.Close(false); NOverlayStack.Instance.Clear();
        NOverlayStack.Instance.Push(NGameOverScreen.Create(presentationState, _finalSave));
        _finalHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ending.Payload.Concat(System.Text.Encoding.UTF8.GetBytes(ending.Value)).ToArray()));
        _waitingForAuthority = false; _sceneReady = true;
        await NGame.Instance.Transition.FadeIn(.03f);
        CheckGeneration(generation); Stage("原版结算界面已同步");
    }
}
