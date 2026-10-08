using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class LocalSpectatorSource
{
	private bool _mapInputInPanel;
	private static readonly FieldInfo? MapDrawingInput = typeof(NMapScreen).GetField("_drawingInput", BindingFlags.Instance | BindingFlags.NonPublic);
	private static NMapDrawingInput? CurrentMapInput => NMapScreen.Instance is { } map ? MapDrawingInput?.GetValue(map) as NMapDrawingInput : null;
	private NMapDrawings? _remoteMapStroke;
	internal void RouteNativeMapInput(bool insidePanel)
	{
		_mapInputInPanel = insidePanel;
		if (!insidePanel) RestoreNativeInput();
		else { if (MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager.Instance?.IsInSelection == true) PauseNativeInput(MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager.Instance); PauseNativeInput(NativePopup); }
		// Native _Input runs outside GUI hit testing. Give the native pen back
		// immediately outside the panel; inside, only translated commands draw.
		if (CurrentMapInput is { } input && Ready(input))
		{
			if (insidePanel && input.IsProcessingInput()) { _pausedNativeInput.Add(input); input.SetProcessInput(false); }
			else if (!insidePanel && _pausedNativeInput.Remove(input)) input.SetProcessInput(true);
		}
	}
	private void StopRemoteMapStroke()
	{
		if (_remoteMapStroke != null && GodotObject.IsInstanceValid(_remoteMapStroke) && _remoteMapStroke.IsInsideTree() && _remoteMapStroke.IsLocalDrawing()) _remoteMapStroke.StopLineLocal();
		_remoteMapStroke = null;
	}
	private bool ExecuteMapInput(NMapScreen map, string payload)
	{
		var parts = payload.Split(',');
		if (parts.Length != 3 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.IsFinite(x) || !float.IsFinite(y) || !map.GetViewportRect().HasPoint(new Vector2(x, y))) return false;
		var drawings = map.Drawings;
		Vector2 local = drawings.GetGlobalTransform().AffineInverse() * new Vector2(x, y);
		switch (parts[0])
		{
			case "draw": case "erase": case "pen":
				if (_remoteMapStroke != null || drawings.IsLocalDrawing()) return false;
				DrawingMode mode = parts[0] == "draw" ? DrawingMode.Drawing : parts[0] == "erase" ? DrawingMode.Erasing : drawings.GetLocalDrawingMode(false);
				if (mode == DrawingMode.None) return false;
				drawings.BeginLineLocal(local, mode); _remoteMapStroke = drawings; return true;
			case "move":
				if (_remoteMapStroke != drawings || !drawings.IsLocalDrawing()) return false;
				drawings.UpdateCurrentLinePositionLocal(local); return true;
			case "end": StopRemoteMapStroke(); return true;
			case "cancel":
				StopRemoteMapStroke();
				if (CurrentMapInput is { } input && Ready(input)) input.StopDrawing();
				return true;
			default: return false;
		}
	}
}

internal sealed partial class SpectatorView
{
	private bool _mapStroke;
	private MouseButton _mapStrokeButton;
	private string _mapStrokeAction = "";
	private Vector2 _mapLastPoint;
	private static string MapPayload(string phase, Vector2 point) => phase + "," + point.X.ToString(CultureInfo.InvariantCulture) + "," + point.Y.ToString(CultureInfo.InvariantCulture);
	private void EndMapStroke()
	{
		if (!_mapStroke) return;
		SubmitControl(_mapStrokeAction, MapPayload("end", _mapLastPoint)); _mapStroke = false;
	}
	private bool HandleMapInput(InputEvent input)
	{
		if (!CanControlPage || _snapshot?.Page != "map") { EndMapStroke(); return false; }
		var action = _snapshot.Control.Actions.FirstOrDefault(a => a.Kind == "mapInput" && a.Enabled);
		if (action == null) return false;
		if (input is InputEventMouseMotion motion && _mapStroke)
		{
			_mapLastPoint = motion.Position / _page.Scale;
			if (!new Rect2(Vector2.Zero, _page.Size).HasPoint(_mapLastPoint)) { _mapLastPoint = _mapLastPoint.Clamp(Vector2.Zero, _page.Size - Vector2.One); EndMapStroke(); }
			else SubmitControl(action.Id, MapPayload("move", _mapLastPoint));
			_viewport.SetInputAsHandled(); return true;
		}
		if (input is not InputEventMouseButton click) return false;
		if (_mapStroke && !click.Pressed && click.ButtonIndex == _mapStrokeButton) { EndMapStroke(); _viewport.SetInputAsHandled(); return true; }
		if (!click.Pressed) return false;
		string phase;
		if (click.ButtonIndex is MouseButton.Right or MouseButton.Middle)
		{
			if (_snapshot.MapDrawingMode != 0) { EndMapStroke(); SubmitControl(action.Id, MapPayload("cancel", click.Position / _page.Scale)); _viewport.SetInputAsHandled(); return true; }
			phase = click.ButtonIndex == MouseButton.Right ? "draw" : "erase";
		}
		else if (click.ButtonIndex == MouseButton.Left && _snapshot.MapDrawingMode != 0)
		{
			// Tool buttons remain clickable while a drawing mode is selected.
			if (_snapshot.Control.Actions.Any(a => a.Kind == "native" && a.Enabled && Rectangle(a.Rect).HasPoint(click.Position / _page.Scale))) return false;
			phase = "pen";
		}
		else return false;
		EndMapStroke(); _mapLastPoint = click.Position / _page.Scale;
		SubmitControl(action.Id, MapPayload(phase, _mapLastPoint));
		if (_lastControlResult?.Accepted == true) { _mapStroke = true; _mapStrokeAction = action.Id; _mapStrokeButton = click.ButtonIndex; }
		_viewport.SetInputAsHandled(); return true;
	}
}
