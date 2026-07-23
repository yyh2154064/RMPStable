using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
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
	private static readonly FieldInfo? RemappableKeyboardInputsField = typeof(NInputManager).GetField("remappableKeyboardInputs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly FieldInfo? KeyboardInputMapField = typeof(NInputManager).GetField("_keyboardInputMap", BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly FieldInfo? EntryTitleMapField = typeof(NInputSettingsEntry).GetField("_commandToLocTitle", BindingFlags.Static | BindingFlags.NonPublic);
	private static INetGameService? _boundService;
	private static NPauseMenu? _patchedPauseMenu;
	private static CanvasLayer? _recoveryCover;
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
		HideRecoveryCover();
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
			if (RemappableKeyboardInputsField?.GetValue(null) is not ICollection<StringName> inputs)
			{
				throw new MissingFieldException(typeof(NInputManager).FullName, "remappableKeyboardInputs");
			}
			if (!inputs.Contains(InputAction))
			{
				inputs.Add(InputAction);
			}
			if (EntryTitleMapField?.GetValue(null) is not Dictionary<StringName, string> titles)
			{
				throw new MissingFieldException(typeof(NInputSettingsEntry).FullName, "_commandToLocTitle");
			}
			titles[InputAction] = "viewMap";
			if (KeyboardInputMapField?.GetValue(NInputManager.Instance) is not Dictionary<StringName, Key> current)
			{
				throw new MissingFieldException(typeof(NInputManager).FullName, "_keyboardInputMap");
			}
			if (!current.ContainsKey(InputAction))
			{
				current[InputAction] = Key.None;
				NInputManager.Instance.ModifyShortcutKey(InputAction, Key.F5);
			}
			if (!_inputRegistered)
			{
				_inputRegistered = true;
				Log.Info($"[RMP:QuickSL] Native input action registered (key={NInputManager.Instance.GetShortcutKey(InputAction)}).");
			}
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
			Task? pendingSave = SaveManager.Instance.CurrentRunSaveTask;
			if (pendingSave != null)
			{
				Log.Info("[RMP:QuickSL] Waiting for the native checkpoint save to finish.");
				await pendingSave;
			}
			ReadSaveResult<SerializableRun> result = SaveManager.Instance.LoadRunSave();
			if (!result.Success || result.SaveData == null)
			{
				throw new InvalidOperationException($"The native run checkpoint could not be read (status={result.Status}).");
			}
			SerializableRun save = result.SaveData;
			RunState runState = RunState.FromSerializable(save);
			await NGame.Instance.Transition.FadeOut(0.35f, runState.Players[0].Character.CharacterSelectTransitionPath);
			RunManager.Instance.CleanUp();
			await RunManager.Instance.SetUpSavedSingleplayer(runState, save);
			NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
			await NGame.Instance.LoadRun(runState, save.PreFinishedRoom);
			await NGame.Instance.Transition.FadeIn(0.35f);
			Log.Info("[RMP:QuickSL] Singleplayer checkpoint reloaded directly without showing the main menu.");
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:QuickSL] Singleplayer reload failed: " + ex);
			try
			{
				await NGame.Instance.ReturnToMainMenu();
			}
			catch (Exception recoveryException)
			{
				Log.Warn("[RMP:QuickSL] Failed to recover to the main menu after a singleplayer reload error: " + recoveryException);
			}
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
			ShowRecoveryCover();
			await NGame.Instance.ReturnToMainMenu();
			if (!await HostBootstrapModule.StartQuickSlLoadedHostAsync())
			{
				recovery.AutoStartCancelled = true;
				HideRecoveryCover();
			}
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:QuickSL] Host reload failed: " + ex);
			if (_recovery != null)
			{
				_recovery.AutoStartCancelled = true;
			}
			HideRecoveryCover();
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
			ShowRecoveryCover();
			await NGame.Instance.ReturnToMainMenu();
			if (!SteamInitializer.Initialized)
			{
				Log.Warn("[RMP:QuickSL] Automatic reconnect requires Steam; falling back to the multiplayer menu.");
				HideRecoveryCover();
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
				HideRecoveryCover();
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
				HideRecoveryCover();
				NGame.Instance.MainMenu.OpenMultiplayerSubmenu();
				return;
			}
			await joinTask;
		}
		catch (Exception ex)
		{
			recovery.AutoStartCancelled = true;
			Log.Warn("[RMP:QuickSL] Automatic reconnect failed: " + ex.Message);
			HideRecoveryCover();
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
			HideRecoveryCover();
			return;
		}
		if (lobby.NetService.Type != NetGameType.Host || !recovery.IsHost)
		{
			return;
		}
		HideRecoveryCover();
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

	private static void ShowRecoveryCover()
	{
		if (_recoveryCover != null && GodotObject.IsInstanceValid(_recoveryCover))
		{
			return;
		}
		CanvasLayer layer = new CanvasLayer
		{
			Name = "RmpQuickSlRecoveryCover",
			Layer = 1000
		};
		ColorRect backdrop = new ColorRect
		{
			Name = "Backdrop",
			Color = Colors.Black,
			MouseFilter = Control.MouseFilterEnum.Stop,
			Position = Vector2.Zero,
			Size = NGame.Instance.GetViewport().GetVisibleRect().Size
		};
		layer.AddChild(backdrop);
		NGame.Instance.AddChild(layer);
		_recoveryCover = layer;
		Log.Info("[RMP:QuickSL] Recovery cover shown; intermediate main-menu frames are hidden.");
	}

	private static void HideRecoveryCover()
	{
		CanvasLayer? layer = _recoveryCover;
		_recoveryCover = null;
		if (layer != null && GodotObject.IsInstanceValid(layer))
		{
			layer.QueueFree();
			Log.Info("[RMP:QuickSL] Recovery cover hidden.");
		}
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
		if (_popupOpen || NModalContainer.Instance == null || NModalContainer.Instance.OpenModal != null)
		{
			return;
		}
		_popupOpen = true;
		TaskHelper.RunSafely(ShowPopupAsync(title, body, yes, no, showNo));
	}

	private static async Task ShowPopupAsync(string title, string body, Action yes, Action? no, bool showNo)
	{
		NGenericPopup? popup = null;
		try
		{
			NModalContainer modal = NModalContainer.Instance ?? throw new InvalidOperationException("The modal container is not available.");
			if (modal.OpenModal != null)
			{
				_popupOpen = false;
				return;
			}
			popup = NGenericPopup.Create();
			if (popup == null)
			{
				_popupOpen = false;
				return;
			}
			popup.Visible = false;
			popup.TreeExiting += () => _popupOpen = false;
			modal.Add(popup, showBackstop: false);
			Log.Info("[RMP:QuickSL] Confirmation popup mounted; waiting for native controls to become ready.");

			NVerticalPopup? verticalPopup = null;
			NPopupYesNoButton? yesButton = null;
			NPopupYesNoButton? noButton = null;
			int readyFrame;
			for (readyFrame = 1; readyFrame <= 10; readyFrame++)
			{
				await NGame.Instance.AwaitProcessFrame();
				if (!GodotObject.IsInstanceValid(popup))
				{
					throw new InvalidOperationException("The confirmation popup was released before initialization completed.");
				}
				verticalPopup = popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
				yesButton = verticalPopup?.GetNodeOrNull<NPopupYesNoButton>("YesButton");
				noButton = verticalPopup?.GetNodeOrNull<NPopupYesNoButton>("NoButton");
				if (popup.IsNodeReady() && verticalPopup?.IsNodeReady() == true && yesButton?.IsNodeReady() == true && noButton?.IsNodeReady() == true)
				{
					break;
				}
			}
			if (verticalPopup == null || yesButton?.IsNodeReady() != true || noButton?.IsNodeReady() != true || readyFrame > 10)
			{
				throw new TimeoutException("Native confirmation controls did not become ready within 10 frames.");
			}

			verticalPopup.SetText(title, body);
			verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), button =>
			{
				_popupOpen = false;
				Log.Info("[RMP:QuickSL] Confirmation accepted.");
				yes();
			});
			if (showNo)
			{
				verticalPopup.InitNoButton(new LocString("main_menu_ui", "GENERIC_POPUP.cancel"), button =>
				{
					_popupOpen = false;
					Log.Info("[RMP:QuickSL] Confirmation cancelled.");
					no?.Invoke();
				});
			}
			else
			{
				verticalPopup.HideNoButton();
			}
			popup.Visible = true;
			modal.ShowBackstop();
			Log.Info($"[RMP:QuickSL] Confirmation popup ready after {readyFrame} frame(s).");
		}
		catch (Exception ex)
		{
			_popupOpen = false;
			NModalContainer? modal = NModalContainer.Instance;
			if (popup != null && GodotObject.IsInstanceValid(popup) && ReferenceEquals(modal?.OpenModal, popup))
			{
				modal?.Clear();
			}
			Log.Warn("[RMP:QuickSL] Popup creation failed: " + ex);
		}
	}
}
