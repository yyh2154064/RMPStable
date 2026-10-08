using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Value-only motion channel, independent of the slower card/HUD snapshot.
internal sealed class AnimationFrame
{
	public string Session { get; set; } = "";
	public string SourceId { get; set; } = "";
	public List<CreatureMotion> Creatures { get; set; } = new();
}
internal sealed class CreatureMotion
{
	public string EntityKey { get; set; } = "";
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] BodyTransform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] Tint { get; set; } = { 1, 1, 1, 1 };
	public List<AnimationTrack> Tracks { get; set; } = new();
}
internal sealed class AnimationTrack
{
	public int Index { get; set; }
	public string Key { get; set; } = "";
	public string Name { get; set; } = "";
	public bool Loop { get; set; }
	public float Time { get; set; }
	public float Speed { get; set; } = 1;
}
internal sealed partial class LocalSpectatorSource
{
	private readonly Dictionary<ulong, (MegaSprite Sprite, Callable Started)> _animationSubscriptions = new();
	private readonly Dictionary<string, long> _animationGenerations = new();
	private long _animationGeneration;
	private static string CaptureSkin(NCreatureVisuals? visuals)
    {
        var skeleton = visuals?.SpineBody?.GetSkeleton()?.BoundObject;
        if (skeleton?.HasMethod("get_skin") != true) return "";
        var skin = skeleton.Call("get_skin").AsGodotObject();
        return skin?.HasMethod("get_name") == true ? skin.Call("get_name").AsString() : "";
    }
    private ArtSnapshot CaptureBodyMaterial(NCreatureVisuals? visuals)
    {
        var material = visuals?.SpineBody?.GetNormalMaterial();
        var result = new ArtSnapshot { Material = Path(material) };
        if (material is ShaderMaterial shader) { result.Shader = Path(shader.Shader); result.ShaderValues = CaptureShaderValues(shader); }
        return result;
    }
	private void UnbindAnimations()
	{
		foreach (var entry in _animationSubscriptions.Values)
			if (GodotObject.IsInstanceValid(entry.Sprite.BoundObject)) entry.Sprite.DisconnectAnimationStarted(entry.Started);
		_animationSubscriptions.Clear(); _animationGenerations.Clear();
	}
	private void WatchAnimation(NCreature node)
	{
		ulong id = node.GetInstanceId();
		if (!node.Visuals.HasSpineAnimation || _animationSubscriptions.ContainsKey(id)) return;
		var sprite = node.Visuals.SpineBody!;
		var callback = Callable.From<GodotObject, GodotObject, GodotObject>((_, __, entry) =>
		{
			int index = entry.HasMethod("get_track_index") ? entry.Call("get_track_index").AsInt32() : 0;
			_animationGenerations[id + ":" + index] = ++_animationGeneration;
		});
		sprite.ConnectAnimationStarted(callback); _animationSubscriptions[id] = (sprite, callback);
	}
	internal AnimationFrame CaptureAnimations()
	{
		var frame = new AnimationFrame { Session = NRun.Instance.GetInstanceId().ToString(), SourceId = _previewSourceId.Length > 0 ? _previewSourceId : _participant?.Id ?? "" };
		if (NCombatRoom.Instance is not { } room) { UnbindAnimations(); return frame; }
		var live = new HashSet<ulong>(room.CreatureNodes.Select(n => n.GetInstanceId()));
		foreach (var id in _animationSubscriptions.Keys.Where(id => !live.Contains(id)).ToArray())
		{
			var old = _animationSubscriptions[id];
			if (GodotObject.IsInstanceValid(old.Sprite.BoundObject)) old.Sprite.DisconnectAnimationStarted(old.Started);
			_animationSubscriptions.Remove(id);
		}
		foreach (var node in room.CreatureNodes)
		{
			if (!Ready(node) || node.Visuals == null) continue;
			WatchAnimation(node);
			var visuals = node.Visuals; var body = visuals.GetCurrentBody();
			var motion = new CreatureMotion { EntityKey = node.GetInstanceId().ToString(), Transform = Transform(visuals.GetGlobalTransform()), BodyTransform = Transform(body.Transform), Tint = new[] { body.Modulate.R, body.Modulate.G, body.Modulate.B, body.Modulate.A } };
			if (visuals.HasSpineAnimation)
			for (int i = 0; i < 4; i++)
			{
				var track = visuals.SpineAnimation.GetCurrentTrack(i);
				if (track == null) continue;
				var native = track.BoundObject;
				string key = node.GetInstanceId() + ":" + i;
				if (!_animationGenerations.TryGetValue(key, out long generation)) _animationGenerations[key] = generation = ++_animationGeneration;
				motion.Tracks.Add(new() { Index = i, Key = generation.ToString(), Name = track.GetAnimationName(), Time = track.GetTrackTime(), Loop = native.HasMethod("get_loop") && native.Call("get_loop").AsBool(), Speed = native.HasMethod("get_time_scale") ? native.Call("get_time_scale").AsSingle() : 1 });
			}
			frame.Creatures.Add(motion);
		}
		return frame;
	}
}
internal sealed partial class SpectatorView
{
	private void ApplyCreatureAppearance(NCreatureVisuals visuals, CreatureSnapshot snapshot)
    {
        var spine = visuals.SpineBody!;
        var skeleton = spine.GetSkeleton();
        if (snapshot.Skin.Length > 0 && skeleton?.GetData().FindSkin(snapshot.Skin) is { } skin)
        { skeleton.SetSkin(skin); skeleton.SetSlotsToSetupPose(); }
        spine.SetNormalMaterial(SnapshotMaterial(snapshot.BodyMaterial));
    }
	private readonly Dictionary<string, string> _motionTrackKeys = new();
	private AnimationFrame? _latestMotion;
	internal void UpdateAnimations(AnimationFrame frame)
	{
		if (_snapshot == null || _snapshot.Session != frame.Session || _snapshot.SourceId != frame.SourceId) return;
		_latestMotion = frame;
		var live = new HashSet<string>();
		foreach (var motion in frame.Creatures)
		{
			if (!_creatureSprites.TryGetValue(motion.EntityKey, out var art)) continue;
			var visuals = art.Holder.GetChildOrNull<Node2D>(0)?.GetChildOrNull<NCreatureVisuals>(0);
			if (visuals == null) continue;
			art.Holder.Transform = Matrix(motion.Transform);
			var body = visuals.GetCurrentBody(); body.Transform = Matrix(motion.BodyTransform); body.Modulate = ColorOf(motion.Tint);
			foreach (var track in motion.Tracks)
			{
				string id = motion.EntityKey + ":" + track.Index; live.Add(id);
				if (!visuals.HasSpineAnimation || !visuals.SpineBody!.HasAnimation(track.Name)) continue;
				string key = visuals.GetInstanceId() + ":" + track.Key + ":" + track.Name;
				var current = visuals.SpineAnimation.GetCurrentTrack(track.Index);
				if (!_motionTrackKeys.TryGetValue(id, out string old) || old != key)
				{
					visuals.SpineAnimation.SetAnimation(track.Name, track.Loop, track.Index);
					current = visuals.SpineAnimation.GetCurrentTrack(track.Index);
					current?.SetMixDuration(0.08f); current?.SetTrackTime(track.Time); _motionTrackKeys[id] = key;
				}
				else if (current != null && Math.Abs(current.GetTrackTime() - track.Time) > 0.12f) current.SetTrackTime(track.Time);
				current?.SetLoop(track.Loop); current?.SetTimeScale(track.Speed);
			}
		}
		foreach (var id in _motionTrackKeys.Keys.Where(k => !live.Contains(k)).ToArray())
		{
			int separator = id.LastIndexOf(':');
			if (_creatureSprites.TryGetValue(id.Substring(0, separator), out var art) &&
				art.Holder.GetChildOrNull<Node2D>(0)?.GetChildOrNull<NCreatureVisuals>(0) is { HasSpineAnimation: true } visuals)
			{
				var native = visuals.SpineAnimation.GetAnimationState()?.BoundObject;
				if (native?.HasMethod("clear_track") == true) native.Call("clear_track", int.Parse(id.Substring(separator + 1)));
			}
			_motionTrackKeys.Remove(id);
		}
	}
}
