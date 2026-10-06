using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
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
	public void Cleanup() => LiveSharingController.Suspend();
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
	private static double _settingsTimer, _captureTimer, _drawingTimer, _sinceCapture;
	private static double _pointerTimer;
	private static bool _dirty;
	private static readonly bool TransportTest = System.Environment.GetEnvironmentVariable("RMP_SPECTATOR_TRANSPORT_TEST") == "1";
	private static IEnumerator<byte>? _captureWork;
	private static string _capturePage = "";
	private static double _captureCpuMs, _lastCaptureSliceMs, _lastCaptureTotalMs, _lastCaptureRenderMs;
	private static int _captureFrames, _lastCaptureFrames;
	private const double CaptureBudgetMs = 2;
	private static string _lastError = "";

	internal static void Initialize()
	{
		Suspend();
		Log.Info("[RMP:LiveSharing] Spectator initialized; new profiles default OFF, saved preferences restore in runs.");
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
		SpectatorPreferences.LoadProfile(Suspend);
		if (_view == null && !pressed && !SpectatorPreferences.Current.Open) return;
		var run = GameStateAccessor.GetRunState();
		var service = RunManager.Instance?.NetService;
		bool singleplayer = RunManager.Instance?.IsInProgress == true && run?.Players.Count == 1 && NRun.Instance?.IsNodeReady() == true
			&& service != null && service.Type != NetGameType.Host && service.Type != NetGameType.Client;
		if (!singleplayer) { if (_view != null) Suspend(); return; }
		ulong session = NRun.Instance.GetInstanceId();
		if (_view != null && _session != session) Suspend();
		var settings = SceneMonitor.FindSettingsScreen();
		bool binding = settings != null && settings.IsVisibleInTree();
		if (pressed && !binding && _view != null) { Close(); return; }
		if (_view == null && !binding && (pressed || SpectatorPreferences.Current.Open))
		{
			try
			{
				_source = new LocalSpectatorSource(); _session = session; _captureTimer = _drawingTimer = _pointerTimer = 0; _sinceCapture = 1; _dirty = true;
				_source.RestorePreviewSource(SpectatorPreferences.Current.SourceId);
				var relicRow = LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventoryHolder>(NRun.Instance.GlobalUi.RelicInventory).Where(n => n.IsVisibleInTree()).Select(n => n.GetGlobalRect()).OrderBy(r => r.Position.Y).FirstOrDefault();
				SpectatorPreferences.DefaultTop = relicRow.Size.Y > 0 ? relicRow.Position.Y + relicRow.Size.Y * 3 / 5 : 116;
				_view = new SpectatorView(Close, () => { _source?.RequestDeck(); _captureTimer = 0; _dirty = true; }, id =>
				{
					if (_source?.SelectPreviewSource(id) != true) return;
					SpectatorPreferences.Current.SourceId = id; SpectatorPreferences.Save();
					CancelCapture(); _captureTimer = 0; _sinceCapture = 1; _dirty = true;
				}, enabled => _source?.SetControlMode(enabled) ?? 0, command =>
				{
					var result = _source?.ExecuteCommand(command) ?? new SpectatorCommandResult { RequestId = command.RequestId, Message = "Source unavailable" };
					CancelCapture(); _captureTimer = 0; _dirty = true;
					return result;
				});
				SpectatorPreferences.Current.Open = true; SpectatorPreferences.Save();
				Log.Info("[RMP:LiveSharing] Local spectator opened.");
			}
			catch (Exception ex) { Suspend(); Log.Warn("[RMP:LiveSharing] Could not open window: " + ex); }
		}
		if (_view == null || _source == null) return;
		_pointerTimer -= delta;
		if (_pointerTimer <= 0)
		{
			_pointerTimer = 1d / 30;
			try { _view.UpdatePointer(_source.CapturePointer(_view.PointerExclusion)); }
			catch (Exception ex) { if (_lastError != ex.Message) Log.Warn("[RMP:LiveSharing] Pointer update: " + ex.Message); _lastError = ex.Message; }
		}
		_lastCaptureSliceMs = 0;
		_lastCaptureRenderMs = 0;
		_captureTimer -= delta;
		_drawingTimer -= delta; _sinceCapture += delta;
		_dirty |= _source.Observe(run!);
		string pageToken = _source.PageToken();
		if (_captureWork != null && pageToken != _capturePage) { CancelCapture(); _captureTimer = 0; _dirty = true; }
		if (_drawingTimer <= 0)
		{
			_drawingTimer = 0.05;
			try { if (_source.CaptureMapDrawings() is { } drawings) _view.UpdateMapDrawings(drawings); }
			catch (Exception ex) { if (_lastError != ex.Message) Log.Warn("[RMP:LiveSharing] Drawing update: " + ex.Message); _lastError = ex.Message; }
		}
		if (_captureWork == null && _captureTimer > 0 && (!_dirty || _sinceCapture < 0.075)) return;
		try
		{
			if (_captureWork == null)
			{
				_source.WantDeck = _view.WantsDeck; _source.WantPile = _view.BrowsePile; _capturePage = pageToken; _captureCpuMs = 0; _captureFrames = 0; _dirty = false;
				_captureWork = _source.CaptureSteps(run!, snapshot =>
				{
					if (_source == null || _view == null || _source.PageToken() != _capturePage) return;
					if (TransportTest) snapshot = SpectatorSnapshot.RoundTrip(snapshot);
					if (SpectatorPreferences.Current.SourceId != snapshot.SourceId) { SpectatorPreferences.Current.SourceId = snapshot.SourceId; SpectatorPreferences.Save(); }
					long renderStarted = Stopwatch.GetTimestamp();
					_view.Update(snapshot); _lastCaptureRenderMs = (Stopwatch.GetTimestamp() - renderStarted) * 1000d / Stopwatch.Frequency; _lastError = "";
				}).GetEnumerator();
			}
			long started = Stopwatch.GetTimestamp(); bool more;
			do { more = _captureWork.MoveNext(); }
			while (more && (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency < CaptureBudgetMs);
			_lastCaptureSliceMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
			_captureCpuMs += _lastCaptureSliceMs; _captureFrames++;
			if (!more)
			{
				_captureWork.Dispose(); _captureWork = null; _lastCaptureFrames = _captureFrames; _lastCaptureTotalMs = _captureCpuMs;
				_captureTimer = Math.Clamp(_captureCpuMs * 0.004, 0.05, 0.25); _sinceCapture = 0; _drawingTimer = 0.05;
			}
		}
		catch (Exception ex)
		{
			_view.ShowError(ex.Message);
			if (_lastError != ex.Message) Log.Warn("[RMP:LiveSharing] Snapshot unavailable: " + ex);
			_lastError = ex.Message;
			CancelCapture();
			_captureTimer = 0.25; _dirty = false;
		}
	}

	internal static void Close()
	{
		SpectatorPreferences.Current.Open = false; SpectatorPreferences.Save(); Suspend();
	}
	internal static void Suspend()
	{
		CancelCapture();
		_view?.Dispose(); _source?.Dispose(); _view = null; _source = null; _session = 0; _lastError = "";
	}
	internal static void CancelCapture()
	{
		if (_captureWork == null) return;
		_captureWork?.Dispose(); _captureWork = null; _source?.AbortCapture();
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
				string text = Localization.Get("LIVE_SHARING_INPUT_LABEL", LocalSpectatorSource.T("本地观战面板（单人测试）", "Local spectator (singleplayer test)"));
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
