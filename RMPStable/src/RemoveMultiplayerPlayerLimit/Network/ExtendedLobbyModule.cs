using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

public class ExtendedLobbyModule : IRMPModule
{
	private sealed class HostJoinPatchState
	{
		public required StartRunLobby Lobby { get; init; }

		public required MessageHandlerDelegate<ClientLobbyJoinRequestMessage> OriginalJoinHandler { get; init; }

		public required MessageHandlerDelegate<ClientLobbyJoinRequestMessage> ReplacementJoinHandler { get; init; }
	}

	private sealed class ExtendedLobbyNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";

			public static readonly StringName PatchStandardScreen = "PatchStandardScreen";

			public static readonly StringName PatchCustomScreen = "PatchCustomScreen";

			public static readonly StringName PatchDailyScreen = "PatchDailyScreen";

			public static readonly StringName OnStandardEmbarkPressed = "OnStandardEmbarkPressed";

			public static readonly StringName OnStandardUnreadyPressed = "OnStandardUnreadyPressed";

			public static readonly StringName OnCustomEmbarkPressed = "OnCustomEmbarkPressed";

			public static readonly StringName OnCustomUnreadyPressed = "OnCustomUnreadyPressed";

			public static readonly StringName OnDailyEmbarkPressed = "OnDailyEmbarkPressed";

			public static readonly StringName OnDailyUnreadyPressed = "OnDailyUnreadyPressed";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly HashSet<ulong> _patchedStandardScreens = new HashSet<ulong>();

		private readonly HashSet<ulong> _patchedCustomScreens = new HashSet<ulong>();

		private readonly HashSet<ulong> _patchedDailyScreens = new HashSet<ulong>();

		private int _frameCounter;

		public ExtendedLobbyNode()
		{
			base.Name = "ExtendedLobbyNode";
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 5 == 0)
			{
				StartRunLobby startRunLobby = SceneMonitor.FindActiveStartRunLobby();
				if (startRunLobby != null && startRunLobby.NetService.Type == NetGameType.Host)
				{
					EnsureHostJoinPatch(startRunLobby);
				}
				PatchStandardScreen(SceneRegistry.Instance?.CharacterSelectScreen);
				PatchCustomScreen(SceneRegistry.Instance?.CustomRunScreen);
				PatchDailyScreen(SceneRegistry.Instance?.DailyRunScreen);
			}
		}

		private void PatchStandardScreen(NCharacterSelectScreen? screen)
		{
			NCharacterSelectScreen screen2 = screen;
			if (screen2 == null)
			{
				return;
			}
			ulong instanceId = screen2.GetInstanceId();
			if (!_patchedStandardScreens.Contains(instanceId))
			{
				ReplaceReleasedHandler(screen2.GetNode<NButton>("ConfirmButton"), screen2, "OnEmbarkPressed", delegate(NButton button)
				{
					OnStandardEmbarkPressed(screen2, button);
				});
				ReplaceReleasedHandler(screen2.GetNode<NButton>("UnreadyButton"), screen2, "OnUnreadyPressed", delegate(NButton button)
				{
					OnStandardUnreadyPressed(screen2, button);
				});
				_patchedStandardScreens.Add(instanceId);
			}
		}

		private void PatchCustomScreen(NCustomRunScreen? screen)
		{
			NCustomRunScreen screen2 = screen;
			if (screen2 == null)
			{
				return;
			}
			ulong instanceId = screen2.GetInstanceId();
			if (!_patchedCustomScreens.Contains(instanceId))
			{
				ReplaceReleasedHandler(screen2.GetNode<NButton>("ConfirmButton"), screen2, "OnEmbarkPressed", delegate(NButton button)
				{
					OnCustomEmbarkPressed(screen2, button);
				});
				ReplaceReleasedHandler(screen2.GetNode<NButton>("UnreadyButton"), screen2, "OnUnreadyPressed", delegate(NButton button)
				{
					OnCustomUnreadyPressed(screen2, button);
				});
				_patchedCustomScreens.Add(instanceId);
			}
		}

		private void PatchDailyScreen(NDailyRunScreen? screen)
		{
			NDailyRunScreen screen2 = screen;
			if (screen2 == null)
			{
				return;
			}
			ulong instanceId = screen2.GetInstanceId();
			if (!_patchedDailyScreens.Contains(instanceId))
			{
				ReplaceReleasedHandler(screen2.GetNode<NButton>("%ConfirmButton"), screen2, "OnEmbarkPressed", delegate(NButton button)
				{
					OnDailyEmbarkPressed(screen2, button);
				});
				ReplaceReleasedHandler(screen2.GetNode<NButton>("%UnreadyButton"), screen2, "OnUnreadyPressed", delegate(NButton button)
				{
					OnDailyUnreadyPressed(screen2, button);
				});
				_patchedDailyScreens.Add(instanceId);
			}
		}

		private void EnsureHostJoinPatch(StartRunLobby lobby)
		{
			StartRunLobby lobby2 = lobby;
			ulong hashCodeAsUlong = lobby2.GetHashCodeAsUlong();
			if (!HostJoinPatchStates.ContainsKey(hashCodeAsUlong) && !(StartRunHandleJoinMethod == null))
			{
				MessageHandlerDelegate<ClientLobbyJoinRequestMessage> originalHandler = (MessageHandlerDelegate<ClientLobbyJoinRequestMessage>)Delegate.CreateDelegate(typeof(MessageHandlerDelegate<ClientLobbyJoinRequestMessage>), lobby2, StartRunHandleJoinMethod);
				MessageHandlerDelegate<ClientLobbyJoinRequestMessage> messageHandlerDelegate = delegate(ClientLobbyJoinRequestMessage message, ulong senderId)
				{
					HandleExtendedJoinRequest(lobby2, originalHandler, message, senderId);
				};
				lobby2.NetService.UnregisterMessageHandler(originalHandler);
				lobby2.NetService.RegisterMessageHandler(messageHandlerDelegate);
				HostJoinPatchStates[hashCodeAsUlong] = new HostJoinPatchState
				{
					Lobby = lobby2,
					OriginalJoinHandler = originalHandler,
					ReplacementJoinHandler = messageHandlerDelegate
				};
			}
		}

		private static void HandleExtendedJoinRequest(StartRunLobby lobby, MessageHandlerDelegate<ClientLobbyJoinRequestMessage> originalHandler, ClientLobbyJoinRequestMessage message, ulong senderId)
		{
			RmpProtocol.MarkPeerAwaitingHandshake(senderId);
			int num = lobby.Players.Count + 1;
			if (!ShouldUseExtendedLobbyProtocol(lobby) || num <= 7)
			{
				originalHandler(message, senderId);
				return;
			}
			if (lobby.NetService.Type != NetGameType.Host || !(lobby.NetService is NetHostGameService netHostGameService))
			{
				throw new InvalidOperationException("Extended join request received as non-host.");
			}
			if (lobby.Players.Count >= lobby.MaxPlayers)
			{
				netHostGameService.DisconnectClient(senderId, NetError.LobbyFull);
				return;
			}
			try
			{
				LobbyPlayer? lobbyPlayer = (LobbyPlayer?)TryAddPlayerMethod?.Invoke(lobby, new object[3] { message.unlockState, message.maxAscensionUnlocked, senderId });
				if (!lobbyPlayer.HasValue)
				{
					netHostGameService.DisconnectClient(senderId, NetError.InternalError);
					return;
				}
				UpdateMaxAscensionMethod?.Invoke(lobby, null);
				ClientLobbyJoinResponseMessage clientLobbyJoinResponseMessage = default(ClientLobbyJoinResponseMessage);
				clientLobbyJoinResponseMessage.playersInLobby = BuildJoinResponsePlayers(lobby.Players, senderId);
				clientLobbyJoinResponseMessage.ascension = lobby.Ascension;
				clientLobbyJoinResponseMessage.dailyTime = lobby.DailyTime;
				clientLobbyJoinResponseMessage.seed = lobby.Seed;
				clientLobbyJoinResponseMessage.modifiers = lobby.Modifiers.Select((ModifierModel modifier) => modifier.ToSerializable()).ToList();
				ClientLobbyJoinResponseMessage message2 = clientLobbyJoinResponseMessage;
				netHostGameService.SendMessage(message2, senderId);
				netHostGameService.SetPeerReadyForBroadcasting(senderId);
				PlayerJoinedMessage playerJoinedMessage = default(PlayerJoinedMessage);
				playerJoinedMessage.lobbyPlayer = lobbyPlayer.Value;
				PlayerJoinedMessage message3 = playerJoinedMessage;
				foreach (LobbyPlayer player in lobby.Players)
				{
					if (player.id != lobby.NetService.NetId && player.id != senderId)
					{
						lobby.NetService.SendMessage(message3, player.id);
					}
				}
				RemoveConnectingPlayerMethod?.Invoke(lobby, new object[1] { senderId });
				lobby.LobbyListener.PlayerConnected(lobbyPlayer.Value);
				RmpProtocol.BroadcastLobbySnapshot(lobby.Players);
			}
			catch (Exception value)
			{
				netHostGameService.DisconnectClient(senderId, NetError.InternalError);
				Log.Error($"[RMP:ExtendedLobby] Failed to process extended join request: {value}");
			}
		}

		private static List<LobbyPlayer> BuildJoinResponsePlayers(IReadOnlyList<LobbyPlayer> players, ulong joiningPlayerId)
		{
			IReadOnlyList<LobbyPlayer> players2 = players;
			List<LobbyPlayer> list = players2.Take(7).ToList();
			if (list.Any((LobbyPlayer player) => player.id == joiningPlayerId))
			{
				return list;
			}
			LobbyPlayer lobbyPlayer = players2.First((LobbyPlayer player) => player.id == joiningPlayerId);
			if (list.Count == 7)
			{
				int num = list.FindLastIndex((LobbyPlayer player) => player.id != players2[0].id);
				if (num < 0)
				{
					num = list.Count - 1;
				}
				list[num] = lobbyPlayer;
			}
			else
			{
				list.Add(lobbyPlayer);
			}
			return list;
		}

		private static void ReplaceReleasedHandler(NButton button, object target, string originalMethodName, Action<NButton> replacement)
		{
			Callable callable = CreatePrivateReleasedCallable(target, originalMethodName);
			if (button.IsConnected(NClickableControl.SignalName.Released, callable))
			{
				button.Disconnect(NClickableControl.SignalName.Released, callable);
			}
			button.Connect(NClickableControl.SignalName.Released, Callable.From(replacement));
		}

		private static Callable CreatePrivateReleasedCallable(object target, string methodName)
		{
			System.Reflection.MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMethodException(target.GetType().FullName, methodName);
			return Callable.From((Action<NButton>)Delegate.CreateDelegate(typeof(Action<NButton>), target, method));
		}

		private static void OnStandardEmbarkPressed(NCharacterSelectScreen screen, NButton button)
		{
			NCharacterSelectScreen screen2 = screen;
			NButton button2 = button;
			StartRunLobby lobby = screen2.Lobby;
			if (!ShouldUseExtendedLobbyProtocol(lobby) || lobby.Players.Count <= 7)
			{
				InvokePrivateButtonMethod(screen2, "OnEmbarkPressed", button2);
				return;
			}
			if (!SaveManager.Instance.SeenFtue("accept_tutorials_ftue"))
			{
				if (NModalContainer.Instance != null)
				{
					NAcceptTutorialsFtue nAcceptTutorialsFtue = NAcceptTutorialsFtue.Create(screen2, delegate
					{
						OnStandardEmbarkPressed(screen2, button2);
					});
					if (nAcceptTutorialsFtue != null)
					{
						NModalContainer.Instance.Add(nAcceptTutorialsFtue);
					}
				}
				return;
			}
			screen2.GetNode<NButton>("ConfirmButton").Disable();
			screen2.GetNode<NButton>("BackButton").Disable();
			lobby.Act1 = screen2.GetNode<NActDropdown>("%ActDropdown").CurrentOption;
			SetLocalReady(screen2, lobby, ready: true);
			foreach (NCharacterSelectButton item in screen2.GetNode<Control>("CharSelectButtons/ButtonContainer").GetChildren().OfType<NCharacterSelectButton>())
			{
				item.Disable();
			}
			if (!lobby.Players.All((LobbyPlayer player) => player.isReady))
			{
				screen2.GetNode<Control>("ReadyAndWaitingPanel").Visible = true;
				screen2.GetNode<NButton>("UnreadyButton").Enable();
			}
		}

		private static void OnStandardUnreadyPressed(NCharacterSelectScreen screen, NButton button)
		{
			StartRunLobby lobby = screen.Lobby;
			if (!ShouldUseExtendedLobbyProtocol(lobby) || lobby.Players.Count <= 7)
			{
				InvokePrivateButtonMethod(screen, "OnUnreadyPressed", button);
				return;
			}
			screen.GetNode<NButton>("ConfirmButton").Enable();
			screen.GetNode<NButton>("BackButton").Enable();
			screen.GetNode<NButton>("UnreadyButton").Disable();
			screen.GetNode<Control>("ReadyAndWaitingPanel").Visible = false;
			foreach (NCharacterSelectButton item in screen.GetNode<Control>("CharSelectButtons/ButtonContainer").GetChildren().OfType<NCharacterSelectButton>())
			{
				item.Enable();
			}
			SetLocalReady(screen, lobby, ready: false);
		}

		private static void OnCustomEmbarkPressed(NCustomRunScreen screen, NButton button)
		{
			StartRunLobby lobby = screen.Lobby;
			if (!ShouldUseExtendedLobbyProtocol(lobby) || lobby.Players.Count <= 7)
			{
				InvokePrivateButtonMethod(screen, "OnEmbarkPressed", button);
				return;
			}
			screen.GetNode<NButton>("ConfirmButton").Disable();
			screen.GetNode<NButton>("BackButton").Disable();
			foreach (NCharacterSelectButton item in screen.GetNode<Control>("LeftContainer/CharSelectButtons/ButtonContainer").GetChildren().OfType<NCharacterSelectButton>())
			{
				item.Disable();
			}
			SetLocalReady(screen, lobby, ready: true);
			if (!lobby.Players.All((LobbyPlayer player) => player.isReady))
			{
				screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = true;
				screen.GetNode<NButton>("UnreadyButton").Enable();
			}
		}

		private static void OnCustomUnreadyPressed(NCustomRunScreen screen, NButton button)
		{
			StartRunLobby lobby = screen.Lobby;
			if (!ShouldUseExtendedLobbyProtocol(lobby) || lobby.Players.Count <= 7)
			{
				InvokePrivateButtonMethod(screen, "OnUnreadyPressed", button);
				return;
			}
			screen.GetNode<NButton>("ConfirmButton").Enable();
			screen.GetNode<NButton>("BackButton").Enable();
			screen.GetNode<NButton>("UnreadyButton").Disable();
			screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = false;
			foreach (NCharacterSelectButton item in screen.GetNode<Control>("LeftContainer/CharSelectButtons/ButtonContainer").GetChildren().OfType<NCharacterSelectButton>())
			{
				item.Enable();
			}
			SetLocalReady(screen, lobby, ready: false);
		}

		private static void OnDailyEmbarkPressed(NDailyRunScreen screen, NButton button)
		{
			if (!(DailyRunLobbyField?.GetValue(screen) is StartRunLobby startRunLobby))
			{
				return;
			}
			if (!ShouldUseExtendedLobbyProtocol(startRunLobby) || startRunLobby.Players.Count <= 7)
			{
				InvokePrivateButtonMethod(screen, "OnEmbarkPressed", button);
				return;
			}
			screen.GetNode<NButton>("%ConfirmButton").Disable();
			screen.GetNode<NButton>("%BackButton").Disable();
			SetLocalReady(screen, startRunLobby, ready: true);
			if (!startRunLobby.Players.All((LobbyPlayer player) => player.isReady))
			{
				screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = true;
				screen.GetNode<NButton>("%UnreadyButton").Enable();
			}
		}

		private static void OnDailyUnreadyPressed(NDailyRunScreen screen, NButton button)
		{
			if (DailyRunLobbyField?.GetValue(screen) is StartRunLobby startRunLobby)
			{
				if (!ShouldUseExtendedLobbyProtocol(startRunLobby) || startRunLobby.Players.Count <= 7)
				{
					InvokePrivateButtonMethod(screen, "OnUnreadyPressed", button);
					return;
				}
				screen.GetNode<NButton>("%ConfirmButton").Enable();
				screen.GetNode<NButton>("%BackButton").Enable();
				screen.GetNode<NButton>("%UnreadyButton").Disable();
				screen.GetNode<Control>("%ReadyAndWaitingPanel").Visible = false;
				SetLocalReady(screen, startRunLobby, ready: false);
			}
		}

		private static void SetLocalReady(Node screen, StartRunLobby lobby, bool ready)
		{
			if (TrySetPlayerReadyState(lobby, lobby.NetService.NetId, ready, out var updatedPlayer))
			{
				NotifyPlayerChanged(lobby, updatedPlayer, isRandomCharacterResolution: false);
				if (lobby.NetService.Type == NetGameType.Host)
				{
					RmpProtocol.BroadcastExtendedReady(ready);
					TryBeginExtendedRun(lobby);
				}
				else
				{
					lobby.NetService.SendMessage(new RmpExtendedReadyStateMessage
					{
						Ready = ready
					});
				}
			}
		}

		private static void InvokePrivateButtonMethod(object target, string methodName, NButton button)
		{
			(target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new MissingMethodException(target.GetType().FullName, methodName)).Invoke(target, new object[1] { button });
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(10)
			{
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.PatchStandardScreen, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.PatchCustomScreen, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.PatchDailyScreen, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnStandardEmbarkPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "button", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnStandardUnreadyPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "button", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnCustomEmbarkPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "button", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnCustomUnreadyPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "button", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnDailyEmbarkPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "button", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnDailyUnreadyPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "button", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName._Process && args.Count == 1)
			{
				_Process(VariantUtils.ConvertTo<double>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.PatchStandardScreen && args.Count == 1)
			{
				PatchStandardScreen(VariantUtils.ConvertTo<NCharacterSelectScreen>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.PatchCustomScreen && args.Count == 1)
			{
				PatchCustomScreen(VariantUtils.ConvertTo<NCustomRunScreen>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.PatchDailyScreen && args.Count == 1)
			{
				PatchDailyScreen(VariantUtils.ConvertTo<NDailyRunScreen>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnStandardEmbarkPressed && args.Count == 2)
			{
				OnStandardEmbarkPressed(VariantUtils.ConvertTo<NCharacterSelectScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnStandardUnreadyPressed && args.Count == 2)
			{
				OnStandardUnreadyPressed(VariantUtils.ConvertTo<NCharacterSelectScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnCustomEmbarkPressed && args.Count == 2)
			{
				OnCustomEmbarkPressed(VariantUtils.ConvertTo<NCustomRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnCustomUnreadyPressed && args.Count == 2)
			{
				OnCustomUnreadyPressed(VariantUtils.ConvertTo<NCustomRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnDailyEmbarkPressed && args.Count == 2)
			{
				OnDailyEmbarkPressed(VariantUtils.ConvertTo<NDailyRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnDailyUnreadyPressed && args.Count == 2)
			{
				OnDailyUnreadyPressed(VariantUtils.ConvertTo<NDailyRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName.OnStandardEmbarkPressed && args.Count == 2)
			{
				OnStandardEmbarkPressed(VariantUtils.ConvertTo<NCharacterSelectScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnStandardUnreadyPressed && args.Count == 2)
			{
				OnStandardUnreadyPressed(VariantUtils.ConvertTo<NCharacterSelectScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnCustomEmbarkPressed && args.Count == 2)
			{
				OnCustomEmbarkPressed(VariantUtils.ConvertTo<NCustomRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnCustomUnreadyPressed && args.Count == 2)
			{
				OnCustomUnreadyPressed(VariantUtils.ConvertTo<NCustomRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnDailyEmbarkPressed && args.Count == 2)
			{
				OnDailyEmbarkPressed(VariantUtils.ConvertTo<NDailyRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnDailyUnreadyPressed && args.Count == 2)
			{
				OnDailyUnreadyPressed(VariantUtils.ConvertTo<NDailyRunScreen>(in args[0]), VariantUtils.ConvertTo<NButton>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			ret = default(godot_variant);
			return false;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if (method == MethodName._Process)
			{
				return true;
			}
			if (method == MethodName.PatchStandardScreen)
			{
				return true;
			}
			if (method == MethodName.PatchCustomScreen)
			{
				return true;
			}
			if (method == MethodName.PatchDailyScreen)
			{
				return true;
			}
			if (method == MethodName.OnStandardEmbarkPressed)
			{
				return true;
			}
			if (method == MethodName.OnStandardUnreadyPressed)
			{
				return true;
			}
			if (method == MethodName.OnCustomEmbarkPressed)
			{
				return true;
			}
			if (method == MethodName.OnCustomUnreadyPressed)
			{
				return true;
			}
			if (method == MethodName.OnDailyEmbarkPressed)
			{
				return true;
			}
			if (method == MethodName.OnDailyUnreadyPressed)
			{
				return true;
			}
			return base.HasGodotClassMethod(in method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if (name == PropertyName._frameCounter)
			{
				_frameCounter = VariantUtils.ConvertTo<int>(in value);
				return true;
			}
			return base.SetGodotClassPropertyValue(in name, in value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			if (name == PropertyName._frameCounter)
			{
				value = VariantUtils.CreateFrom(in _frameCounter);
				return true;
			}
			return base.GetGodotClassPropertyValue(in name, out value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.PropertyInfo> GetGodotPropertyList()
		{
			return new List<Godot.Bridge.PropertyInfo>
			{
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
		}
	}

	private static readonly System.Reflection.MethodInfo? TryAddPlayerMethod = typeof(StartRunLobby).GetMethod("TryAddPlayerInFirstAvailableSlot", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly System.Reflection.MethodInfo? UpdateMaxAscensionMethod = typeof(StartRunLobby).GetMethod("UpdateMaxMultiplayerAscension", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly System.Reflection.MethodInfo? RemoveConnectingPlayerMethod = typeof(StartRunLobby).GetMethod("RemoveConnectingPlayer", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly System.Reflection.MethodInfo? StartRunHandleJoinMethod = typeof(StartRunLobby).GetMethod("HandleClientLobbyJoinRequestMessage", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly System.Reflection.MethodInfo? GetRandomActListMethod = typeof(ActModel).GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault((System.Reflection.MethodInfo method) => method.Name == "GetRandomList" && method.GetParameters().Length == 3);

	private static readonly System.Reflection.MethodInfo? GenericActMethod = typeof(ModelDb).GetMethod("Act", BindingFlags.Static | BindingFlags.Public);

	private static readonly FieldInfo? DailyRunLobbyField = typeof(NDailyRunScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly HashSet<ulong> ExtendedRunStartingLobbyIds = new HashSet<ulong>();

	private static readonly Dictionary<ulong, HostJoinPatchState> HostJoinPatchStates = new Dictionary<ulong, HostJoinPatchState>();

	public string Name => "ExtendedLobby";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
	}

	public Node? CreateNode()
	{
		return new ExtendedLobbyNode();
	}

	public void Cleanup()
	{
		HostJoinPatchStates.Clear();
		ExtendedRunStartingLobbyIds.Clear();
	}

	internal static bool ShouldUseExtendedLobbyProtocol(StartRunLobby lobby)
	{
		return lobby.NetService.Type.IsMultiplayer();
	}

	internal static bool TrySetPlayerReadyState(StartRunLobby lobby, ulong playerId, bool ready, out LobbyPlayer updatedPlayer)
	{
		int num = lobby.Players.FindIndex((LobbyPlayer player) => player.id == playerId);
		if (num < 0)
		{
			updatedPlayer = default(LobbyPlayer);
			return false;
		}
		updatedPlayer = lobby.Players[num];
		updatedPlayer.isReady = ready;
		lobby.Players[num] = updatedPlayer;
		return true;
	}

	internal static List<ActModel> BuildActsForBeginRun(string seed, string act1, StartRunLobby lobby, IReadOnlyList<LobbyPlayer> players)
	{
		UnlockState unlockState = new UnlockState(players.Select((LobbyPlayer player) => UnlockState.FromSerializable(player.unlockState)));
		Rng rng = new Rng((uint)StringHelper.GetDeterministicHashCode(seed));
		List<ActModel> list = InvokeGetRandomActList(seed, rng, unlockState, lobby.NetService.Type.IsMultiplayer());
		ActModel act2 = GetAct(act1);
		if (act2 != null)
		{
			list[0] = act2;
		}
		return list;
	}

	internal static void NotifyPlayerChanged(StartRunLobby lobby, LobbyPlayer player, bool isRandomCharacterResolution)
	{
		System.Reflection.MethodInfo method = lobby.LobbyListener.GetType().GetMethod("PlayerChanged");
		if (!(method == null))
		{
			if (method.GetParameters().Length >= 2)
			{
				method.Invoke(lobby.LobbyListener, new object[2] { player, isRandomCharacterResolution });
			}
			else
			{
				method.Invoke(lobby.LobbyListener, new object[1] { player });
			}
		}
	}

	internal static bool TryBeginExtendedRun(StartRunLobby lobby)
	{
		if (!ShouldUseExtendedLobbyProtocol(lobby) || lobby.Players.Count <= 7 || lobby.NetService.Type != NetGameType.Host)
		{
			return false;
		}
		if (lobby.Players.Count <= 1 || lobby.Players.Any((LobbyPlayer player) => !player.isReady))
		{
			return false;
		}
		ulong hashCodeAsUlong = lobby.GetHashCodeAsUlong();
		if (!ExtendedRunStartingLobbyIds.Add(hashCodeAsUlong))
		{
			return false;
		}
		try
		{
			string seed = NGame.Instance?.DebugSeedOverride ?? (string.IsNullOrWhiteSpace(lobby.Seed) ? SeedHelper.GetRandomSeed() : SeedHelper.CanonicalizeSeed(lobby.Seed));
			NormalizeRandomCharacters(lobby, seed);
			List<ModifierModel> modifiers = lobby.Modifiers.ToList();
			List<ActModel> acts = BuildActsForBeginRun(seed, lobby.Act1, lobby, lobby.Players);
			RmpProtocol.BroadcastExtendedBeginRun(lobby.Players, seed, lobby.Act1, modifiers);
			lobby.LobbyListener.BeginRun(seed, acts, modifiers);
			if (lobby.NetService is NetHostGameService netHostGameService)
			{
				netHostGameService.NetHost?.SetHostIsClosed(isClosed: true);
			}
			Log.Info($"[RMP:ExtendedLobby] Started extended multiplayer run with {lobby.Players.Count} players.");
			return true;
		}
		finally
		{
			ExtendedRunStartingLobbyIds.Remove(hashCodeAsUlong);
		}
	}

	private static void NormalizeRandomCharacters(StartRunLobby lobby, string seed)
	{
		Rng rng = new Rng((uint)StringHelper.GetDeterministicHashCode(seed));
		for (int i = 0; i < lobby.Players.Count; i++)
		{
			LobbyPlayer lobbyPlayer = lobby.Players[i];
			if (lobbyPlayer.character is RandomCharacter)
			{
				lobbyPlayer.character = rng.NextItem(ModelDb.AllCharacters) ?? ModelDb.AllCharacters.First();
				lobby.Players[i] = lobbyPlayer;
				NotifyPlayerChanged(lobby, lobbyPlayer, isRandomCharacterResolution: true);
			}
		}
	}

	private static ActModel? GetAct(string act1Key)
	{
		string text = ((act1Key == "overgrowth") ? "MegaCrit.Sts2.Core.Models.Acts.Overgrowth" : ((!(act1Key == "underdocks")) ? null : "MegaCrit.Sts2.Core.Models.Acts.Underdocks"));
		if (text == null || GenericActMethod == null)
		{
			return null;
		}
		Type type = typeof(ActModel).Assembly.GetType(text);
		if (type == null)
		{
			return null;
		}
		return GenericActMethod.MakeGenericMethod(type).Invoke(null, null) as ActModel;
	}

	private static List<ActModel> InvokeGetRandomActList(string seed, Rng rng, UnlockState unlockState, bool isMultiplayer)
	{
		if (GetRandomActListMethod == null)
		{
			throw new InvalidOperationException("ActModel.GetRandomList method was not found.");
		}
		return ((IEnumerable<ActModel>)((GetRandomActListMethod.GetParameters()[0].ParameterType == typeof(string)) ? GetRandomActListMethod.Invoke(null, new object[3] { seed, unlockState, isMultiplayer }) : GetRandomActListMethod.Invoke(null, new object[3] { rng, unlockState, isMultiplayer }))).ToList();
	}
}
