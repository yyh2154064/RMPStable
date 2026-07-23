using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpExtendedBeginRunMessage : INetMessage, IPacketSerializable
{
	public List<RmpLobbyPlayerState>? players;

	public string seed;

	public string act1;

	public List<SerializableModifier> modifiers;

	public readonly bool ShouldBroadcast => false;

	public readonly bool ShouldBuffer => true;

	public readonly NetTransferMode Mode => NetTransferMode.Reliable;

	public readonly LogLevel LogLevel => LogLevel.Info;

	public readonly void Serialize(PacketWriter writer)
	{
		if (players == null)
		{
			throw new InvalidOperationException("players must not be null");
		}
		writer.WriteList(players, 5);
		writer.WriteString(seed);
		writer.WriteString(act1);
		writer.WriteList(modifiers);
	}

	public void Deserialize(PacketReader reader)
	{
		players = reader.ReadList<RmpLobbyPlayerState>(5);
		seed = reader.ReadString();
		act1 = reader.ReadString();
		modifiers = reader.ReadList<SerializableModifier>();
	}

	public override readonly string ToString()
	{
		return $"RmpExtendedBeginRun(players={players?.Count ?? 0}, seed={seed})";
	}
}
