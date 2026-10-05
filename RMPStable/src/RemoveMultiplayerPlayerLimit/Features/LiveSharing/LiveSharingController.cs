using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

public sealed class LiveSharingModule : IRMPModule
{
	public string Name => "LocalLiveSharing";
	public void Initialize(ConfigManager config, ReflectionCache cache) => LiveSharingController.Initialize();
	public Node? CreateNode() => null;
	public void Cleanup() => LiveSharingController.Close();
}

internal static class LiveSharingController
{
	private static readonly StringName Action = new("rmpLiveSharing");
	private static readonly FieldInfo? Inputs = typeof(NInputManager).GetField(
#if STS2_0111
		"remappableMKbInputs",
#else
		"remappableKeyboardInputs",
#endif
		BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly FieldInfo? Keys = typeof(NInputManager).GetField(
#if STS2_0111
		"_mKbInputMap",
#else
		"_keyboardInputMap",
#endif
		BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? Titles = typeof(NInputSettingsEntry).GetField(
#if STS2_0111
		"commandToLocTitle",
#else
		"_commandToLocTitle",
#endif
		BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
	private static SpectatorView? _view;
	private static LocalSpectatorSource? _source;
	private static ulong _session;
	private static bool _wasDown;
	private static double _settingsTimer, _captureTimer;
	private static string _lastError = "";

	internal static void Initialize()
	{
		Close();
		Log.Info("[RMP:LiveSharing] Singleplayer spectator prototype initialized, OFF by default; default hotkey F8.");
	}

	internal static void ProcessFrame(double delta)
	{
		_settingsTimer -= delta;
		if (_settingsTimer <= 0) { RegisterInput(); _settingsTimer = 0.5; }
		Key key = Key.None;
		if (NInputManager.Instance != null && Keys?.GetValue(NInputManager.Instance) is Dictionary<StringName, Key> map) map.TryGetValue(Action, out key);
		bool down = key != Key.None && Input.IsKeyPressed(key);
		bool pressed = down && !_wasDown;
		_wasDown = down;
		if (_view == null && !pressed) return;
		var run = GameStateAccessor.GetRunState();
		var service = RunManager.Instance?.NetService;
		bool singleplayer = RunManager.Instance?.IsInProgress == true && run?.Players.Count == 1 && NRun.Instance != null
			&& service != null && service.Type != NetGameType.Host && service.Type != NetGameType.Client;
		if (!singleplayer) { if (_view != null) Close(); return; }
		ulong session = NRun.Instance.GetInstanceId();
		if (_view != null && _session != session) Close();
		var settings = SceneMonitor.FindSettingsScreen();
		bool binding = settings != null && settings.IsVisibleInTree();
		if (pressed && !binding)
		{
			if (_view != null) { Close(); return; }
			try
			{
				_source = new LocalSpectatorSource(); _session = session; _captureTimer = 0;
				_view = new SpectatorView(Close);
				Log.Info("[RMP:LiveSharing] Local spectator opened.");
			}
			catch (Exception ex) { Close(); Log.Warn("[RMP:LiveSharing] Could not open window: " + ex); }
		}
		if (_view == null || _source == null) return;
		_captureTimer -= delta;
		if (_captureTimer > 0) return;
		_captureTimer = 0.25;
		try
		{
			var snapshot = SpectatorSnapshot.RoundTrip(_source.Capture(run!));
			_view.Update(snapshot); _lastError = "";
		}
		catch (Exception ex)
		{
			_view.ShowError(ex.Message);
			if (_lastError != ex.Message) Log.Warn("[RMP:LiveSharing] Snapshot unavailable: " + ex);
			_lastError = ex.Message;
		}
	}

	internal static void Close()
	{
		_view?.Dispose(); _view = null; _source = null; _session = 0; _lastError = "";
	}

	private static void RegisterInput()
	{
		if (NInputManager.Instance == null) return;
		try
		{
			if (Inputs?.GetValue(null) is not ICollection<StringName> inputs ||
				Keys?.GetValue(NInputManager.Instance) is not Dictionary<StringName, Key> keys ||
				Titles?.GetValue(null) is not Dictionary<StringName, string> titles)
				throw new MissingFieldException("Native input settings fields unavailable");
			if (!inputs.Contains(Action)) inputs.Add(Action);
			titles[Action] = "viewMap";
			if (!keys.ContainsKey(Action))
			{
				keys[Action] = Key.None;
#if STS2_0111
				NInputManager.Instance.ModifyMKbKey(Action, Key.F8);
#else
				NInputManager.Instance.ModifyShortcutKey(Action, Key.F8);
#endif
				Log.Info("[RMP:LiveSharing] Native remappable F8 binding registered.");
			}
			var settings = SceneMonitor.FindSettingsScreen();
			if (settings == null) return;
			foreach (var entry in LocalSpectatorSource.Descendants<NInputSettingsEntry>(settings))
			{
				if (entry.InputName != Action) continue;
				string text = Localization.Get("LIVE_SHARING_INPUT_LABEL", LocalSpectatorSource.T("本地观战窗口（单人测试）", "Local spectator (singleplayer test)"));
				var label = entry.GetNodeOrNull<Node>("%InputLabel");
				if (label is MegaLabel plain) plain.SetTextAutoSize(text);
				else if (label is MegaRichTextLabel rich) rich.SetTextAutoSize(text);
			}
		}
		catch (Exception ex)
		{
			if (_lastError != ex.Message) Log.Warn("[RMP:LiveSharing] Input registration: " + ex.Message);
			_lastError = ex.Message;
		}
	}
}
