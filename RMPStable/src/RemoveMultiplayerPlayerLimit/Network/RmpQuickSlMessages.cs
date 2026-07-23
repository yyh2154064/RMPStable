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
	public ulong OperationId;
	public ulong HostId;
	public ulong PreviousLobbyId;
	public readonly bool ShouldBroadcast => true;
	public readonly bool ShouldBuffer => false;
	public readonly NetTransferMode Mode => NetTransferMode.Reliable;
	public readonly LogLevel LogLevel => LogLevel.Info;
	public readonly void Serialize(PacketWriter writer)
	{
		writer.WriteULong(OperationId);
		writer.WriteULong(HostId);
		writer.WriteULong(PreviousLobbyId);
	}
	public void Deserialize(PacketReader reader)
	{
		OperationId = reader.ReadULong();
		HostId = reader.ReadULong();
		PreviousLobbyId = reader.ReadULong();
	}
}
