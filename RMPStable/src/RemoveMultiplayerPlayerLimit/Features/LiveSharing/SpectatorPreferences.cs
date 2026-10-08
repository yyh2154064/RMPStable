using System;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Saves;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Account/profile scoped, independent of run saves and Quick SL checkpoints.
internal sealed class SpectatorPreferences
{
	public bool Open { get; set; }
	public bool HasLayout { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public float Width { get; set; }
	public int DockEdge { get; set; }
	public bool Pinned { get; set; }
	public bool ControlMode { get; set; }
	public string SourceId { get; set; } = "";
	internal static SpectatorPreferences Current { get; private set; } = new();
	internal static float DefaultTop { get; set; } = 116;
	private static string _path = "";
	internal static bool LoadProfile(Action beforeChange)
	{
		if (SaveManager.Instance?.IsProfileInitialized != true) return false;
		string path = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("rmp-spectator.json"));
		if (path == _path) return false;
		beforeChange();
		_path = path; Current = new();
		try
		{
			if (File.Exists(path)) Current = JsonSerializer.Deserialize<SpectatorPreferences>(File.ReadAllText(path)) ?? new();
			if (!float.IsFinite(Current.X) || !float.IsFinite(Current.Y) || !float.IsFinite(Current.Width) || Current.Width <= 0) Current.HasLayout = false;
			Current.SourceId ??= "";
			if (Current.DockEdge is < 0 or > 4 || !Current.HasLayout) Current.DockEdge = 0;
		}
		catch (Exception ex) { Log.Warn("[RMP:LiveSharing] Preferences load: " + ex.Message); }
		return true;
	}
	internal static void Save()
	{
		if (_path.Length == 0) return;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(Current));
			File.Move(_path + ".tmp", _path, true);
		}
		catch (Exception ex) { Log.Warn("[RMP:LiveSharing] Preferences save: " + ex.Message); }
	}
	internal static void RememberLayout(Vector2 position, float width)
	{
		var p = Current;
		if (p.HasLayout && p.X == position.X && p.Y == position.Y && p.Width == width) return;
		p.HasLayout = true; p.X = position.X; p.Y = position.Y; p.Width = width; Save();
	}
}
