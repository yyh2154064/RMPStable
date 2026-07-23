using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport.Steam;
using Steamworks;

namespace RemoveMultiplayerPlayerLimit.Network;

internal static class SteamLobbyHelper
{
	internal static bool TryGetLobbyId(INetGameService netService, out ulong lobbyId)
	{
		try
		{
			if (netService is NetHostGameService { NetHost: SteamHost { LobbyId: var id } } && id.HasValue)
			{
				lobbyId = id.Value.m_SteamID;
				return true;
			}
		}
		catch { }
		lobbyId = 0;
		return false;
	}

	internal static bool TryUpdateMemberLimit(INetGameService netService, int limit)
	{
		try
		{
			if (!(netService is NetHostGameService netHostGameService))
			{
				return false;
			}
			if (!(netHostGameService.NetHost is SteamHost { LobbyId: var lobbyId }))
			{
				return false;
			}
			if (!lobbyId.HasValue)
			{
				return false;
			}
			bool num = SteamMatchmaking.SetLobbyMemberLimit(lobbyId.Value, limit);
			if (num)
			{
				Log.Info($"[RMP] Steam lobby member limit set to {limit} (lobby={lobbyId.Value.m_SteamID})");
			}
			else
			{
				Log.Warn($"[RMP] SteamMatchmaking.SetLobbyMemberLimit({limit}) returned false");
			}
			return num;
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP] Failed to update Steam lobby limit: " + ex.Message);
			return false;
		}
	}

	internal static int GetCurrentMemberLimit(INetGameService netService)
	{
		try
		{
			if (!(netService is NetHostGameService netHostGameService))
			{
				return -1;
			}
			if (!(netHostGameService.NetHost is SteamHost { LobbyId: var lobbyId }))
			{
				return -1;
			}
			if (!lobbyId.HasValue)
			{
				return -1;
			}
			return SteamMatchmaking.GetLobbyMemberLimit(lobbyId.Value);
		}
		catch
		{
			return -1;
		}
	}
}
