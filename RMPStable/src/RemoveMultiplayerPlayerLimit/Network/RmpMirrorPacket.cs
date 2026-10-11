using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
namespace RemoveMultiplayerPlayerLimit.Network;
// Room routing never carries local pipe credentials.
public struct RmpMirrorPacket : INetMessage, IPacketSerializable
{
    public ulong Origin, Destination;
    public string Token;
    public int Transfer, Part, Parts;
    public byte[] Data;
    public readonly bool ShouldBroadcast => false;
    public readonly bool ShouldBuffer => false;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly void Serialize(PacketWriter w)
    {
        w.WriteULong(Origin); w.WriteULong(Destination); w.WriteString(Token);
        w.WriteInt(Transfer); w.WriteInt(Part); w.WriteInt(Parts);
        w.WriteInt(Data.Length); w.WriteBytes(Data, Data.Length);
    }
    public void Deserialize(PacketReader r)
    {
        Origin = r.ReadULong(); Destination = r.ReadULong(); Token = r.ReadString();
        Transfer = r.ReadInt(); Part = r.ReadInt(); Parts = r.ReadInt(); int n = r.ReadInt();
        if (Token.Length != 32 || Transfer <= 0 || Parts < 1 || Parts > 1024 || Part < 0 || Part >= Parts || n < 1 || n > 32768)
            throw new InvalidOperationException("Invalid multiplayer mirror fragment");
        Data = new byte[n]; r.ReadBytes(Data, n);
    }
}
