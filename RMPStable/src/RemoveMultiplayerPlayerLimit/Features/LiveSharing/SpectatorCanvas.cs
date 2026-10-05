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
		if (SnapshotEquality.List(retained.Snapshot, art, SnapshotEquality.Equal)) return false;
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
	private static bool SameArtStructure(ArtSnapshot a, ArtSnapshot b) => a.Key == b.Key && a.Parent == b.Parent && a.Group == b.Group && a.Solid == b.Solid && a.Polygon == b.Polygon && (a.Points != null) == (b.Points != null) && a.Skeleton == b.Skeleton && a.Animation == b.Animation && a.Stretch == b.Stretch && a.FlipH == b.FlipH && a.FlipV == b.FlipV && a.BeginCap == b.BeginCap && a.EndCap == b.EndCap && a.Joint == b.Joint && a.Antialiased == b.Antialiased && SnapshotEquality.Array(a.PatchMargins, b.PatchMargins);
	private void UpdateArtNode(ArtNode node, ArtSnapshot a, Transform2D local)
	{
		var p = node.Snapshot;
		if (p.Texture != a.Texture || !SnapshotEquality.Array(p.Region, a.Region))
		{
			Texture2D? texture = Asset<Texture2D>(a.Texture);
			if (a.Region is { Length: 4 } region && texture != null) texture = new AtlasTexture { Atlas = texture, Region = Rectangle(region) };
			if (node.Drawing is TextureRect image) image.Texture = texture;
			else if (node.Drawing is NinePatchRect patch) patch.Texture = texture;
			else if (node.Drawing is Polygon2D imagePolygon) imagePolygon.Texture = texture;
		}
		if (p.Material != a.Material || p.Shader != a.Shader) node.Drawing.Material = SnapshotMaterial(a);
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
		if (node.Drawing.Material is ShaderMaterial shader && !SnapshotEquality.List(p.ShaderValues, a.ShaderValues, SnapshotEquality.Equal)) ApplyUniforms(shader, a.ShaderValues);
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
	internal void UpdateMapDrawings(List<DrawingSnapshot> drawings)
	{ if (_snapshot?.Page == "map") UpdateDrawings(drawings); }
	private void DrawHovers(List<HoverSnapshot> hovers)
	{
		foreach (var hover in hovers.Where(h => h.Tips.Count > 0))
		{
			var hitbox = Area(_screenHovers, Rectangle(hover.Rect).Position, Rectangle(hover.Rect).Size); hitbox.MouseFilter = Control.MouseFilterEnum.Pass;
			hitbox.MouseEntered += () => { if (_inspectIndex >= 0) return; Clear(_preview); var rect = hitbox.GetGlobalRect(); DrawTips(_preview, hover.Tips, new Vector2(Math.Max(20, rect.Position.X - 380), Math.Clamp(rect.Position.Y, 80, 600))); };
			hitbox.MouseExited += () => { if (_inspectIndex < 0) Clear(_preview); };
		}
	}
}
