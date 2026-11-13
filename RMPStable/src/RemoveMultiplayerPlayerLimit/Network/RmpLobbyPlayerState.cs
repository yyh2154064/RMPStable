using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Unlocks;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpLobbyPlayerState : IPacketSerializable
{
	internal const int SlotBits = 4;
	public ulong id;

	public int slotId;

	public CharacterModel character;

	public SerializableUnlockState unlockState;

	public int maxMultiplayerAscensionUnlocked;
#if STS2_0113
	public int maxBlitzAscension;
	public bool isRotatingDread;
#endif

	public bool isReady;

#if STS2_0111
	public bool isModded;
#endif

	public readonly LobbyPlayer ToLobbyPlayer()
	{
		LobbyPlayer result = default(LobbyPlayer);
		result.id = id;
		result.slotId = slotId;
		result.character = character;
		result.unlockState = unlockState;
#if STS2_0113
		result.maxStandardAscension = maxMultiplayerAscensionUnlocked;
		result.maxBlitzAscension = maxBlitzAscension;
		result.isRotatingDread = isRotatingDread;
#else
		result.maxMultiplayerAscensionUnlocked = maxMultiplayerAscensionUnlocked;
#endif
		result.isReady = isReady;
#if STS2_0111
		result.isModded = isModded;
#endif
		return result;
	}

	public static RmpLobbyPlayerState FromLobbyPlayer(LobbyPlayer lobbyPlayer)
	{
		RmpLobbyPlayerState result = default(RmpLobbyPlayerState);
		result.id = lobbyPlayer.id;
		result.slotId = lobbyPlayer.slotId;
		result.character = lobbyPlayer.character;
		result.unlockState = lobbyPlayer.unlockState;
#if STS2_0113
		result.maxMultiplayerAscensionUnlocked = lobbyPlayer.maxStandardAscension;
		result.maxBlitzAscension = lobbyPlayer.maxBlitzAscension;
		result.isRotatingDread = lobbyPlayer.isRotatingDread;
#else
		result.maxMultiplayerAscensionUnlocked = lobbyPlayer.maxMultiplayerAscensionUnlocked;
#endif
		result.isReady = lobbyPlayer.isReady;
#if STS2_0111
		result.isModded = lobbyPlayer.isModded;
#endif
		return result;
	}

	public readonly void Serialize(PacketWriter writer)
	{
		writer.WriteULong(id);
		writer.WriteInt(slotId, SlotBits);
		writer.WriteModel(character);
		writer.Write(unlockState);
		writer.WriteInt(maxMultiplayerAscensionUnlocked);
#if STS2_0113
		writer.WriteInt(maxBlitzAscension);
		writer.WriteBool(isRotatingDread);
#endif
		writer.WriteBool(isReady);
#if STS2_0111
		writer.WriteBool(isModded);
#endif
	}

	public void Deserialize(PacketReader reader)
	{
		id = reader.ReadULong();
		slotId = reader.ReadInt(SlotBits);
		character = reader.ReadModel<CharacterModel>();
		unlockState = reader.Read<SerializableUnlockState>();
		maxMultiplayerAscensionUnlocked = reader.ReadInt();
#if STS2_0113
		maxBlitzAscension = reader.ReadInt();
		isRotatingDread = reader.ReadBool();
#endif
		isReady = reader.ReadBool();
#if STS2_0111
		isModded = reader.ReadBool();
#endif
	}
}
