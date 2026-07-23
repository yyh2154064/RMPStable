using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RemoveMultiplayerPlayerLimit.Network;

public struct RmpExtendedReadyStateMessage : INetMessage, IPacketSerializable
{
	public bool Ready;

	public readonly bool ShouldBroadcast => true;

	public readonly bool ShouldBuffer => true;

	public readonly NetTransferMode Mode => NetTransferMode.Reliable;

	public readonly LogLevel LogLevel => LogLevel.Debug;

	public readonly void Serialize(PacketWriter writer)
	{
		writer.WriteBool(Ready);
	}

	public void Deserialize(PacketReader reader)
	{
		Ready = reader.ReadBool();
	}

	public override readonly string ToString()
	{
		return $"RmpExtendedReady(ready={Ready})";
	}
}
