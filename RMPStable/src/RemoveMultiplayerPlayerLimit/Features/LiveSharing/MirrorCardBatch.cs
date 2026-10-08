using System.Collections.Generic;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MirrorCardBatch : IPacketSerializable
{
    internal List<SerializableCard> Cards = new();
    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(Cards.Count, 16);
        foreach (var card in Cards) writer.Write(card);
    }
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadInt(16);
        for (int i = 0; i < count; i++) Cards.Add(reader.Read<SerializableCard>());
    }
}
