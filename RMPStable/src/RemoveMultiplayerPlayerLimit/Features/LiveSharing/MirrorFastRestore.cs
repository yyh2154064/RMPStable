using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Skip presentation delays only while reconstructing a saved checkpoint.
// Native hooks, draws, choices, RNG and action execution still run normally.
internal static class MirrorFastRestore
{
    internal static bool Source, Replica;
    private static bool Active => Source || Replica;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    internal static void Initialize()
    {
        var harmony = new Harmony("RMPStable.NativeCheckpointWarmRestore");
        foreach (var method in typeof(Cmd).GetMethods().Where(m => m.Name is "Wait" or "CustomScaledWait"))
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(MirrorFastRestore).GetMethod(nameof(Wait), Any)));
        foreach (var name in new[] { "TweenProperty", "TweenMethod" })
            harmony.Patch(typeof(Tween).GetMethod(name)!, prefix: new HarmonyMethod(typeof(MirrorFastRestore).GetMethod(nameof(TweenDuration), Any)));
        harmony.Patch(typeof(Tween).GetMethod("TweenInterval")!, prefix: new HarmonyMethod(typeof(MirrorFastRestore).GetMethod(nameof(Interval), Any)));
        foreach (var type in new[] { typeof(PropertyTweener), typeof(MethodTweener), typeof(CallbackTweener) })
            harmony.Patch(type.GetMethod("SetDelay")!, prefix: new HarmonyMethod(typeof(MirrorFastRestore).GetMethod(nameof(Delay), Any)));
    }
    private static bool Wait(ref Task __result, object[] __args)
    {
        if (!Active) return true;
        foreach (var argument in __args)
            if (argument is CancellationToken token && token.IsCancellationRequested) { __result = Task.FromCanceled(token); return false; }
        __result = Task.CompletedTask; return false;
    }
    private static void TweenDuration(ref double __3) { if (Active) __3 = System.Math.Min(__3, .005); }
    private static void Interval(ref double __0) { if (Active) __0 = System.Math.Min(__0, .005); }
    private static void Delay(ref double __0) { if (Active) __0 = System.Math.Min(__0, .005); }
}
