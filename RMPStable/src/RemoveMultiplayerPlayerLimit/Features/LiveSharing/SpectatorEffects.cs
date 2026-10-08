using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Draw data only. Never instantiate a native VFX scene: its Ready callbacks can
// shake the source screen, play sound and start gameplay-owned timers.
internal sealed class EffectFrame
{
	public string Session { get; set; } = "";
	public string SourceId { get; set; } = "";
	public List<ArtSnapshot> Back { get; set; } = new();
	public List<ArtSnapshot> Front { get; set; } = new();
	public List<TextSnapshot> BackLabels { get; set; } = new();
	public List<TextSnapshot> FrontLabels { get; set; } = new();
}
internal sealed class ParticleSnapshot
{
	public string Class { get; set; } = "";
	public long Revision { get; set; }
	public bool Emitting { get; set; }
	public float Speed { get; set; } = 1;
	public float AmountRatio { get; set; } = 1;
	public List<RenderProperty> Properties { get; set; } = new();
	public List<RenderProperty> ProcessValues { get; set; } = new();
}
internal sealed class RenderProperty
{
	public string Name { get; set; } = "";
	public RenderValue Value { get; set; } = new();
}
internal sealed class RenderValue
{
	public string Kind { get; set; } = "Nil";
	public string Text { get; set; } = "";
	public long Integer { get; set; }
	public double[] Numbers { get; set; } = Array.Empty<double>();
	public List<RenderValue> Items { get; set; } = new();
	public RenderResource? Resource { get; set; }
}
internal sealed class RenderResource
{
	public string Class { get; set; } = "";
	public string Path { get; set; } = "";
	public List<RenderProperty> Properties { get; set; } = new();
}
internal static class EffectValues
{
	// Native rendering resources only; scripts, audio, game models and arbitrary
	// classes are deliberately not part of this wire format.
	private static readonly HashSet<string> ResourceClasses = new(StringComparer.Ordinal)
	{ "ParticleProcessMaterial", "ShaderMaterial", "CanvasItemMaterial", "Gradient", "GradientTexture1D", "GradientTexture2D", "Curve", "CurveTexture", "CurveXYZTexture", "NoiseTexture2D", "FastNoiseLite", "AtlasTexture" };
	private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
	{ "script", "resource_name", "resource_local_to_scene", "name", "unique_name_in_owner", "owner", "process_mode", "process_priority", "process_physics_priority", "process_thread_group", "process_thread_group_order", "process_thread_messages", "visible", "modulate", "self_modulate", "show_behind_parent", "top_level", "light_mask", "visibility_layer", "z_index", "z_as_relative", "y_sort_enabled", "texture_filter", "texture_repeat", "material", "use_parent_material", "position", "rotation", "scale", "skew", "transform", "emitting" };
	internal static List<RenderProperty> CaptureProperties(GodotObject obj, int depth = 0)
	{
		var result = new List<RenderProperty>();
		foreach (var property in obj.GetPropertyList())
		{
			string name = property["name"].AsString();
			if (((PropertyUsageFlags)property["usage"].AsInt64() & PropertyUsageFlags.Storage) == 0 || Excluded.Contains(name) || name.StartsWith("metadata/")) continue;
			if (Capture(obj.Get(name), depth) is { } value) result.Add(new() { Name = name, Value = value });
		}
		return result;
	}
	private static RenderValue? Capture(Variant v, int depth)
	{
		if (depth > 8) return null;
		var value = new RenderValue { Kind = v.VariantType.ToString() };
		switch (v.VariantType)
		{
			case Variant.Type.Nil: break;
			case Variant.Type.Bool: value.Integer = v.AsBool() ? 1 : 0; break;
			case Variant.Type.Int: value.Integer = v.AsInt64(); break;
			case Variant.Type.Float: value.Numbers = new[] { v.AsDouble() }; break;
			case Variant.Type.String: case Variant.Type.StringName: value.Text = v.AsString(); break;
			case Variant.Type.Vector2: var v2 = v.AsVector2(); value.Numbers = new double[] { v2.X, v2.Y }; break;
			case Variant.Type.Vector3: var v3 = v.AsVector3(); value.Numbers = new double[] { v3.X, v3.Y, v3.Z }; break;
			case Variant.Type.Color: var c = v.AsColor(); value.Numbers = new double[] { c.R, c.G, c.B, c.A }; break;
			case Variant.Type.Rect2: var r = v.AsRect2(); value.Numbers = new double[] { r.Position.X, r.Position.Y, r.Size.X, r.Size.Y }; break;
			case Variant.Type.PackedFloat32Array: value.Numbers = v.As<float[]>().Select(x => (double)x).ToArray(); break;
			case Variant.Type.PackedFloat64Array: value.Numbers = v.As<double[]>(); break;
			case Variant.Type.PackedInt32Array: value.Numbers = v.AsInt32Array().Select(x => (double)x).ToArray(); break;
			case Variant.Type.PackedVector2Array: value.Numbers = v.AsVector2Array().SelectMany(x => new double[] { x.X, x.Y }).ToArray(); break;
			case Variant.Type.PackedVector3Array: value.Numbers = v.AsVector3Array().SelectMany(x => new double[] { x.X, x.Y, x.Z }).ToArray(); break;
			case Variant.Type.PackedColorArray: value.Numbers = v.AsColorArray().SelectMany(x => new double[] { x.R, x.G, x.B, x.A }).ToArray(); break;
			case Variant.Type.Array:
				foreach (var entry in v.AsGodotArray()) { var item = Capture(entry, depth + 1); if (item == null) return null; value.Items.Add(item); } break;
			case Variant.Type.Object:
				if (v.AsGodotObject() is not Resource resource) return null;
				string type = resource.GetClass();
				// Textures and shader programs are immutable game assets. Dynamic
				// process materials/gradients are reconstructed as private resources.
				if (resource is Texture2D or Shader && resource.ResourcePath.Length > 0)
					value.Resource = new() { Class = type, Path = resource.ResourcePath };
				else if (ResourceClasses.Contains(type)) value.Resource = new() { Class = type, Properties = CaptureProperties(resource, depth + 1) };
				else return null;
				break;
			default: return null;
		}
		return value;
	}
	internal static void Apply(GodotObject obj, List<RenderProperty> properties)
	{ foreach (var property in properties) if (!Excluded.Contains(property.Name)) obj.Set(property.Name, Restore(property.Value)); }
	internal static List<RenderProperty> CaptureScalars(GodotObject obj, string[] names)
		=> names.Select(name => new RenderProperty { Name = name, Value = Capture(obj.Get(name), 0)! }).ToList();
	internal static string[] ScalarNames(List<RenderProperty> properties) => properties.Where(p => p.Value.Kind is "Bool" or "Int" or "Float" or "Color" or "Vector2" or "Vector3").Select(p => p.Name).ToArray();
	internal static bool Equal(RenderValue a, RenderValue b) => a.Kind == b.Kind && a.Integer == b.Integer && a.Text == b.Text && a.Numbers.SequenceEqual(b.Numbers);
	private static Variant Restore(RenderValue value)
	{
		var n = value.Numbers;
		switch (value.Kind)
		{
			case "Bool": return value.Integer != 0;
			case "Int": return value.Integer;
			case "Float": return n[0];
			case "String": return value.Text;
			case "StringName": return new StringName(value.Text);
			case "Vector2": return new Vector2((float)n[0], (float)n[1]);
			case "Vector3": return new Vector3((float)n[0], (float)n[1], (float)n[2]);
			case "Color": return new Color((float)n[0], (float)n[1], (float)n[2], (float)n[3]);
			case "Rect2": return new Rect2((float)n[0], (float)n[1], (float)n[2], (float)n[3]);
			case "PackedFloat32Array": return n.Select(x => (float)x).ToArray();
			case "PackedFloat64Array": return n;
			case "PackedInt32Array": return n.Select(x => (int)x).ToArray();
			case "PackedVector2Array": return Enumerable.Range(0, n.Length / 2).Select(i => new Vector2((float)n[i * 2], (float)n[i * 2 + 1])).ToArray();
			case "PackedVector3Array": return Enumerable.Range(0, n.Length / 3).Select(i => new Vector3((float)n[i * 3], (float)n[i * 3 + 1], (float)n[i * 3 + 2])).ToArray();
			case "PackedColorArray": return Enumerable.Range(0, n.Length / 4).Select(i => new Color((float)n[i * 4], (float)n[i * 4 + 1], (float)n[i * 4 + 2], (float)n[i * 4 + 3])).ToArray();
			case "Array": var array = new Godot.Collections.Array(); foreach (var item in value.Items) array.Add(Restore(item)); return array;
			case "Object":
				if (value.Resource is not { } data) return default;
				if (data.Path.StartsWith("res://") && ResourceLoader.Load(data.Path) is Resource asset && asset is Texture2D or Shader) return asset;
				if (!ResourceClasses.Contains(data.Class)) return default;
				var resource = ClassDB.Instantiate(data.Class).As<Resource>(); Apply(resource, data.Properties); return resource;
			default: return default;
		}
	}
}
internal sealed partial class LocalSpectatorSource
{
	private sealed class ParticleCache
	{
		internal List<RenderProperty> Properties = new();
		internal List<(Resource Resource, Action Changed)> Resources = new();
		internal ulong Material;
		internal string[] ScalarNames = Array.Empty<string>();
		internal bool Dirty = true;
		internal long Revision;
		internal void Disconnect() { foreach (var r in Resources) if (GodotObject.IsInstanceValid(r.Resource)) r.Resource.Changed -= r.Changed; Resources.Clear(); }
	}
	private readonly Dictionary<ulong, ParticleCache> _particles = new();
	private long _particleRevision;
	private readonly Dictionary<ulong, List<RenderProperty>> _particleSamples = new();
	private void ClearParticles() { foreach (var cache in _particles.Values) cache.Disconnect(); _particles.Clear(); }
	private ParticleSnapshot CaptureParticle(CanvasItem node)
	{
		ulong id = node.GetInstanceId();
		if (!_particles.TryGetValue(id, out var cache)) _particles[id] = cache = new();
		var material = node is GpuParticles2D gpu ? gpu.ProcessMaterial : null;
		ulong materialId = material?.GetInstanceId() ?? 0;
		if (cache.Dirty || cache.Material != materialId)
		{
			cache.Disconnect(); cache.Properties = EffectValues.CaptureProperties(node); cache.Material = materialId; cache.Dirty = false; cache.Revision = ++_particleRevision;
			cache.ScalarNames = material == null ? EffectValues.ScalarNames(cache.Properties) : EffectValues.ScalarNames(EffectValues.CaptureProperties(material));
			// Materials used for recolored projectiles are often pathless copies.
			// Subscribe once, rather than serializing every property at 60 Hz.
			if (material != null) { Action changed = () => cache.Dirty = true; material.Changed += changed; cache.Resources.Add((material, changed)); }
		}
		ulong sampleId = materialId == 0 ? id : materialId;
		if (!_particleSamples.TryGetValue(sampleId, out var values)) _particleSamples[sampleId] = values = EffectValues.CaptureScalars(material ?? (GodotObject)node, cache.ScalarNames);
		return new() { Class = node.GetClass(), Revision = cache.Revision, Properties = cache.Properties, ProcessValues = values, Emitting = node.Get("emitting").AsBool(), Speed = node.Get("speed_scale").AsSingle(), AmountRatio = node is GpuParticles2D emitter ? emitter.AmountRatio : 1 };
	}
	internal EffectFrame CaptureEffects()
	{
		var frame = new EffectFrame { Session = NRun.Instance.GetInstanceId().ToString(), SourceId = _previewSourceId.Length > 0 ? _previewSourceId : _participant?.Id ?? "" };
		_particleSamples.Clear();
		if (NCombatRoom.Instance is { } room && Ready(room))
		{
			// Keep the shader sampling cache separate from an in-flight full capture.
			bool previous = _drawingCapture; _drawingCapture = true; _drawingSamples.Clear();
			try
			{
				foreach (var step in CaptureArtSteps(room.BackCombatVfxContainer, frame.Back, includeParticles: true)) { }
				foreach (var step in CaptureArtSteps(room.CombatVfxContainer, frame.Front, includeParticles: true)) { }
				foreach (var step in CaptureLabelSteps(room.BackCombatVfxContainer, frame.BackLabels)) { }
				foreach (var step in CaptureLabelSteps(room.CombatVfxContainer, frame.FrontLabels)) { }
			}
			finally { _drawingCapture = previous; }
		}
		var live = new HashSet<string>(frame.Back.Concat(frame.Front).Where(a => a.Particle != null).Select(a => a.Key));
		foreach (var id in _particles.Keys.Where(id => !live.Contains(id.ToString())).ToArray()) { _particles[id].Disconnect(); _particles.Remove(id); }
		return frame;
	}
}
