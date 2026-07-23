using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

public static class RmpProtocol
{
	public const int ProtocolVersion = 3;

	private static INetGameService? _netService;

	private static readonly HashSet<ulong> ReadyPeerIds = new HashSet<ulong>();

	private static readonly FieldInfo? RemoteCursorSynchronizerField = typeof(NRemoteMouseCursorContainer).GetField("_synchronizer", BindingFlags.Instance | BindingFlags.NonPublic);

	private static bool _awaitingInitialSync;

	private static bool _didWarnAboutRemoteCursorReflection;

	public static bool IsActive => _netService != null;

	public static void Bind(INetGameService netService)
	{
		if (ReferenceEquals(_netService, netService))
		{
			return;
		}
		Unbind();
		_netService = netService;
		netService.RegisterMessageHandler<RmpConfigSyncMessage>(HandleConfigSync);
		netService.RegisterMessageHandler<RmpLobbySnapshotMessage>(HandleLobbySnapshot);
		netService.RegisterMessageHandler<RmpExtendedReadyStateMessage>(HandleExtendedReadyState);
		netService.RegisterMessageHandler<RmpExtendedBeginRunMessage>(HandleExtendedBeginRun);
		netService.RegisterMessageHandler<RmpSyncRequestMessage>(HandleSyncRequest);
		_awaitingInitialSync = netService.Type == NetGameType.Client;
		Log.Info($"[RMP] Protocol v{ProtocolVersion} bound to {netService.Type} (NetId={netService.NetId})");
		RequestInitialSync();
	}

	public static void Unbind()
	{
		if (_netService != null)
		{
			try
			{
				_netService.UnregisterMessageHandler<RmpConfigSyncMessage>(HandleConfigSync);
				_netService.UnregisterMessageHandler<RmpLobbySnapshotMessage>(HandleLobbySnapshot);
				_netService.UnregisterMessageHandler<RmpExtendedReadyStateMessage>(HandleExtendedReadyState);
				_netService.UnregisterMessageHandler<RmpExtendedBeginRunMessage>(HandleExtendedBeginRun);
				_netService.UnregisterMessageHandler<RmpSyncRequestMessage>(HandleSyncRequest);
			}
			catch
			{
			}
			_netService = null;
		}
		ReadyPeerIds.Clear();
		_awaitingInitialSync = false;
	}

	public static void RequestInitialSync()
	{
		if (_netService != null && _netService.Type == NetGameType.Client && _awaitingInitialSync)
		{
			_netService.SendMessage(new RmpSyncRequestMessage());
		}
	}

	public static void MarkPeerAwaitingHandshake(ulong peerId)
	{
		ReadyPeerIds.Remove(peerId);
	}

	public static void BroadcastLobbySnapshot(IReadOnlyList<LobbyPlayer> players)
	{
		if (_netService != null && _netService.Type == NetGameType.Host)
		{
			RmpLobbySnapshotMessage message = new RmpLobbySnapshotMessage
			{
				players = players.Select(RmpLobbyPlayerState.FromLobbyPlayer).ToList()
			};
			foreach (LobbyPlayer player in players)
			{
				if (player.id != _netService.NetId && ReadyPeerIds.Contains(player.id))
				{
					_netService.SendMessage(message, player.id);
				}
			}
		}
	}

	public static void BroadcastExtendedReady(bool ready)
	{
		if (_netService != null && _netService.Type == NetGameType.Host)
		{
			_netService.SendMessage(new RmpExtendedReadyStateMessage
			{
				Ready = ready
			});
		}
	}

	public static void BroadcastExtendedBeginRun(IReadOnlyList<LobbyPlayer> players, string seed, string act1, IReadOnlyList<ModifierModel> modifiers)
	{
		if (_netService != null && _netService.Type == NetGameType.Host)
		{
			_netService.SendMessage(new RmpExtendedBeginRunMessage
			{
				players = players.Select(RmpLobbyPlayerState.FromLobbyPlayer).ToList(),
				seed = seed,
				act1 = act1,
				modifiers = modifiers.Select((ModifierModel modifier) => modifier.ToSerializable()).ToList()
			});
		}
	}

	private static void HandleConfigSync(RmpConfigSyncMessage message, ulong senderId)
	{
		_awaitingInitialSync = false;
		if (message.ProtocolVersion != ProtocolVersion)
		{
			Log.Warn($"[RMP] Protocol version mismatch: local={ProtocolVersion}, remote={message.ProtocolVersion} from {senderId}");
		}
		Log.Info($"[RMP] Config sync from {senderId}: v{message.ProtocolVersion}, maxPlayers={message.MaxPlayerLimit} (local fixed at {16})");
	}

	private static void HandleSyncRequest(RmpSyncRequestMessage message, ulong senderId)
	{
		if (_netService == null || _netService.Type != NetGameType.Host)
		{
			return;
		}
		ReadyPeerIds.Add(senderId);
		_netService.SendMessage(new RmpConfigSyncMessage
		{
			ProtocolVersion = ProtocolVersion,
			MaxPlayerLimit = 16
		}, senderId);
		StartRunLobby startRunLobby = SceneMonitor.FindActiveStartRunLobby();
		if (startRunLobby != null)
		{
			_netService.SendMessage(new RmpLobbySnapshotMessage
			{
				players = startRunLobby.Players.Select(RmpLobbyPlayerState.FromLobbyPlayer).ToList()
			}, senderId);
		}
		Log.Info($"[RMP] Initial sync completed for peer {senderId}.");
	}

	private static void HandleLobbySnapshot(RmpLobbySnapshotMessage message, ulong senderId)
	{
		if (message.players != null)
		{
			StartRunLobby startRunLobby = SceneMonitor.FindActiveStartRunLobby();
			if (startRunLobby != null)
			{
				ApplyLobbySnapshot(startRunLobby, message.players);
			}
		}
	}

	private static void HandleExtendedReadyState(RmpExtendedReadyStateMessage message, ulong senderId)
	{
		StartRunLobby startRunLobby = SceneMonitor.FindActiveStartRunLobby();
		if (startRunLobby != null && ExtendedLobbyModule.ShouldUseExtendedLobbyProtocol(startRunLobby))
		{
			if (ExtendedLobbyModule.TrySetPlayerReadyState(startRunLobby, senderId, message.Ready, out var updatedPlayer))
			{
				ExtendedLobbyModule.NotifyPlayerChanged(startRunLobby, updatedPlayer, isRandomCharacterResolution: false);
			}
			if (startRunLobby.NetService.Type == NetGameType.Host)
			{
				ExtendedLobbyModule.TryBeginExtendedRun(startRunLobby);
			}
		}
	}

	private static void HandleExtendedBeginRun(RmpExtendedBeginRunMessage message, ulong senderId)
	{
		if (message.players == null)
		{
			return;
		}
		StartRunLobby startRunLobby = SceneMonitor.FindActiveStartRunLobby();
		if (startRunLobby != null)
		{
			ApplyLobbySnapshot(startRunLobby, message.players);
			List<ModifierModel> modifiers = message.modifiers.Select(ModifierModel.FromSerializable).ToList();
			List<ActModel> acts = ExtendedLobbyModule.BuildActsForBeginRun(message.seed, message.act1, startRunLobby, message.players.Select((RmpLobbyPlayerState player) => player.ToLobbyPlayer()).ToList());
			startRunLobby.LobbyListener.BeginRun(message.seed, acts, modifiers);
		}
	}

	private static void ApplyLobbySnapshot(StartRunLobby lobby, IReadOnlyList<RmpLobbyPlayerState> snapshotPlayers)
	{
		Dictionary<ulong, LobbyPlayer> dictionary = lobby.Players.ToDictionary((LobbyPlayer player) => player.id);
		List<LobbyPlayer> list = snapshotPlayers.Select((RmpLobbyPlayerState player) => player.ToLobbyPlayer()).ToList();
		HashSet<ulong> hashSet = list.Select((LobbyPlayer player) => player.id).ToHashSet();
		for (int num = lobby.Players.Count - 1; num >= 0; num--)
		{
			LobbyPlayer player2 = lobby.Players[num];
			if (!hashSet.Contains(player2.id))
			{
				lobby.Players.RemoveAt(num);
				if (player2.id != lobby.NetService.NetId)
				{
					lobby.LobbyListener.RemotePlayerDisconnected(player2);
				}
			}
		}
		foreach (LobbyPlayer snapshotPlayer in list)
		{
			if (!dictionary.TryGetValue(snapshotPlayer.id, out var value))
			{
				lobby.Players.Add(snapshotPlayer);
				if (snapshotPlayer.id != lobby.NetService.NetId)
				{
					lobby.LobbyListener.PlayerConnected(snapshotPlayer);
				}
			}
			else if (!LobbyPlayersEqual(value, snapshotPlayer))
			{
				int num2 = lobby.Players.FindIndex((LobbyPlayer player) => player.id == snapshotPlayer.id);
				if (num2 >= 0)
				{
					lobby.Players[num2] = snapshotPlayer;
				}
				ExtendedLobbyModule.NotifyPlayerChanged(lobby, snapshotPlayer, isRandomCharacterResolution: false);
			}
		}
		EnsureRemoteCursorContainerInitialized(lobby);
	}

	private static void EnsureRemoteCursorContainerInitialized(StartRunLobby lobby)
	{
		NRemoteMouseCursorContainer remoteCursorContainer = NGame.Instance?.RemoteCursorContainer;
		if (remoteCursorContainer == null)
		{
			return;
		}
		if (RemoteCursorSynchronizerField == null)
		{
			if (!_didWarnAboutRemoteCursorReflection)
			{
				_didWarnAboutRemoteCursorReflection = true;
				Log.Warn("[RMP] Could not inspect the remote cursor synchronizer; preserving the vanilla cursor binding.");
			}
			return;
		}
		try
		{
			PeerInputSynchronizer peerInputSynchronizer = RemoteCursorSynchronizerField.GetValue(remoteCursorContainer) as PeerInputSynchronizer;
			if (ReferenceEquals(peerInputSynchronizer, lobby.InputSynchronizer))
			{
				return;
			}
			remoteCursorContainer.Initialize(lobby.InputSynchronizer, lobby.Players.Select((LobbyPlayer player) => player.id));
		}
		catch (Exception value)
		{
			Log.Warn($"[RMP] Failed to initialize the remote cursor container safely: {value.Message}");
		}
	}

	private static bool LobbyPlayersEqual(LobbyPlayer a, LobbyPlayer b)
	{
		if (a.id == b.id && a.slotId == b.slotId && a.character == b.character && a.maxMultiplayerAscensionUnlocked == b.maxMultiplayerAscensionUnlocked)
		{
			return a.isReady == b.isReady;
		}
		return false;
	}
}
