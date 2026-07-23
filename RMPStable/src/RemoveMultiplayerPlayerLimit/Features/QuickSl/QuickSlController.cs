using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;
using RemoveMultiplayerPlayerLimit.Infrastructure;
using RemoveMultiplayerPlayerLimit.Network;
using Steamworks;

namespace RemoveMultiplayerPlayerLimit.Features.QuickSl;

internal static class QuickSlController
{
	private const string InputActionText = "rmpQuickSl";
	private static readonly StringName InputAction = new StringName(InputActionText);
	private static readonly FieldInfo? PauseButtonLabelField = typeof(NPauseMenuButton).GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? RemappableKeyboardInputsField = typeof(NInputManager).GetField("remappableKeyboardInputs", BindingFlags.Static | BindingFlags.NonPublic);
	private static readonly FieldInfo? KeyboardInputMapField = typeof(NInputManager).GetField("_keyboardInputMap", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? EntryTitleMapField = typeof(NInputSettingsEntry).GetField("_commandToLocTitle", BindingFlags.Static | BindingFlags.NonPublic);
	private static readonly FieldInfo? VerticalPopupSceneField = typeof(NVerticalPopup).GetField("_scenePath", BindingFlags.Static | BindingFlags.NonPublic);
	private static readonly MethodInfo? ContinueMethod = typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu).GetMethod("OnContinueButtonPressedAsync", BindingFlags.Instance | BindingFlags.NonPublic);
	private static INetGameService? _boundService;
	private static NPauseMenu? _patchedPauseMenu;
	private static bool _inputRegistered;
	private static bool _hotkeyWasDown;
	private static bool _popupOpen;
	private static bool _operationRunning;
	private static RecoveryState? _recovery;

	private sealed class RecoveryState
	{
		public ulong OperationId;
		public ulong HostId;
		public ulong PreviousLobbyId;
		public HashSet<ulong> ExpectedPlayers = new HashSet<ulong>();
		public Dictionary<ulong, DateTime> Deadlines = new Dictionary<ulong, DateTime>();
		public bool IsHost;
		public bool HasSeenLoadLobby;
		public bool LocalReadySent;
		public bool AutoStartCancelled;
		public bool HostReadySent;
	}

	internal static void Initialize()
	{
		RegisterInputAction();
		Log.Info("[RMP:QuickSL] Initialized (default hotkey F5, reconnect timeout 8s).");
	}

	internal static void Cleanup()
	{
		BindService(null);
		_recovery = null;
		_patchedPauseMenu = null;
	}

	internal static void ProcessFrame()
	{
		RegisterInputAction();
		INetGameService? service = RunManager.Instance?.NetService ?? SceneMonitor.FindActiveLoadRunLobby()?.NetService;
		BindService(service);
		PatchPauseMenu();
		PatchInputSettingsLabel();
		PollHotkey();
		ProcessRecoveryLobby();
	}

	private static void RegisterInputAction()
	{
		if (NInputManager.Instance == null)
		{
			return;
		}
		try
		{
			if (!_inputRegistered && RemappableKeyboardInputsField?.GetValue(null) is ICollection<StringName> inputs && !inputs.Contains(InputAction))
			{
				inputs.Add(InputAction);
			}
			if (!_inputRegistered && EntryTitleMapField?.GetValue(null) is Dictionary<StringName, string> titles)
			{
				titles[InputAction] = "viewMap";
			}
			if (KeyboardInputMapField?.GetValue(NInputManager.Instance) is Dictionary<StringName, Key> current && !current.ContainsKey(InputAction))
			{
				current[InputAction] = Key.None;
				NInputManager.Instance.ModifyShortcutKey(InputAction, Key.F5);
			}
			_inputRegistered = true;
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:QuickSL] Could not register the native input action: " + ex.Message);
		}
	}

	private static void PatchInputSettingsLabel()
	{
		NSettingsScreen? screen = SceneMonitor.FindSettingsScreen();
		if (screen == null)
		{
			return;
		}
		NInputSettingsEntry? entry = null;
		foreach (NInputSettingsEntry candidate in FindNodesOfType<NInputSettingsEntry>(screen))
		{
			if (candidate.InputName == InputAction)
			{
				entry = candidate;
				break;
			}
		}
		MegaRichTextLabel? label = entry?.GetNodeOrNull<MegaRichTextLabel>("%InputLabel");
		if (label != null)
		{
			label.Text = Localization.Get("QUICK_SL_INPUT_LABEL", "Quick SL");
		}
	}

	private static IEnumerable<T> FindNodesOfType<T>(Node root) where T : Node
	{
		if (root is T typed)
		{
			yield return typed;
		}
		foreach (Node child in root.GetChildren())
		{
			foreach (T nested in FindNodesOfType<T>(child))
			{
				yield return nested;
			}
		}
	}

	private static void PatchPauseMenu()
	{
		NPauseMenu? pause = SceneMonitor.FindNodeOfType<NPauseMenu>(SceneMonitor.GetRoot());
		if (pause == null)
		{
			return;
		}
		if (ReferenceEquals(pause, _patchedPauseMenu))
		{
			NPauseMenuButton? existing = pause.GetNodeOrNull<Control>("%ButtonContainer")?.GetNodeOrNull<NPauseMenuButton>("RmpQuickSl");
			MegaLabel? existingLabel = existing?.GetNodeOrNull<MegaLabel>("Label") ?? (existing == null ? null : PauseButtonLabelField?.GetValue(existing) as MegaLabel);
			existingLabel?.SetTextAutoSize(Localization.Get("QUICK_SL_BUTTON", "Quick SL"));
			return;
		}
		try
		{
			Control? container = pause.GetNodeOrNull<Control>("%ButtonContainer");
			NPauseMenuButton? compendium = container?.GetNodeOrNull<NPauseMenuButton>("Compendium");
			if (container == null || compendium == null)
			{
				return;
			}
			NPauseMenuButton? button = container.GetNodeOrNull<NPauseMenuButton>("RmpQuickSl");
			if (button == null)
			{
				button = compendium.Duplicate(14) as NPauseMenuButton;
				if (button == null)
				{
					return;
				}
				button.Name = "RmpQuickSl";
				button.Visible = true;
				container.AddChild(button);
				container.MoveChild(button, compendium.GetIndex() + 1);
				button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => TriggerRequested()));
			}
			MegaLabel? label = button.GetNodeOrNull<MegaLabel>("Label") ?? PauseButtonLabelField?.GetValue(button) as MegaLabel;
			label?.SetTextAutoSize(Localization.Get("QUICK_SL_BUTTON", "Quick SL"));
			RebuildPauseFocus(container);
			_patchedPauseMenu = pause;
			Log.Info("[RMP:QuickSL] Pause menu button injected.");
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:QuickSL] Pause menu injection failed: " + ex.Message);
			_patchedPauseMenu = pause;
		}
	}

	private static void RebuildPauseFocus(Control container)
	{
		List<NPauseMenuButton> buttons = container.GetChildren().OfType<NPauseMenuButton>().Where(button => button.Visible).ToList();
		for (int i = 0; i < buttons.Count; i++)
		{
			buttons[i].FocusNeighborLeft = buttons[i].GetPath();
			buttons[i].FocusNeighborRight = buttons[i].GetPath();
			buttons[i].FocusNeighborTop = buttons[(i + buttons.Count - 1) % buttons.Count].GetPath();
			buttons[i].FocusNeighborBottom = buttons[(i + 1) % buttons.Count].GetPath();
		}
	}

	private static void PollHotkey()
	{
		Key key = _inputRegistered ? NInputManager.Instance.GetShortcutKey(InputAction) : Key.F5;
		bool down = key != Key.None && Input.IsKeyPressed(key);
		if (down && !_hotkeyWasDown && RunManager.Instance?.IsInProgress == true)
		{
			TriggerRequested();
		}
		_hotkeyWasDown = down;
	}

	private static void TriggerRequested()
	{
		if (_popupOpen || _operationRunning || RunManager.Instance?.IsInProgress != true)
		{
			return;
		}
		INetGameService? service = RunManager.Instance.NetService;
		if (service == null || (service.Type != NetGameType.Host && service.Type != NetGameType.Client))
		{
			ShowConfirmation(Localization.Get("QUICK_SL_CONFIRM_TITLE", "Quick SL"), Localization.Get("QUICK_SL_CONFIRM_SINGLE", "Return to the latest native save checkpoint?"), () => TaskHelper.RunSafely(RunSingleplayerSlAsync()));
			return;
		}
		if (service.Type == NetGameType.Client)
		{
			ShowConfirmation(Localization.Get("QUICK_SL_CONFIRM_TITLE", "Quick SL"), Localization.Get("QUICK_SL_CONFIRM_CLIENT", "Send a Quick SL request to the host?"), () =>
			{
				service.SendMessage(new RmpQuickSlRequestMessage());
				Log.Info("[RMP:QuickSL] Request sent to host.");
			});
			return;
		}
		if (service.Type == NetGameType.Host)
		{
			ShowConfirmation(Localization.Get("QUICK_SL_CONFIRM_TITLE", "Quick SL"), Localization.Get("QUICK_SL_CONFIRM_HOST", "Reload the multiplayer checkpoint for everyone?"), () => TaskHelper.RunSafely(BeginHostSlAsync()));
		}
	}

	private static async Task RunSingleplayerSlAsync()
	{
		if (_operationRunning)
		{
			return;
		}
		_operationRunning = true;
		try
		{
			await NGame.Instance.ReturnToMainMenu();
			object? result = ContinueMethod?.Invoke(NGame.Instance.MainMenu, null);
			if (result is Task task)
			{
				await task;
			}
			else
			{
				throw new MissingMethodException("NMainMenu.OnContinueButtonPressedAsync");
			}
			Log.Info("[RMP:QuickSL] Singleplayer checkpoint reloaded through the native Continue flow.");
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:QuickSL] Singleplayer reload failed: " + ex);
		}
		finally
		{
			_operationRunning = false;
		}
	}

	private static void BindService(INetGameService? service)
	{
		if (service != null && service.Type != NetGameType.Host && service.Type != NetGameType.Client)
		{
			service = null;
		}
		if (ReferenceEquals(service, _boundService))
		{
			return;
		}
		if (_boundService != null)
		{
			try
			{
				_boundService.UnregisterMessageHandler<RmpQuickSlRequestMessage>(OnQuickSlRequest);
				_boundService.UnregisterMessageHandler<RmpQuickSlDecisionMessage>(OnQuickSlDecision);
				_boundService.UnregisterMessageHandler<RmpQuickSlBeginMessage>(OnQuickSlBegin);
			}
			catch { }
		}
		_boundService = service;
		if (_boundService != null)
		{
			_boundService.RegisterMessageHandler<RmpQuickSlRequestMessage>(OnQuickSlRequest);
			_boundService.RegisterMessageHandler<RmpQuickSlDecisionMessage>(OnQuickSlDecision);
			_boundService.RegisterMessageHandler<RmpQuickSlBeginMessage>(OnQuickSlBegin);
		}
	}

	private static void OnQuickSlRequest(RmpQuickSlRequestMessage message, ulong senderId)
	{
		if (_boundService?.Type != NetGameType.Host || RunManager.Instance?.IsInProgress != true)
		{
			return;
		}
		if (_popupOpen || _operationRunning)
		{
			_boundService.SendMessage(new RmpQuickSlDecisionMessage { Accepted = false }, senderId);
			return;
		}
		string name = GetPlayerName(senderId);
		ShowConfirmation(Localization.Get("QUICK_SL_REQUEST_TITLE", "Quick SL Request"), string.Format(Localization.Get("QUICK_SL_REQUEST_BODY", "{0} requests a Quick SL. Accept?"), name), () =>
		{
			_boundService?.SendMessage(new RmpQuickSlDecisionMessage { Accepted = true }, senderId);
			TaskHelper.RunSafely(BeginHostSlAsync());
		}, () => _boundService?.SendMessage(new RmpQuickSlDecisionMessage { Accepted = false }, senderId));
	}

	private static void OnQuickSlDecision(RmpQuickSlDecisionMessage message, ulong senderId)
	{
		if (!message.Accepted)
		{
			ShowInformation(Localization.Get("QUICK_SL_REJECTED_TITLE", "Quick SL"), Localization.Get("QUICK_SL_REJECTED_BODY", "The host rejected the Quick SL request."));
		}
	}

	private static void OnQuickSlBegin(RmpQuickSlBeginMessage message, ulong senderId)
	{
		if (_boundService?.Type != NetGameType.Client || senderId != message.HostId || _operationRunning)
		{
			return;
		}
		_recovery = new RecoveryState
		{
			OperationId = message.OperationId,
			HostId = message.HostId,
			PreviousLobbyId = message.PreviousLobbyId,
			IsHost = false
		};
		TaskHelper.RunSafely(ReturnAndReconnectClientAsync(_recovery));
	}

	private static async Task BeginHostSlAsync()
	{
		if (_operationRunning || RunManager.Instance?.NetService?.Type != NetGameType.Host)
		{
			return;
		}
		_operationRunning = true;
		try
		{
			INetGameService service = RunManager.Instance.NetService;
			HashSet<ulong> expected = RunManager.Instance.RunLobby?.ConnectedPlayerIds.ToHashSet() ?? new HashSet<ulong> { service.NetId };
			ulong oldLobbyId = SteamLobbyHelper.TryGetLobbyId(service, out ulong lobbyId) ? lobbyId : 0UL;
			RecoveryState recovery = new RecoveryState
			{
				OperationId = unchecked((ulong)DateTime.UtcNow.Ticks),
				HostId = service.NetId,
				PreviousLobbyId = oldLobbyId,
				ExpectedPlayers = expected,
				IsHost = true
			};
			_recovery = recovery;
			service.SendMessage(new RmpQuickSlBeginMessage
			{
				OperationId = recovery.OperationId,
				HostId = recovery.HostId,
				PreviousLobbyId = recovery.PreviousLobbyId
			});
			await Task.Delay(150);
			await NGame.Instance.ReturnToMainMenu();
			if (!await HostBootstrapModule.StartQuickSlLoadedHostAsync())
			{
				recovery.AutoStartCancelled = true;
			}
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:QuickSL] Host reload failed: " + ex);
			if (_recovery != null)
			{
				_recovery.AutoStartCancelled = true;
			}
		}
		finally
		{
			_operationRunning = false;
		}
	}

	private static async Task ReturnAndReconnectClientAsync(RecoveryState recovery)
	{
		_operationRunning = true;
		try
		{
			await NGame.Instance.ReturnToMainMenu();
			if (!SteamInitializer.Initialized)
			{
				Log.Warn("[RMP:QuickSL] Automatic reconnect requires Steam; falling back to the multiplayer menu.");
				NGame.Instance.MainMenu.OpenMultiplayerSubmenu();
				return;
			}
			SteamFriends.RequestFriendRichPresence(new CSteamID(recovery.HostId));
			ulong lobbyId = 0;
			DateTime availabilityLimit = DateTime.UtcNow.AddSeconds(60);
			while (DateTime.UtcNow < availabilityLimit && lobbyId == 0)
			{
				lobbyId = FindNewHostLobby(recovery.HostId, recovery.PreviousLobbyId);
				if (lobbyId == 0)
				{
					await Task.Delay(100);
				}
			}
			if (lobbyId == 0)
			{
				Log.Warn("[RMP:QuickSL] Host lobby was not advertised; falling back to manual join.");
				NGame.Instance.MainMenu.OpenMultiplayerSubmenu();
				return;
			}
			Task joinTask = NGame.Instance.MainMenu.JoinGame(SteamClientConnectionInitializer.FromLobby(lobbyId));
			Task finished = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(8)));
			if (finished != joinTask)
			{
				recovery.AutoStartCancelled = true;
				CancelActiveJoinFlow();
				Log.Warn("[RMP:QuickSL] Automatic reconnect timed out after 8 seconds; manual join remains available.");
				NGame.Instance.MainMenu.OpenMultiplayerSubmenu();
				return;
			}
			await joinTask;
		}
		catch (Exception ex)
		{
			recovery.AutoStartCancelled = true;
			Log.Warn("[RMP:QuickSL] Automatic reconnect failed: " + ex.Message);
			if (NGame.Instance?.MainMenu != null && SceneMonitor.FindActiveLoadRunLobby() == null)
			{
				NGame.Instance.MainMenu.OpenMultiplayerSubmenu();
			}
		}
		finally
		{
			_operationRunning = false;
		}
	}

	private static void ProcessRecoveryLobby()
	{
		RecoveryState? recovery = _recovery;
		if (recovery == null)
		{
			return;
		}
		LoadRunLobby? lobby = SceneMonitor.FindActiveLoadRunLobby();
		if (lobby == null)
		{
			if (recovery.HasSeenLoadLobby && RunManager.Instance?.IsInProgress == true)
			{
				Log.Info("[RMP:QuickSL] Recovery completed.");
				_recovery = null;
			}
			return;
		}
		recovery.HasSeenLoadLobby = true;
		ulong localId = lobby.NetService.NetId;
		if (lobby.NetService.Type == NetGameType.Client)
		{
			if (!recovery.LocalReadySent || !lobby.IsPlayerReady(localId))
			{
				lobby.SetReady(true);
				recovery.LocalReadySent = true;
				Log.Info("[RMP:QuickSL] Reconnected client marked ready automatically.");
			}
			return;
		}
		if (lobby.NetService.Type != NetGameType.Host || !recovery.IsHost)
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		foreach (ulong id in recovery.ExpectedPlayers.Where(id => id != localId))
		{
			if (!recovery.Deadlines.ContainsKey(id))
			{
				recovery.Deadlines[id] = now.AddSeconds(8);
			}
			if (now >= recovery.Deadlines[id] && (!lobby.ConnectedPlayerIds.Contains(id) || !lobby.IsPlayerReady(id)))
			{
				recovery.AutoStartCancelled = true;
			}
		}
		bool allClientsReady = recovery.ExpectedPlayers.Where(id => id != localId).All(id => lobby.ConnectedPlayerIds.Contains(id) && lobby.IsPlayerReady(id));
		if (!recovery.AutoStartCancelled && allClientsReady && !recovery.HostReadySent)
		{
			recovery.HostReadySent = true;
			lobby.SetReady(true);
			Log.Info("[RMP:QuickSL] All original clients are ready; host marked ready last for automatic start.");
		}
	}

	private static ulong FindNewHostLobby(ulong hostId, ulong previousLobbyId)
	{
		try
		{
			CSteamID host = new CSteamID(hostId);
			if (SteamFriends.GetFriendGamePlayed(host, out FriendGameInfo_t gameInfo) && gameInfo.m_steamIDLobby.IsValid())
			{
				ulong advertisedLobbyId = gameInfo.m_steamIDLobby.m_SteamID;
				if (advertisedLobbyId != previousLobbyId && SteamMatchmaking.GetLobbyOwner(gameInfo.m_steamIDLobby).m_SteamID == hostId)
				{
					return advertisedLobbyId;
				}
			}
			string presence = SteamFriends.GetFriendRichPresence(new CSteamID(hostId), "steam_player_group") ?? string.Empty;
			foreach (string token in presence.Split(presence.Where(ch => !char.IsDigit(ch)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries))
			{
				if (ulong.TryParse(token, out ulong id) && id != 0 && id != previousLobbyId)
				{
					CSteamID lobby = new CSteamID(id);
					if (SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == hostId)
					{
						return id;
					}
				}
			}
		}
		catch { }
		return 0;
	}

	private static void CancelActiveJoinFlow()
	{
		try
		{
			Node? screen = SceneMonitor.FindNode(SceneMonitor.GetRoot(), node => node.GetType().Name == "NJoinFriendScreen");
			object? flow = screen?.GetType().GetField("_currentJoinFlow", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(screen);
			flow?.GetType().GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(flow, null);
		}
		catch { }
	}

	private static string GetPlayerName(ulong id)
	{
		try
		{
			if (SteamInitializer.Initialized)
			{
				string name = SteamFriends.GetFriendPersonaName(new CSteamID(id));
				if (!string.IsNullOrWhiteSpace(name))
				{
					return name;
				}
			}
		}
		catch { }
		return id.ToString();
	}

	private static void ShowConfirmation(string title, string body, Action yes, Action? no = null)
	{
		ShowPopup(title, body, yes, no, showNo: true);
	}

	private static void ShowInformation(string title, string body)
	{
		ShowPopup(title, body, () => { }, null, showNo: false);
	}

	private static void ShowPopup(string title, string body, Action yes, Action? no, bool showNo)
	{
		if (_popupOpen || NModalContainer.Instance == null)
		{
			return;
		}
		try
		{
			string? scenePath = VerticalPopupSceneField?.GetValue(null) as string;
			PackedScene? scene = string.IsNullOrEmpty(scenePath) ? null : ResourceLoader.Load<PackedScene>(scenePath, null, ResourceLoader.CacheMode.Reuse);
			NVerticalPopup? popup = scene?.Instantiate<NVerticalPopup>();
			if (popup == null)
			{
				return;
			}
			popup.SetText(title, body);
			popup.YesButton.SetText(Localization.Get("QUICK_SL_YES", "Yes"));
			popup.NoButton.SetText(Localization.Get("QUICK_SL_NO", "No"));
			popup.YesButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(button =>
			{
				ClosePopup();
				_popupOpen = false;
				yes();
			}));
			if (showNo)
			{
				popup.NoButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(button =>
				{
					ClosePopup();
					_popupOpen = false;
					no?.Invoke();
				}));
			}
			else
			{
				popup.HideNoButton();
			}
			_popupOpen = true;
			popup.TreeExiting += () => _popupOpen = false;
			NModalContainer.Instance.Add(popup);
			NModalContainer.Instance.ShowBackstop();
		}
		catch (Exception ex)
		{
			_popupOpen = false;
			Log.Warn("[RMP:QuickSL] Popup creation failed: " + ex.Message);
		}
	}

	private static void ClosePopup()
	{
		NModalContainer.Instance?.Clear();
		NModalContainer.Instance?.HideBackstop();
	}
}
