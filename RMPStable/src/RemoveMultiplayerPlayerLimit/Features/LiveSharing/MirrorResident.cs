using System;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// One native child belongs to the lifetime of this game process. Controllers
// lease its current view; leaving a run releases that lease, not the process.
internal static class MirrorResident
{
    private static MirrorHost? _host;
    internal static MirrorHost? Host => _host;
    internal static MirrorHost Acquire(ulong source, bool multiplayer)
    {
        if (_host?.Process.Child.HasExited == true)
        { _host.Dispose(); _host = null; }
        if (_host == null) _host = new MirrorHost(source, multiplayer);
        else
        {
            if (_host.Process.Wire.Error.Length > 0)
                throw new InvalidOperationException("常驻副本通信已失效，无法在原进程内恢复：" + _host.Process.Wire.Error);
            _host.Rebind(source, multiplayer);
        }
        return _host;
    }
    internal static void Release(MirrorHost? host)
    { if (ReferenceEquals(host, _host)) host?.Park(); }
    internal static void TickUnleased(MirrorHost? singleplayer, double delta)
    {
        if (_host != null && !ReferenceEquals(_host, singleplayer) && !ReferenceEquals(_host, MultiplayerMirrorSession.Mirror))
            _host.Tick(null, delta);
    }
}
