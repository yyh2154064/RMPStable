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

internal sealed partial class SpectatorView
{
	private Node2D _remotePointer = null!;
	private TextureRect _pointerImage = null!;
	private readonly Dictionary<string, Texture2D?> _pointerTextures = new();
	private PointerSnapshot _pointerState = new();
	private Vector2 _pointerTarget;
	internal Rect2 PointerExclusion => _panel.GetGlobalRect();
	private void CreatePointer()
	{
		_remotePointer = new Node2D { Name = "SpectatorSourcePointer", Visible = false, ZIndex = 4096, ZAsRelative = false };
		_canvas.AddChild(_remotePointer);
		_pointerImage = new TextureRect { Name = "NativeCursor", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
		_remotePointer.AddChild(_pointerImage);
	}
	internal void UpdatePointer(PointerSnapshot pointer)
	{
		if (_snapshot == null || pointer.Session != _snapshot.Session || pointer.SourceId != _snapshot.SourceId) { _remotePointer.Hide(); return; }
		bool changedSource = pointer.Session != _pointerState.Session || pointer.SourceId != _pointerState.SourceId;
		if (!changedSource && pointer.Sequence < _pointerState.Sequence) return;
		bool snap = changedSource || !_remotePointer.Visible;
		_pointerState = pointer;
		if (!pointer.Visible || !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y) || pointer.X is < 0 or > 1 || pointer.Y is < 0 or > 1) { _remotePointer.Hide(); return; }
		if (!_pointerTextures.TryGetValue(pointer.Texture, out var texture))
		{
			var resource = Asset<Resource>(pointer.Texture);
			texture = resource is Image image ? ImageTexture.CreateFromImage(image) : resource as Texture2D;
			_pointerTextures[pointer.Texture] = texture;
		}
		_pointerImage.Texture = texture;
		var scale = new Vector2(pointer.ScaleX, pointer.ScaleY);
		_pointerImage.Position = -new Vector2(pointer.HotspotX, pointer.HotspotY) * scale;
		_pointerImage.Size = (texture?.GetSize() ?? Vector2.Zero) * scale;
		_pointerTarget = new Vector2(pointer.X * 1920, pointer.Y * 1080);
		if (snap || _remotePointer.Position.DistanceTo(_pointerTarget) > 500) _remotePointer.Position = _pointerTarget;
	}
	private void ProcessPointerMotion()
	{
		if (_remotePointer == null) return;
		bool valid = _snapshot != null && _pointerState.Session == _snapshot.Session && _pointerState.SourceId == _snapshot.SourceId && _pointerState.Visible && float.IsFinite(_pointerState.X) && float.IsFinite(_pointerState.Y) && _pointerState.X is >= 0 and <= 1 && _pointerState.Y is >= 0 and <= 1;
		_remotePointer.Visible = valid && _pointerImage.Texture != null && !_showDeck && BrowsePile.Length == 0 && _inspectIndex < 0 && _relicIndex < 0;
		if (_remotePointer.Visible) _remotePointer.Position = _remotePointer.Position.Lerp(_pointerTarget, 1 - MathF.Exp(-(float)_panel.GetProcessDeltaTime() * 30));
	}
}
