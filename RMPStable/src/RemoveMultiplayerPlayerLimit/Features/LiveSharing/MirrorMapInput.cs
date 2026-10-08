using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private readonly Queue<(long Generation, long Epoch, string Key, string Kind, string Value)> _mapInputs = new();
    private string _mapGestureKey = "", _mapGestureMode = "";
    private Vector2 _mapGesturePoint;
    private static string Coordinates(Vector2 point) => point.X.ToString(CultureInfo.InvariantCulture) + "," + point.Y.ToString(CultureInfo.InvariantCulture);
    private void QueueMapInput(string kind, string value)
    {
        // Leave room for the release/cancel even during a long stroke.
        int limit = value.StartsWith("move,", StringComparison.Ordinal) || kind == "scroll" ? 2046 : 2048;
        if (_mapInputs.Count < limit) _mapInputs.Enqueue((_desiredGeneration, _epoch, _mapGestureKey, kind, value));
    }
    private void StartMapGesture(Vector2 point, string mode)
    {
        if (_state == null || _waitingForAuthority || _mapGestureKey.Length > 0) return;
        var action = _commands.CaptureCommands(_state).Control.Actions.FirstOrDefault(a => a.Enabled && a.Kind == (mode == "pan" ? "scroll" : "mapInput"));
        if (action == null) return;
        _mapGestureKey = action.NativePath; _mapGestureMode = mode; _mapGesturePoint = point;
        if (mode != "pan") QueueMapInput("mapInput", mode + "," + Coordinates(ViewportPoint(point)));
    }
    private void CancelMapDrawing(Vector2 point)
    {
        if (_mapGestureKey.Length > 0) FinishMapGesture(point);
        var action = _state == null ? null : _commands.CaptureCommands(_state).Control.Actions.FirstOrDefault(a => a.Enabled && a.Kind == "mapInput");
        if (action == null) return;
        _mapGestureKey = action.NativePath;
        QueueMapInput("mapInput", "cancel," + Coordinates(ViewportPoint(point)));
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
        else QueueMapInput("mapInput", "move," + Coordinates(ViewportPoint(point)));
        _mapGesturePoint = point;
    }
    private void FinishMapGesture(Vector2 point)
    {
        UpdateMapGesture(point);
        if (_mapGestureMode != "pan") QueueMapInput("mapInput", "end," + Coordinates(ViewportPoint(point)));
        _mapGestureKey = ""; _mapGestureMode = "";
    }
    private void ProcessMapGesture()
    {
        if (!_control || _generation != _desiredGeneration || NMapScreen.Instance?.IsOpen != true)
        { _mapGestureKey = ""; _mapGestureMode = ""; _mapInputs.Clear(); return; }
        UpdateMapGesture(_pointer);
        while (_mapInputs.TryPeek(out var pending) && (pending.Generation != _desiredGeneration || pending.Epoch != _epoch)) _mapInputs.Dequeue();
        if (NativeIdle && !_waitingForAuthority && _mapInputs.TryDequeue(out var input))
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
            SendIntent(input.Kind, -1, -1, MirrorState.Hash(_state!), _events, input.Key, value);
        }
    }
    private static void ReplayDrawing(NMapDrawings drawings, MirrorOperation operation)
    {
        var parts = operation.Value.Split(',');
        float Number(int index) => float.Parse(parts[index], CultureInfo.InvariantCulture);
        switch (operation.Kind)
        {
            case "drawStart": drawings.BeginLineLocal(new Vector2(Number(1), Number(2)), (DrawingMode)int.Parse(parts[0], CultureInfo.InvariantCulture)); break;
            case "drawMove": drawings.UpdateCurrentLinePositionLocal(new Vector2(Number(0), Number(1))); break;
            case "drawEnd": drawings.StopLineLocal(); break;
            case "drawMode": drawings.SetDrawingModeLocal((DrawingMode)int.Parse(parts[0], CultureInfo.InvariantCulture)); break;
            default: throw new InvalidOperationException("Unknown native drawing operation");
        }
    }
}
