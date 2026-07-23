using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpSyncRequestMessage : INetMessage, IPacketSerializable
{
	public readonly bool ShouldBroadcast => false;

	public readonly bool ShouldBuffer => true;

	public readonly NetTransferMode Mode => NetTransferMode.Reliable;

	public readonly LogLevel LogLevel => LogLevel.Debug;

	public readonly void Serialize(PacketWriter writer)
	{
	}

	public void Deserialize(PacketReader reader)
	{
	}

	public override readonly string ToString()
	{
		return "RmpSyncRequest";
	}
}
