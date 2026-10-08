using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.addons.mega_text;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class SpectatorView
{
	private sealed record LabelNode(Node2D Holder, TextSnapshot Snapshot);
	private readonly Dictionary<ulong, Dictionary<string, LabelNode>> _retainedLabels = new();
	private readonly Dictionary<string, (Node2D Holder, CreatureSnapshot Snapshot)> _creatureSprites = new();
	private readonly Dictionary<string, (Control Root, CreatureSnapshot Snapshot)> _creatureStates = new();

	private void RetainLabels(Node parent, List<TextSnapshot> labels, bool nativeOrder = false)
	{
		ulong id = parent.GetInstanceId();
		if (!_retainedLabels.TryGetValue(id, out var previous)) previous = new();
		var next = new Dictionary<string, LabelNode>();
		for (int i = 0; i < labels.Count; i++)
		{
			var text = labels[i]; string key = text.ArtKey + ":" + i;
			Node destination = nativeOrder && PageAnchor(text.ArtKey) is { } anchor ? anchor : parent;
			if (!previous.TryGetValue(key, out var old) || !GodotObject.IsInstanceValid(old.Holder) || old.Holder.IsQueuedForDeletion() || !old.Holder.IsInsideTree() || old.Snapshot.Rich != text.Rich || old.Snapshot.Font != text.Font)
			{
				var holder = DrawLabels(parent, new List<TextSnapshot> { text }, nativeOrder)[0];
				next[key] = new LabelNode(holder, text); continue;
			}
			var p = old.Snapshot;
			if (old.Holder.GetParent() != destination) { old.Holder.GetParent()?.RemoveChild(old.Holder); destination.AddChild(old.Holder); }
			old.Holder.Transform = destination == parent ? Matrix(text.Transform) : Transform2D.Identity;
			var control = (Control)old.Holder.GetChild(0);
			if (!SnapshotEquality.Array(p.Size, text.Size)) control.Size = new Vector2(text.Size[0], text.Size[1]);
			var color = destination == parent ? text.Color : text.LocalColor;
			// Themes are set only for changed values; frequent timer/HP text changes
			// no longer instantiate every label and material in the HUD.
			if (control is MegaRichTextLabel rich)
			{
				if (p.Text != text.Text) rich.SetTextAutoSize(text.Text);
				if (p.FontSize != text.FontSize) foreach (var name in ThemeConstants.RichTextLabel.AllFontSizes) rich.AddThemeFontSizeOverride(name, text.FontSize);
				if (p.Alignment != text.Alignment) rich.HorizontalAlignment = (HorizontalAlignment)text.Alignment;
				if (p.VerticalAlignment != text.VerticalAlignment) rich.VerticalAlignment = (VerticalAlignment)text.VerticalAlignment;
				if (p.WrapMode != text.WrapMode) rich.AutowrapMode = (TextServer.AutowrapMode)text.WrapMode;
				if (!SnapshotEquality.Array(p.Color, text.Color) || !SnapshotEquality.Array(p.LocalColor, text.LocalColor)) rich.AddThemeColorOverride("default_color", ColorOf(color));
			}
			else if (control is Label label)
			{
				if (p.Text != text.Text) label.Text = text.Text;
				if (p.FontSize != text.FontSize) label.AddThemeFontSizeOverride("font_size", text.FontSize);
				if (p.Alignment != text.Alignment) label.HorizontalAlignment = (HorizontalAlignment)text.Alignment;
				if (p.VerticalAlignment != text.VerticalAlignment) label.VerticalAlignment = (VerticalAlignment)text.VerticalAlignment;
				if (!SnapshotEquality.Array(p.Color, text.Color) || !SnapshotEquality.Array(p.LocalColor, text.LocalColor)) label.AddThemeColorOverride("font_color", ColorOf(color));
			}
			if (!SnapshotEquality.Array(p.OutlineColor, text.OutlineColor)) control.AddThemeColorOverride("font_outline_color", ColorOf(text.OutlineColor));
			if (p.OutlineSize != text.OutlineSize) control.AddThemeConstantOverride("outline_size", text.OutlineSize);
			next[key] = new LabelNode(old.Holder, text);
		}
		foreach (var old in previous) if ((!next.TryGetValue(old.Key, out var current) || current.Holder != old.Value.Holder) && GodotObject.IsInstanceValid(old.Value.Holder) && !old.Value.Holder.IsQueuedForDeletion())
		{ old.Value.Holder.GetParent()?.RemoveChild(old.Value.Holder); _mountedLabels.Remove(old.Value.Holder); old.Value.Holder.QueueFree(); }
		_retainedLabels[id] = next;
	}

	private static string CreatureKey(CreatureSnapshot c, int index) => c.EntityKey.Length > 0 ? c.EntityKey : index + ":" + c.Name;
	private void RetainCreatureArt(List<CreatureSnapshot> creatures)
	{
		var live = new HashSet<string>();
		for (int i = 0; i < creatures.Count; i++)
		{
			var c = creatures[i]; string key = CreatureKey(c, i); live.Add(key);
			if (_creatureSprites.TryGetValue(key, out var old) && old.Snapshot.VisualScene == c.VisualScene)
			{
				if (_latestMotion?.Creatures.Any(m => m.EntityKey == key) != true && !SnapshotEquality.Array(old.Snapshot.Transform, c.Transform)) old.Holder.Transform = Matrix(c.Transform);
				_creatureSprites[key] = (old.Holder, c); continue;
			}
			if (old.Holder != null) { _actorSprites.RemoveChild(old.Holder); old.Holder.QueueFree(); }
			var root = new Node2D(); _actorSprites.AddChild(root);
			DrawCreatureArt(new List<CreatureSnapshot> { c }, root);
			// The wrapper owns the transform; the drawing child remains local.
			root.Transform = Matrix(c.Transform); ((Node2D)root.GetChild(0)).Transform = Transform2D.Identity;
			_creatureSprites[key] = (root, c);
		}
		foreach (var key in _creatureSprites.Keys.Where(k => !live.Contains(k)).ToList())
		{ var node = _creatureSprites[key].Holder; _actorSprites.RemoveChild(node); node.QueueFree(); _creatureSprites.Remove(key); }
	}
	private void RetainCreatureState(List<CreatureSnapshot> creatures)
	{
		var live = new HashSet<string>();
		for (int i = 0; i < creatures.Count; i++)
		{
			var c = creatures[i]; string key = CreatureKey(c, i); live.Add(key);
			if (!_creatureStates.TryGetValue(key, out var old)) { var root = Area(_actorState, Vector2.Zero, _page.Size); old = (root, c); }
			var art = new List<ArtSnapshot>(c.StateArt); var labels = new List<TextSnapshot>(c.StateLabels);
			foreach (var intent in c.Intents) { art.AddRange(intent.Art); labels.AddRange(intent.Labels); }
			RetainArt(old.Root, art); RetainLabels(old.Root, labels);
			_creatureStates[key] = (old.Root, c);
		}
		foreach (var key in _creatureStates.Keys.Where(k => !live.Contains(k)).ToList())
		{
			var root = _creatureStates[key].Root; _retainedArt.Remove(root.GetInstanceId()); _retainedLabels.Remove(root.GetInstanceId());
			_actorState.RemoveChild(root); root.QueueFree(); _creatureStates.Remove(key);
		}
	}
}
