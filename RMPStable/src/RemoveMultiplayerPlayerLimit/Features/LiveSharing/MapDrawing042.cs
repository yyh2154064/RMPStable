using System;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Native map coordinates are independent of viewport size, pan and DPI.
// Drawing is a presentation stream; gameplay still uses full context checks.
internal static class MapDrawing042
{
    private static NMapDrawings? _stroke;
    private static long _epoch;
    internal static void Stop()
    {
        if (_stroke != null && GodotObject.IsInstanceValid(_stroke) && _stroke.IsInsideTree() && _stroke.IsLocalDrawing()) _stroke.StopLineLocal();
        _stroke = null;
    }
    internal static Vector2 ToNet(NMapDrawings drawing, Vector2 local) => (Vector2)MultiplayerNoncombat.Invoke(drawing, "ToNetPosition", local)!;
    internal static Vector2 FromNet(NMapDrawings drawing, Vector2 net) => (Vector2)MultiplayerNoncombat.Invoke(drawing, "FromNetPosition", net)!;
    internal static bool Execute(string value, long epoch)
    {
        if (NMapScreen.Instance?.Drawings is not { } drawings || RunManager.Instance.DebugOnlyGetState()?.IsGameOver != false) return false;
        var pieces = value.Split(':', 2);
        if (pieces.Length != 2) return false;
        string kind = pieces[0];
        var points = pieces[1].Length == 0 ? Array.Empty<string>() : pieces[1].Split(';');
        if (points.Length > 64) return false;
        var positions = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var xy = points[i].Split(',');
            if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) > 4 || y < -.5f || y > 1.5f) return false;
            positions[i] = FromNet(drawings, new Vector2(x, y));
        }
        if (_epoch != epoch || _stroke != drawings)
        {
            if (_stroke != null && GodotObject.IsInstanceValid(_stroke) && _stroke.IsInsideTree() && _stroke.IsLocalDrawing()) _stroke.StopLineLocal();
            _stroke = null; _epoch = epoch;
        }
        switch (kind)
        {
            case "draw": case "erase": case "pen":
                if (positions.Length != 1 || _stroke != null || drawings.IsLocalDrawing()) return false;
                var mode = kind == "erase" ? DrawingMode.Erasing : DrawingMode.Drawing;
                drawings.BeginLineLocal(positions[0], mode); _stroke = drawings; return true;
            case "move":
                if (_stroke != drawings || !drawings.IsLocalDrawing()) return false;
                foreach (var p in positions) drawings.UpdateCurrentLinePositionLocal(p);
                return true;
            case "end": case "cancel":
                if (_stroke == drawings && drawings.IsLocalDrawing()) drawings.StopLineLocal();
                _stroke = null;
                if (kind == "cancel") drawings.SetDrawingModeLocal(DrawingMode.None);
                return true;
            default: return false;
        }
    }
    internal static void Replay(NMapDrawings drawing, ulong player, string kind, string value)
    {
        var state = MultiplayerNoncombat.Invoke(drawing, "GetDrawingStateForPlayer", player)!;
        var parts = value.Split(',');
        Vector2 Point(int offset) => FromNet(drawing, new Vector2(float.Parse(parts[offset], CultureInfo.InvariantCulture), float.Parse(parts[offset + 1], CultureInfo.InvariantCulture)));
        switch (kind)
        {
            case "drawStart": MultiplayerNoncombat.Invoke(drawing, "BeginLine", state, Point(1), (DrawingMode)int.Parse(parts[0])); break;
            case "drawMove": MultiplayerNoncombat.Invoke(drawing, "UpdateCurrentLinePosition", state, Point(0)); break;
            case "drawEnd": MultiplayerNoncombat.Invoke(drawing, "StopDrawingLine", state); break;
            case "drawMode": MultiplayerNoncombat.Invoke(drawing, "SetDrawingMode", state, (DrawingMode)int.Parse(parts[0])); break;
        }
        if (player == MirrorRenderer.Active?.ViewSource && kind != "drawMove") MultiplayerNoncombat.Invoke(drawing, "UpdateLocalCursor");
    }
}
