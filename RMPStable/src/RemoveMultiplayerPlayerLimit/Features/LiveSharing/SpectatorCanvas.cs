using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private sealed record ArtNode(string Key, Node2D Holder, CanvasItem Drawing, ArtSnapshot Initial)
	{ internal ArtSnapshot Snapshot = Initial; }
	private sealed record ArtLayer(Node2D Root, Dictionary<string, ArtNode> Index, List<ArtSnapshot> Snapshot);
	private readonly Dictionary<string, Resource> _assets = new();
	private readonly Dictionary<ulong, ArtLayer> _retainedArt = new();
	private readonly List<(Node2D Holder, SubViewport Viewport, TextureRect Surface, List<ArtSnapshot>? Lines)> _drawingSurfaces = new();
	private readonly List<Node> _mountedCards = new(), _mountedLabels = new();
	private CanvasItem? PageAnchor(string key) => _retainedArt.TryGetValue(_screenArt.GetInstanceId(), out var art) && art.Index.TryGetValue(key, out var node) ? node.Drawing : null;
	private static void ClearMounted(List<Node> nodes)
	{
		foreach (var node in nodes) if (GodotObject.IsInstanceValid(node)) { node.GetParent()?.RemoveChild(node); node.QueueFree(); }
		nodes.Clear();
	}

	private static Material? DrawingMaterial(CanvasItem drawing) => drawing.GetClass() == "SpineSprite"
        ? new MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite(drawing).GetNormalMaterial() : drawing.Material;
    private static void SetDrawingMaterial(CanvasItem drawing, Material? material)
    {
        if (drawing.GetClass() == "SpineSprite") new MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite(drawing).SetNormalMaterial(material);
        else drawing.Material = material;
    }
	private Material? SnapshotMaterial(ArtSnapshot art)
	{
		if (Asset<Shader>(art.Shader) is not { } shader) return Asset<Material>(art.Material);
		// Per-node copy: mutating a cached material would recolor the real game.
		var material = new ShaderMaterial { Shader = shader };
		ApplyUniforms(material, art.ShaderValues); return material;
	}
	private void ApplyUniforms(ShaderMaterial material, List<ShaderValueSnapshot> uniforms)
	{
		foreach (var uniform in uniforms)
		{
			var v = uniform.Values;
			Variant value;
			switch (uniform.Kind)
			{
				case "Bool": value = v[0] != 0; break;
				case "Int": value = (int)v[0]; break;
				case "Float": value = v[0]; break;
				case "Color": value = ColorOf(v); break;
				case "Vector2": value = new Vector2(v[0], v[1]); break;
				case "Vector3": value = new Vector3(v[0], v[1], v[2]); break;
				case "Vector4": value = new Vector4(v[0], v[1], v[2], v[3]); break;
				case "Object": value = Asset<Texture2D>(uniform.Texture); break;
				default: continue;
			}
			material.SetShaderParameter(uniform.Name, value);
		}
	}
	private bool RetainArt(Node parent, List<ArtSnapshot> art)
	{
		ulong id = parent.GetInstanceId();
		if (!_retainedArt.TryGetValue(id, out var retained) || !GodotObject.IsInstanceValid(retained.Root) || !retained.Root.IsInsideTree())
		{
			var root = new Node2D();
			var created = DrawArt(parent, art, root);
			_retainedArt[id] = new ArtLayer(root, created.ToDictionary(n => n.Key), art); return true;
		}
		if (!art.Any(a => a.Particle != null) && SnapshotEquality.List(retained.Snapshot, art, SnapshotEquality.Equal)) return false;
		bool structureChanged = retained.Snapshot.Count != art.Count;
		for (int i = 0; !structureChanged && i < art.Count; i++) structureChanged = !SameArtStructure(retained.Snapshot[i], art[i]);
		if (structureChanged)
		{
			// Map scroll only creates newly visible nodes. Retain unchanged nodes
			// and reparent survivors before deleting obsolete ancestor subtrees.
			var updated = DrawArt(parent, art, retained.Root, retained.Index);
			var index = updated.ToDictionary(n => n.Key);
			foreach (var old in retained.Index.Values)
				if ((!index.TryGetValue(old.Key, out var next) || !ReferenceEquals(old, next)) && GodotObject.IsInstanceValid(old.Holder))
				{ old.Holder.GetParent()?.RemoveChild(old.Holder); old.Holder.QueueFree(); }
			_retainedArt[id] = new ArtLayer(retained.Root, index, art); return true;
		}
		var matrices = new Dictionary<string, Transform2D>();
		foreach (var a in art)
		{
			if (!retained.Index.TryGetValue(a.Key, out var node)) continue;
			var matrix = Matrix(a.Transform);
			var ancestor = matrices.TryGetValue(a.Parent, out var previous) ? previous : Transform2D.Identity;
			UpdateArtNode(node, a, ancestor.AffineInverse() * matrix);
			matrices[a.Key] = matrix * new Transform2D(0, new Vector2(a.Rect[0], a.Rect[1]));
		}
		_retainedArt[id] = retained with { Snapshot = art };
		return false;
	}
	private static bool SameArtStructure(ArtSnapshot a, ArtSnapshot b) => a.Key == b.Key && a.Parent == b.Parent && a.Group == b.Group && a.Solid == b.Solid && a.Polygon == b.Polygon && (a.Points != null) == (b.Points != null) && a.Skeleton == b.Skeleton && a.AttachmentClass == b.AttachmentClass && a.AttachmentName == b.AttachmentName && a.Stretch == b.Stretch && a.FlipH == b.FlipH && a.FlipV == b.FlipV && a.BeginCap == b.BeginCap && a.EndCap == b.EndCap && a.Joint == b.Joint && a.Antialiased == b.Antialiased && SnapshotEquality.Array(a.PatchMargins, b.PatchMargins);
	private void UpdateArtNode(ArtNode node, ArtSnapshot a, Transform2D local)
	{
		var p = node.Snapshot;
		if (a.Particle != null) UpdateParticle(node.Drawing, a.Particle);
		if (a.Skeleton.Length > 0 && node.Drawing.GetClass() == "SpineSprite")
		{
			var state = new MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite(node.Drawing).GetAnimationState();
			var track = state.GetCurrent(0);
			if (p.Animation != a.Animation && a.Animation.Length > 0) { state.SetAnimation(a.Animation, a.AnimationLoop); track = state.GetCurrent(0); }
			if (track != null) { track.SetLoop(a.AnimationLoop); if (Math.Abs(track.GetTrackTime() - a.AnimationTime) > 0.15f) track.SetTrackTime(a.AnimationTime); }
		}
		if (p.Texture != a.Texture || !SnapshotEquality.Array(p.Region, a.Region))
		{
			Texture2D? texture = Asset<Texture2D>(a.Texture);
			if (a.Region is { Length: 4 } region && texture != null) texture = new AtlasTexture { Atlas = texture, Region = Rectangle(region) };
			if (node.Drawing is TextureRect image) image.Texture = texture;
			else if (node.Drawing is NinePatchRect patch) patch.Texture = texture;
			else if (node.Drawing is Polygon2D imagePolygon) imagePolygon.Texture = texture;
		}
		if (p.Material != a.Material || p.Shader != a.Shader) SetDrawingMaterial(node.Drawing, SnapshotMaterial(a));
		if (node.Holder.Transform != local) node.Holder.Transform = local;
		if (!SnapshotEquality.Array(p.Tint, a.Tint)) node.Holder.Modulate = ColorOf(a.Tint);
		if (p.Z != a.Z) node.Holder.ZIndex = a.Z;
		if (p.ZRelative != a.ZRelative) node.Holder.ZAsRelative = a.ZRelative;
		if (p.BehindParent != a.BehindParent) node.Holder.ShowBehindParent = a.BehindParent;
		if (!SnapshotEquality.Array(p.SelfTint, a.SelfTint)) node.Drawing.SelfModulate = ColorOf(a.SelfTint);
		if (p.ClipChildren != a.ClipChildren) node.Drawing.ClipChildren = (CanvasItem.ClipChildrenMode)a.ClipChildren;
		if (node.Drawing is Control control)
		{
			if (!SnapshotEquality.Array(p.Rect, a.Rect)) { control.Position = new Vector2(a.Rect[0], a.Rect[1]); control.Size = new Vector2(a.Rect[2], a.Rect[3]); }
			if (p.ClipContents != a.ClipContents) control.ClipContents = a.ClipContents;
		}
		if (DrawingMaterial(node.Drawing) is ShaderMaterial shader && !SnapshotEquality.List(p.ShaderValues, a.ShaderValues, SnapshotEquality.Equal)) ApplyUniforms(shader, a.ShaderValues);
		if (node.Drawing is Line2D line)
		{
			if (a.Points != null && !SnapshotEquality.Array(p.Points, a.Points)) line.Points = Points(a.Points);
			if (p.LineWidth != a.LineWidth) line.Width = a.LineWidth;
		}
		if (node.Drawing is Polygon2D polygon && a.Points != null && !SnapshotEquality.Array(p.Points, a.Points)) polygon.Polygon = Points(a.Points);
		node.Snapshot = a;
	}
	private static Vector2[] Points(float[] points) => Enumerable.Range(0, points.Length / 2).Select(i => new Vector2(points[i * 2], points[i * 2 + 1])).ToArray();
	private void UpdateDrawings(List<DrawingSnapshot> drawings)
	{
		if (_drawingSurfaces.Count != drawings.Count)
		{
			foreach (var old in _drawingSurfaces) _retainedArt.Remove(old.Viewport.GetInstanceId());
			Clear(_screenDrawings); _drawingSurfaces.Clear();
			foreach (var drawing in drawings)
			{
				var holder = new Node2D(); _screenDrawings.AddChild(holder);
				var viewport = new SubViewport { TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled }; holder.AddChild(viewport);
				var surface = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Texture = viewport.GetTexture(), MouseFilter = Control.MouseFilterEnum.Ignore }; holder.AddChild(surface);
				_drawingSurfaces.Add((holder, viewport, surface, null));
			}
		}
		for (int i = 0; i < drawings.Count; i++)
		{
			var drawing = drawings[i]; var retained = _drawingSurfaces[i];
			retained.Holder.Transform = Matrix(drawing.Transform); retained.Surface.Size = new Vector2(drawing.Size[0], drawing.Size[1]);
			retained.Viewport.Size = new Vector2I(drawing.ViewportSize[0], drawing.ViewportSize[1]);
			if (SnapshotEquality.List(drawing.Lines, retained.Lines, SnapshotEquality.Equal)) continue;
			RetainArt(retained.Viewport, drawing.Lines); retained.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			_drawingSurfaces[i] = (retained.Holder, retained.Viewport, retained.Surface, drawing.Lines);
		}
	}
	private MapMotionFrame? _latestMapMotion;
    internal void UpdateMapDrawings(MapMotionFrame frame)
    {
        if (_snapshot?.Page != "map" || _snapshot.Session != frame.Session || _snapshot.SourceId != frame.SourceId) return;
        _latestMapMotion = frame; ApplyMapMotion();
    }
    private void ApplyMapMotion()
    {
        var frame = _latestMapMotion;
        if (frame == null || _snapshot?.Page != "map" || _snapshot.Session != frame.Session || _snapshot.SourceId != frame.SourceId ||
            !_retainedArt.TryGetValue(_screenArt.GetInstanceId(), out var layer) || !layer.Index.TryGetValue(frame.Key, out var node)) return;
        var parent = layer.Index.TryGetValue(node.Snapshot.Parent, out var ancestor) ? Matrix(ancestor.Snapshot.Transform) * new Transform2D(0, new Vector2(ancestor.Snapshot.Rect[0], ancestor.Snapshot.Rect[1])) : Transform2D.Identity;
        node.Holder.Transform = parent.AffineInverse() * Matrix(frame.Transform);
        UpdateDrawings(frame.Drawings);
        var old = Matrix(node.Snapshot.Transform);
        var delta = Matrix(frame.Transform) * old.AffineInverse();
        foreach (var action in _snapshot.Control.Actions.Where(a => a.MapAttached))
            if (_actionButtons.TryGetValue(action.Id, out var button)) button.Position = (delta * Rectangle(action.Rect).Position) * _page.Scale;
    }
	private HoverSnapshot? _activeScreenHover;
	private void ProcessScreenHover(Vector2 point)
	{
		// Hit-test snapshot bounds directly: command buttons sit above the page
		// in control mode, so their GUI signals cannot be the sole hover source.
		var hover = _snapshot != null && !_showDeck && BrowsePile.Length == 0 &&
			_inspectIndex < 0 && _relicIndex < 0 && _dragCard == null && !HasCardPreview && _snapshot.ModalArt.Count == 0
			? _snapshot.Hovers.LastOrDefault(h => h.Tips.Count > 0 && Rectangle(h.Rect).HasPoint(point / _page.Scale)) : null;
		if (SnapshotEquality.Equal(hover, _activeScreenHover) && (hover == null || _preview.GetChildCount() > 0))
		{ _activeScreenHover = hover; return; }
		bool hadHover = _activeScreenHover != null; _activeScreenHover = hover;
		if (hover == null) { if (hadHover && !HasCardPreview) Clear(_preview); return; }
		Clear(_preview);
		var rect = Rectangle(hover.Rect); rect.Position *= _page.Scale; rect.Size *= _page.Scale;
		float y = Math.Clamp(rect.Position.Y, 80, 600);
		// Native left-aligned reward tips put text on the left and card previews
		// on the right; both can occur in the same linked reward.
		var text = hover.Tips.Where(t => t.Card == null).ToList();
		var cards = hover.Tips.Where(t => t.Card != null).ToList();
		if (text.Count > 0) DrawTips(_preview, text, new Vector2(Math.Clamp(rect.Position.X - 380, 20, 1540), y));
		if (cards.Count > 0) DrawTips(_preview, cards, new Vector2(Math.Clamp(rect.End.X, 20, 1600), y));
	}
}
