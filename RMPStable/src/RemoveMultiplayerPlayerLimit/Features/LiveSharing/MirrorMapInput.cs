using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private readonly Queue<(long Generation, long Epoch, string Key, string Kind, string Value, string Origin)> _mapInputs = new();
    private string _mapGestureKey = "", _mapGestureMode = "";
    private string _mapGestureOrigin = "";
    private readonly string _mapOrigin = Guid.NewGuid().ToString("N");
    private long _mapStrokeNumber;
    private MapDrawingPreview? _mapPreview;
    private Vector2 _mapGesturePoint;
    private string MapCoordinates(Vector2 point)
    {
        var drawings = NMapScreen.Instance.Drawings;
        var local = drawings.GetGlobalTransformWithCanvas().AffineInverse() * ViewportPoint(point);
        return Coordinates(MapDrawing042.ToNet(drawings, local));
    }
    private void QueueScroll(string key, string value)
    {
        if (_mapInputs.Count < 8190) _mapInputs.Enqueue((_desiredGeneration, _epoch, key, "scroll", value, ""));
    }
    private static string Coordinates(Vector2 point) => point.X.ToString(CultureInfo.InvariantCulture) + "," + point.Y.ToString(CultureInfo.InvariantCulture);
    private void QueueMapInput(string kind, string value)
    {
        // Leave room for the release/cancel even during a long stroke.
        int limit = value.StartsWith("move:", StringComparison.Ordinal) || kind == "scroll" ? 8190 : 8192;
        if (_mapInputs.Count < limit) _mapInputs.Enqueue((_desiredGeneration, _epoch, _mapGestureKey, kind, value, _mapGestureOrigin));
    }
    private void StartMapGesture(Vector2 point, string mode)
    {
        if (_state == null || !_control || !MapBrowseOpen || _mapGestureKey.Length > 0) return;
        _mapGestureKey = MirrorNativeUi.Key(NMapScreen.Instance); _mapGestureMode = mode; _mapGesturePoint = point;
        if (mode != "pan")
        {
            var drawings = NMapScreen.Instance.Drawings;
            string tool = mode == "pen" ? drawings.GetLocalDrawingMode(false) == DrawingMode.Erasing ? "erase" : "draw" : mode;
            _mapGestureOrigin = _mapOrigin + "-" + ++_mapStrokeNumber;
            _mapPreview ??= new MapDrawingPreview(drawings, ViewSource);
            var local = drawings.GetGlobalTransformWithCanvas().AffineInverse() * ViewportPoint(point);
            _mapPreview.Begin(local, tool == "erase" ? DrawingMode.Erasing : DrawingMode.Drawing, _mapGestureOrigin);
            QueueMapInput("mapStroke", tool + ":" + Coordinates(MapDrawing042.ToNet(drawings, local)));
        }
    }
    private void CancelMapDrawing(Vector2 point)
    {
        if (_mapGestureKey.Length > 0) FinishMapGesture(point);
        if (_state == null || !MapBrowseOpen) return;
        _mapGestureKey = MirrorNativeUi.Key(NMapScreen.Instance);
        QueueMapInput("mapStroke", "cancel:");
        SetLocalPenMode(DrawingMode.None);
        _mapGestureKey = "";
    }
    private void UpdateMapGesture(Vector2 point)
    {
        if (_mapGestureKey.Length == 0 || point == _mapGesturePoint) return;
        if (_mapGestureMode == "pan")
        {
            float distance = (ViewportPoint(point) - ViewportPoint(_mapGesturePoint)).Y;
            if (distance != 0) QueueMapInput("scroll", (distance > 0 ? "up:" : "down:") + Math.Min(64, Math.Abs(distance) / 40).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            var drawings = NMapScreen.Instance.Drawings;
            var local = drawings.GetGlobalTransformWithCanvas().AffineInverse() * ViewportPoint(point);
            _mapPreview?.Move(local);
            QueueMapInput("mapStroke", "move:" + Coordinates(MapDrawing042.ToNet(drawings, local)));
        }
        _mapGesturePoint = point;
    }
    private void FinishMapGesture(Vector2 point)
    {
        UpdateMapGesture(point);
        if (_mapGestureMode != "pan") { _mapPreview?.End(); QueueMapInput("mapStroke", "end:"); }
        _mapGestureKey = ""; _mapGestureMode = ""; _mapGestureOrigin = "";
    }
    private void DiscardMapPreview()
    { _mapPreview?.Dispose(); _mapPreview = null; }
    private void DrawingEcho(MirrorOperation operation, ulong player)
    {
        if (player != ViewSource || _mapPreview == null) return;
        _mapPreview.Echo(operation);
        if (_mapPreview.Settled) DiscardMapPreview();
    }
    private void ProcessMapGesture()
    {
        if (!_control || _generation != _desiredGeneration || _state == null)
        { _mapGestureKey = ""; _mapGestureMode = ""; _mapInputs.Clear(); DiscardMapPreview(); return; }
        if (NMapScreen.Instance?.IsOpen != true)
        { if (_mapGestureKey.Length > 0) FinishMapGesture(_pointer); DiscardMapPreview(); }
        UpdateMapGesture(_pointer);
        _mapPreview?.Tick();
        while (_mapInputs.TryPeek(out var pending) && (pending.Generation != _desiredGeneration || pending.Epoch != _epoch)) _mapInputs.Dequeue();
        // Do not serialize every segment behind a gameplay hash round trip.
        // The source validates the lease/generation and ordered native stroke.
        while (_mapInputs.TryPeek(out var stroke) && stroke.Kind == "mapStroke")
        {
            _mapInputs.Dequeue();
            string payload = stroke.Value;
            if (payload.StartsWith("move:", StringComparison.Ordinal))
            {
                var points = new List<string> { payload.Substring(5) };
                while (points.Count < 64 && _mapInputs.TryPeek(out var next) && next.Generation == stroke.Generation && next.Epoch == stroke.Epoch && next.Origin == stroke.Origin && next.Kind == "mapStroke" && next.Value.StartsWith("move:", StringComparison.Ordinal))
                { _mapInputs.Dequeue(); points.Add(next.Value.Substring(5)); }
                payload = "move:" + string.Join(";", points);
            }
            _ = _wire.Send(new MirrorMessage { Kind = "intent", Model = "mapStroke", Multiplayer = Multiplayer, Generation = _generation, Epoch = _epoch, Request = ++_request,
                Value = stroke.Origin.Length > 0 ? stroke.Origin + "|" + payload : payload });
        }
        if (NativeIdle && !_waitingForAuthority && _authorityReady && _authorityEvents == _events &&
            _authorityHash == MirrorState.Hash(_state) && _mapInputs.TryDequeue(out var input))
        {
            string value = input.Value;
            if (input.Kind == "mapInput" && value.StartsWith("move,", StringComparison.Ordinal))
            {
                var points = new List<string> { value.Substring(5) };
                while (points.Count < 64 && _mapInputs.TryPeek(out var next) && next.Generation == input.Generation && next.Epoch == input.Epoch &&
                    next.Key == input.Key && next.Kind == input.Kind && next.Value.StartsWith("move,", StringComparison.Ordinal))
                { _mapInputs.Dequeue(); points.Add(next.Value.Substring(5)); }
                value = "moves:" + string.Join(";", points);
            }
            else if (input.Kind == "scroll")
            {
                var parts = value.Split(':');
                float steps = float.Parse(parts[1], CultureInfo.InvariantCulture);
                while (_mapInputs.TryPeek(out var next) && next.Generation == input.Generation && next.Epoch == input.Epoch && next.Key == input.Key &&
                    next.Kind == "scroll" && next.Value.StartsWith(parts[0] + ":", StringComparison.Ordinal))
                {
                    float more = float.Parse(next.Value.Substring(next.Value.IndexOf(':') + 1), CultureInfo.InvariantCulture);
                    if (steps + more > 64) break;
                    _mapInputs.Dequeue(); steps += more;
                }
                value = parts[0] + ":" + steps.ToString(CultureInfo.InvariantCulture);
            }
            if (MirrorNativeUi.Resolve(input.Key) is Control node && node.IsVisibleInTree())
                SendIntent(input.Kind, -1, -1, MirrorState.Hash(_state!), _events, input.Key, value);
        }
    }
    private static void ShowMapSelection(NMapPoint point)
    {
        // Native presentation only. The next room still comes from the host checkpoint.
        if (point is NNormalMapPoint normal)
            typeof(NNormalMapPoint).GetMethod("ShowCircleVfx", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(normal, new object[] { true });
        var vfx = MegaCrit.Sts2.Core.Nodes.Vfx.NMapNodeSelectVfx.Create(point is NBossMapPoint ? 2f : point is NAncientMapPoint ? 1.5f : 1f);
        point.AddChild(vfx); vfx.Position += point.PivotOffset;
    }
    private void ReplayDrawing(NMapDrawings drawings, MirrorOperation operation)
    {
        var parts = operation.Value.Split(',');
        float Number(int index) => float.Parse(parts[index], CultureInfo.InvariantCulture);
        switch (operation.Kind)
        {
            case "drawStart": drawings.BeginLineLocal(MapDrawing042.FromNet(drawings, new Vector2(Number(1), Number(2))), (DrawingMode)int.Parse(parts[0], CultureInfo.InvariantCulture)); break;
            case "drawMove": drawings.UpdateCurrentLinePositionLocal(MapDrawing042.FromNet(drawings, new Vector2(Number(0), Number(1)))); break;
            case "drawEnd": drawings.StopLineLocal(); break;
            case "drawMode": drawings.SetDrawingModeLocal((DrawingMode)int.Parse(parts[0], CultureInfo.InvariantCulture)); break;
            default: throw new InvalidOperationException("Unknown native drawing operation");
        }
        DrawingEcho(operation, ViewSource);
    }
}
