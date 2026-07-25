using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpQuickSlRequestMessage : INetMessage, IPacketSerializable
{
	public readonly bool ShouldBroadcast => false;
	public readonly bool ShouldBuffer => false;
	public readonly NetTransferMode Mode => NetTransferMode.Reliable;
	public readonly LogLevel LogLevel => LogLevel.Info;
	public readonly void Serialize(PacketWriter writer) { }
	public void Deserialize(PacketReader reader) { }
}

public struct RmpQuickSlDecisionMessage : INetMessage, IPacketSerializable
{
	public bool Accepted;
	public readonly bool ShouldBroadcast => false;
	public readonly bool ShouldBuffer => false;
	public readonly NetTransferMode Mode => NetTransferMode.Reliable;
	public readonly LogLevel LogLevel => LogLevel.Info;
	public readonly void Serialize(PacketWriter writer) => writer.WriteBool(Accepted);
	public void Deserialize(PacketReader reader) => Accepted = reader.ReadBool();
}

public struct RmpQuickSlBeginMessage : INetMessage, IPacketSerializable
{
	private const int PlayerCountBits = 5;
	private const int MaxPlayerCount = 16;

	public ulong OperationId;
	public ulong HostId;
	public ulong PreviousLobbyId;
	public List<ulong>? PlayerIds;
	public readonly bool ShouldBroadcast => true;
	public readonly bool ShouldBuffer => false;
	public readonly NetTransferMode Mode => NetTransferMode.Reliable;
	public readonly LogLevel LogLevel => LogLevel.Info;
	public readonly void Serialize(PacketWriter writer)
	{
		writer.WriteULong(OperationId);
		writer.WriteULong(HostId);
		writer.WriteULong(PreviousLobbyId);
		int count = PlayerIds == null ? 0 : System.Math.Min(PlayerIds.Count, MaxPlayerCount);
		writer.WriteInt(count, PlayerCountBits);
		for (int i = 0; i < count; i++)
		{
			writer.WriteULong(PlayerIds![i]);
		}
	}
	public void Deserialize(PacketReader reader)
	{
		OperationId = reader.ReadULong();
		HostId = reader.ReadULong();
		PreviousLobbyId = reader.ReadULong();
		int count = reader.ReadInt(PlayerCountBits);
		if (count < 0 || count > MaxPlayerCount)
		{
			throw new System.InvalidOperationException($"Quick SL player count {count} is outside the supported range.");
		}
		PlayerIds = new List<ulong>(count);
		for (int i = 0; i < count; i++)
		{
			PlayerIds.Add(reader.ReadULong());
		}
	}
}
