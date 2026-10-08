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
        var writer = new PacketWriter { WarnOnGrow = false }; writer.Write(value);
        return writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
    }
    internal static T Unpack<T>(byte[] bytes) where T : IPacketSerializable, new()
    { var reader = new PacketReader(); reader.Reset(bytes); return reader.Read<T>(); }
    internal static string Hash(RunState state)
    {
        var saved = RunManager.Instance.ToSave(null);
        saved.SaveTime = saved.StartTime = saved.RunTime = saved.WinTime = 0;
        saved.NumReloads = 0; saved.MapDrawings = null;
        // Hover tips mark models as seen in each isolated profile. Those
        // presentation discoveries can differ without changing the run.
        foreach (var player in saved.Players)
        {
            player.DiscoveredCards.Clear(); player.DiscoveredEnemies.Clear(); player.DiscoveredEpochs.Clear();
            player.DiscoveredPotions.Clear(); player.DiscoveredRelics.Clear();
        }
        // The engine keeps the last combat's phase/energy/RNG objects after
        // leaving it. A fresh noncombat checkpoint has no such transient state.
        var combat = state.CurrentRoom is MegaCrit.Sts2.Core.Rooms.CombatRoom ? Pack(NetFullCombatState.FromRun(state, null)) : Array.Empty<byte>();
        var run = Pack(saved);
        var bytes = new byte[combat.Length + run.Length];
        combat.CopyTo(bytes, 0); run.CopyTo(bytes, combat.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    internal static string Build => typeof(RunManager).Assembly.ManifestModule.ModuleVersionId + ":" + typeof(MirrorState).Assembly.ManifestModule.ModuleVersionId + ":" + ModelIdSerializationCache.Hash;
    internal static string DrawingHash() => MegaCrit.Sts2.Core.Nodes.NRun.Instance is { } run ?
        Convert.ToHexString(SHA256.HashData(Pack(run.GlobalUi.MapScreen.Drawings.GetSerializableMapDrawings()))) : "";
}
