using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Unlocks;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpLobbyPlayerState : IPacketSerializable
{
	public ulong id;

	public int slotId;

	public CharacterModel character;

	public SerializableUnlockState unlockState;

	public int maxMultiplayerAscensionUnlocked;

	public bool isReady;

	public readonly LobbyPlayer ToLobbyPlayer()
	{
		LobbyPlayer result = default(LobbyPlayer);
		result.id = id;
		result.slotId = slotId;
		result.character = character;
		result.unlockState = unlockState;
		result.maxMultiplayerAscensionUnlocked = maxMultiplayerAscensionUnlocked;
		result.isReady = isReady;
		return result;
	}

	public static RmpLobbyPlayerState FromLobbyPlayer(LobbyPlayer lobbyPlayer)
	{
		RmpLobbyPlayerState result = default(RmpLobbyPlayerState);
		result.id = lobbyPlayer.id;
		result.slotId = lobbyPlayer.slotId;
		result.character = lobbyPlayer.character;
		result.unlockState = lobbyPlayer.unlockState;
		result.maxMultiplayerAscensionUnlocked = lobbyPlayer.maxMultiplayerAscensionUnlocked;
		result.isReady = lobbyPlayer.isReady;
		return result;
	}

	public readonly void Serialize(PacketWriter writer)
	{
		writer.WriteULong(id);
		writer.WriteInt(slotId, 4);
		writer.WriteModel(character);
		writer.Write(unlockState);
		writer.WriteInt(maxMultiplayerAscensionUnlocked);
		writer.WriteBool(isReady);
	}

	public void Deserialize(PacketReader reader)
	{
		id = reader.ReadULong();
		slotId = reader.ReadInt(4);
		character = reader.ReadModel<CharacterModel>();
		unlockState = reader.Read<SerializableUnlockState>();
		maxMultiplayerAscensionUnlocked = reader.ReadInt();
		isReady = reader.ReadBool();
	}
}
