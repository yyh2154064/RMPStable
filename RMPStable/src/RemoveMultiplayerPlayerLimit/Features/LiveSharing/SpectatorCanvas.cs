using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private sealed record ArtNode(string Key, Node2D Holder, CanvasItem Drawing);
	private readonly Dictionary<string, Resource> _assets = new();
	private readonly Dictionary<ulong, (string Structure, List<ArtNode> Nodes)> _retainedArt = new();
	private readonly List<(Node2D Holder, SubViewport Viewport, TextureRect Surface, string Lines)> _drawingSurfaces = new();
	private readonly List<Node> _mountedCards = new(), _mountedLabels = new();
	private CanvasItem? PageAnchor(string key) => _retainedArt.TryGetValue(_screenArt.GetInstanceId(), out var art) ? art.Nodes.FirstOrDefault(n => n.Key == key)?.Drawing : null;
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
		// Structural changes rebuild the layer. Animation, scroll, color, shader
		// uniforms and dimensions update existing nodes without reallocating them.
		string structure = JsonSerializer.Serialize(art.Select(a => new { a.Key, a.Parent, a.Group, a.Solid, a.Polygon, Line = a.Points != null, a.Texture, a.Material, a.Shader, a.Skeleton, a.Animation, a.PatchMargins, a.Region }));
		if (!_retainedArt.TryGetValue(id, out var retained) || retained.Structure != structure)
		{
			Clear(parent); _retainedArt[id] = (structure, DrawArt(parent, art)); return true;
		}
		var matrices = new Dictionary<string, Transform2D>();
		var nodes = retained.Nodes.ToDictionary(n => n.Key);
		foreach (var a in art)
		{
			if (!nodes.TryGetValue(a.Key, out var node)) continue;
			var matrix = Matrix(a.Transform);
			var ancestor = matrices.TryGetValue(a.Parent, out var previous) ? previous : Transform2D.Identity;
			node.Holder.Transform = ancestor.AffineInverse() * matrix;
			node.Holder.Modulate = ColorOf(a.Tint); node.Holder.ZIndex = a.Z; node.Holder.ZAsRelative = a.ZRelative;
			node.Holder.ShowBehindParent = a.BehindParent;
			node.Drawing.SelfModulate = ColorOf(a.SelfTint); node.Drawing.ClipChildren = (CanvasItem.ClipChildrenMode)a.ClipChildren;
			if (node.Drawing is Control control) { control.Position = new Vector2(a.Rect[0], a.Rect[1]); control.Size = new Vector2(a.Rect[2], a.Rect[3]); control.ClipContents = a.ClipContents; }
			if (node.Drawing.Material is ShaderMaterial shader) ApplyUniforms(shader, a.ShaderValues);
			if (node.Drawing is Line2D line && a.Points != null) { line.Points = Points(a.Points); line.Width = a.LineWidth; }
			matrices[a.Key] = matrix * new Transform2D(0, new Vector2(a.Rect[0], a.Rect[1]));
		}
		return false;
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
				_drawingSurfaces.Add((holder, viewport, surface, ""));
			}
		}
		for (int i = 0; i < drawings.Count; i++)
		{
			var drawing = drawings[i]; var retained = _drawingSurfaces[i];
			retained.Holder.Transform = Matrix(drawing.Transform); retained.Surface.Size = new Vector2(drawing.Size[0], drawing.Size[1]);
			retained.Viewport.Size = new Vector2I(drawing.ViewportSize[0], drawing.ViewportSize[1]);
			string lines = JsonSerializer.Serialize(drawing.Lines);
			if (lines == retained.Lines) continue;
			RetainArt(retained.Viewport, drawing.Lines); retained.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			_drawingSurfaces[i] = (retained.Holder, retained.Viewport, retained.Surface, lines);
		}
	}
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
