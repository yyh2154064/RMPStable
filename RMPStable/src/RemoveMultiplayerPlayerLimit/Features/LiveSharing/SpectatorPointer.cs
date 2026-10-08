using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class LocalSpectatorSource
{
	private PointerSnapshot _pointer = new();
	private static readonly FieldInfo? CursorImage = typeof(NCursorManager).GetField("_lastSetCursor", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? InspectCursorImage = typeof(NCursorManager).GetField("_cursorInspect", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly PropertyInfo? CursorHotspot = typeof(NCursorManager).GetProperty("HotSpot", BindingFlags.Instance | BindingFlags.NonPublic);
	internal PointerSnapshot CapturePointer(Rect2 spectatorBounds)
	{
		var viewport = NRun.Instance.GetViewport();
		var size = viewport.GetVisibleRect().Size;
		var point = viewport.GetMousePosition();
		string session = NRun.Instance.GetInstanceId().ToString();
		string source = _previewSourceId.Length > 0 ? _previewSourceId : _participant?.Id ?? "";
		bool visible = viewport.GetVisibleRect().HasPoint(point) && !spectatorBounds.HasPoint(point) && Input.MouseMode is not Input.MouseModeEnum.Hidden and not Input.MouseModeEnum.ConfinedHidden;
		float x = visible ? point.X / Math.Max(1, size.X) : 0, y = visible ? point.Y / Math.Max(1, size.Y) : 0;
		var manager = NGame.Instance.CursorManager;
		bool inspect = Input.GetCurrentCursorShape() == Input.CursorShape.Help;
		string texture = Path((inspect ? InspectCursorImage : CursorImage)?.GetValue(manager) as Image);
		var hotspot = inspect ? new Vector2(12, 12) : CursorHotspot?.GetValue(manager) is Vector2 native ? native : new Vector2(14, 5);
		var windowSize = DisplayServer.WindowGetSize();
		var scale = size / new Vector2(Math.Max(1, windowSize.X), Math.Max(1, windowSize.Y));
		if (_pointer.Session == session && _pointer.SourceId == source && _pointer.Visible == visible && _pointer.Texture == texture && _pointer.HotspotX == hotspot.X && _pointer.HotspotY == hotspot.Y && _pointer.ScaleX == scale.X && _pointer.ScaleY == scale.Y && Math.Abs(_pointer.X - x) < 0.0005f && Math.Abs(_pointer.Y - y) < 0.0005f) return _pointer;
		return _pointer = new PointerSnapshot { Session = session, SourceId = source, Sequence = _pointer.Sequence + 1, Visible = visible, X = x, Y = y, Texture = texture, HotspotX = hotspot.X, HotspotY = hotspot.Y, ScaleX = scale.X, ScaleY = scale.Y };
	}
}
