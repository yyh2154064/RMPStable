using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private bool _localMapPan;
    private Vector2 _localMapPanPoint;
    private long _localMapPanGeneration, _localMapPanEpoch;
    private bool MapBrowseOpen => _sceneReady && _generation == _desiredGeneration && NMapScreen.Instance?.IsOpen == true &&
        NCapstoneContainer.Instance?.CurrentCapstoneScreen == null && NModalContainer.Instance?.OpenModal == null &&
        NGameInspectClosed;
    private static bool NGameInspectClosed => MegaCrit.Sts2.Core.Nodes.NGame.Instance?.InspectCardScreen?.IsVisibleInTree() != true &&
        MegaCrit.Sts2.Core.Nodes.NGame.Instance?.InspectRelicScreen?.IsVisibleInTree() != true;
    private bool TryBrowseMapScroll(Vector2 point, float wheel)
    {
        if (!MapBrowseOpen || !Contains(NMapScreen.Instance, point)) return false;
        if (wheel != 0) LocalSpectatorSource.ScrollNative(NMapScreen.Instance, WheelValue(wheel * 3.25f));
        return true;
    }
    private bool ProcessMapBrowsePan(uint message, Vector2 point)
    {
        if (_localMapPan && message == MirrorWin32.LeftUp)
        { UpdateMapBrowsePan(point); _localMapPan = false; return true; }
        if (message != MirrorWin32.LeftDown || !MapBrowseOpen || !Contains(NMapScreen.Instance, point) ||
            NMapScreen.Instance.Drawings.GetLocalDrawingMode(false) != DrawingMode.None) return false;
        // Map points and drawing tools still go through source authorization.
        if (LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>(NMapScreen.Instance)
            .Any(n => n.IsEnabled && Contains(InteractiveArea(n), point))) return false;
        _localMapPan = true; _localMapPanPoint = point;
        _localMapPanGeneration = _generation; _localMapPanEpoch = _epoch;
        return true;
    }
    private void UpdateMapBrowsePan(Vector2 point)
    {
        if (!_localMapPan) return;
        if (!MapBrowseOpen || _localMapPanGeneration != _generation || _localMapPanEpoch != _epoch)
        { _localMapPan = false; return; }
        float distance = (ViewportPoint(point) - ViewportPoint(_localMapPanPoint)).Y;
        if (distance != 0) LocalSpectatorSource.ScrollNative(NMapScreen.Instance,
            (distance > 0 ? "up:" : "down:") + Math.Min(64, Math.Abs(distance) / 40).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _localMapPanPoint = point;
    }
}
