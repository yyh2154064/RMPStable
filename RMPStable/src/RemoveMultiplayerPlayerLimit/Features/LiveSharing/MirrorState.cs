using System;
using System.Reflection;
using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static class MirrorState
{
    private static readonly FieldInfo? Replay = typeof(CombatReplayWriter).GetField("_replay", BindingFlags.Instance | BindingFlags.NonPublic);
    internal static CombatReplay? CurrentReplay => RunManager.Instance.CombatReplayWriter is { } writer ? Replay?.GetValue(writer) as CombatReplay : null;
    internal static byte[] Pack<T>(T value) where T : IPacketSerializable
    {
        var writer = new PacketWriter(); writer.Write(value);
        return writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
    }
    internal static T Unpack<T>(byte[] bytes) where T : IPacketSerializable, new()
    { var reader = new PacketReader(); reader.Reset(bytes); return reader.Read<T>(); }
    internal static string Hash(RunState state) => Convert.ToHexString(SHA256.HashData(Pack(NetFullCombatState.FromRun(state, null))));
    internal static string Build => typeof(RunManager).Assembly.ManifestModule.ModuleVersionId + ":" + typeof(MirrorState).Assembly.ManifestModule.ModuleVersionId + ":" + ModelIdSerializationCache.Hash;
}
