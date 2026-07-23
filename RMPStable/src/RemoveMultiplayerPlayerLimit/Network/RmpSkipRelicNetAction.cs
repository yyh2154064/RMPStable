using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace RemoveMultiplayerPlayerLimit.Network;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct RmpSkipRelicNetAction : INetAction, IPacketSerializable
{
	public readonly GameAction ToGameAction(Player player)
	{
		return new RmpSkipRelicGameAction(player);
	}

	public readonly void Serialize(PacketWriter writer)
	{
	}

	public void Deserialize(PacketReader reader)
	{
	}

	public override readonly string ToString()
	{
		return "RmpSkipRelicNetAction";
	}
}
