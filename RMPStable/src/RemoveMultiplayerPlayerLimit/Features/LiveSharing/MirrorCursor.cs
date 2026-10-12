using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Native system cursors bypass Godot's canvas stretch. Apply that stretch to
// their bitmap and hotspot in the replica, including the Help/inspect cursor.
internal static class MirrorCursor
{
    private static readonly Dictionary<Input.CursorShape, (Resource Image, Vector2 Hotspot)> Original = new();
    private static readonly Dictionary<ulong, Image> Scaled = new();
    private static float _scale = 1;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal static void Initialize()
    {
        new Harmony("RMPStable.NativeReplicaCursorScale").Patch(typeof(Input).GetMethod(nameof(Input.SetCustomMouseCursor))!,
            prefix: new HarmonyMethod(typeof(MirrorCursor).GetMethod(nameof(ScaleCursor), BindingFlags.Static | BindingFlags.NonPublic)!));
    }
    private static void ScaleCursor(ref Resource __0, Input.CursorShape __1, ref Vector2 __2)
    {
        if (!MirrorRenderer.IsRenderer || DisplayServer.GetName() == "headless" || __0 is not Image original) return;
        Original[__1] = (original, __2);
        if (!Scaled.TryGetValue(original.GetInstanceId(), out var scaled))
        {
            scaled = (Image)original.Duplicate();
            scaled.Resize(Math.Clamp((int)Math.Round(original.GetWidth() * _scale), 1, 256),
                Math.Clamp((int)Math.Round(original.GetHeight() * _scale), 1, 256), Image.Interpolation.Lanczos);
            Scaled[original.GetInstanceId()] = scaled;
        }
        __2 *= new Vector2(scaled.GetWidth() / (float)original.GetWidth(), scaled.GetHeight() / (float)original.GetHeight());
        __0 = scaled;
    }
    internal static void Tick()
    {
        if (DisplayServer.GetName() == "headless" || NGame.Instance == null) return;
        float scale = NGame.Instance.GetViewport().GetFinalTransform().Scale.X;
        if (!float.IsFinite(scale) || scale <= 0 || Math.Abs(scale - _scale) < .001f) return;
        _scale = scale;
        foreach (var image in Scaled.Values) image.Dispose(); Scaled.Clear();
        // CursorManager can have entered the tree before mod initialization.
        if (LocalSpectatorSource.Descendants<NCursorManager>(NGame.Instance).FirstOrDefault() is { } cursor)
        {
            if (!Original.ContainsKey(Input.CursorShape.Help) && typeof(NCursorManager).GetField("_cursorInspect", Any)?.GetValue(cursor) is Resource inspect)
                Original[Input.CursorShape.Help] = (inspect, new Vector2(12, 12));
            if (!Original.ContainsKey(Input.CursorShape.Arrow) && typeof(NCursorManager).GetProperty("CursorNotTilted", Any)?.GetValue(cursor) is Resource arrow)
                Original[Input.CursorShape.Arrow] = (arrow, (Vector2)typeof(NCursorManager).GetProperty("HotSpot", Any)!.GetValue(cursor)!);
        }
        foreach (var pair in Original.ToArray()) Input.SetCustomMouseCursor(pair.Value.Image, pair.Key, pair.Value.Hotspot);
    }
}
