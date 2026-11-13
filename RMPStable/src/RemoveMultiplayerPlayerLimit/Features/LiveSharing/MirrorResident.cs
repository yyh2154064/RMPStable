namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Reuse the native child across runs while its process and pipe are healthy.
internal static class MirrorResident
{
    private static MirrorHost? _host;
    internal static MirrorHost? Host => _host;
    internal static MirrorHost Acquire(ulong source, bool multiplayer)
    {
        if (_host?.Process.NeedsReplacement == true)
        { _host.Dispose(); _host = null; }
        if (_host == null) _host = new MirrorHost(source, multiplayer);
        else _host.Rebind(source, multiplayer);
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
