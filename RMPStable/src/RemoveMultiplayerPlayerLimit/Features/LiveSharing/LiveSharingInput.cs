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

internal static partial class LiveSharingController
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
