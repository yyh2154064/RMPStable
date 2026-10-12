using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static partial class MultiplayerMirrorSession
{
    internal static bool IsRestoring { get; private set; }
    private static RunState? _restoreState;
    private static ulong[] _restorePlayers = Array.Empty<ulong>();
    private static ulong _restoreLocal, _restoreSource, _restoreDeadline;

    // Called only after the original Quick SL flow has accepted the reload.
    internal static void BeginQuickSlRestore()
    {
        if (MirrorRenderer.IsRenderer || _state == null || _service == null || IsRestoring) return;
        var previousState = _state;
        var previousPlayers = _state.Players.Select(p => p.NetId).OrderBy(id => id).ToArray();
        ulong previousLocal = _service.NetId, previousSource = _selected;
        LiveSharingController.HideMultiplayerForRestore();
        var retained = Mirror?.CanReuse == true ? Mirror : null;
        if (retained != null) Mirror = null;
        // Reset/Stop clears any old restore attempt. Publish the new restore
        // identity only afterwards, so ordinary teardown cannot erase it.
        Reset();
        _restoreState = previousState; _restorePlayers = previousPlayers;
        _restoreLocal = previousLocal; _restoreSource = previousSource;
        _restoreDeadline = Time.GetTicksMsec() + 60000;
        Mirror = retained; IsRestoring = true;
        retained?.SwitchMultiplayerSource(_restoreSource);
    }

    internal static void CancelQuickSlRestore()
    {
        if (!IsRestoring) return;
        IsRestoring = false; _restoreState = null; _restorePlayers = Array.Empty<ulong>();
        Stop();
    }

    private static bool ProcessQuickSlRestore(INetGameService? service, RunState? state, double delta)
    {
        if (!IsRestoring) return false;
        if (Time.GetTicksMsec() > _restoreDeadline) { CancelQuickSlRestore(); return false; }
        if (ReferenceEquals(state, _restoreState) || state == null || service?.IsConnected != true ||
            service.Type is not NetGameType.Host and not NetGameType.Client || NRun.Instance?.IsNodeReady() != true)
        { Mirror?.Tick(null, delta); return true; }
        if (service.NetId != _restoreLocal || !state.Players.Select(p => p.NetId).OrderBy(id => id).SequenceEqual(_restorePlayers))
        { CancelQuickSlRestore(); return false; }
        IsRestoring = false; _restoreState = null; _restorePlayers = Array.Empty<ulong>();
        // The previous room's session, subscription and permission are gone.
        // Bind the freshly connected service before requesting a new checkpoint.
        _service = service; _state = state; _session = Guid.NewGuid().ToString("N");
        service.RegisterMessageHandler<Network.RmpMirrorPacket>(Receive);
        if (_restoreSource != 0 && Connected(_restoreSource)) { Select(_restoreSource); Mirror?.PreserveOpeningOnce(); }
        else { MirrorResident.Release(Mirror); Mirror = null; }
        return false;
    }
}
