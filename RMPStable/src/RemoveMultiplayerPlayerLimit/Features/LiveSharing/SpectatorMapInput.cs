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
