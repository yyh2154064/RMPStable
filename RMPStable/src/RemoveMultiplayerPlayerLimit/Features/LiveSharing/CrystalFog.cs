using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Saves;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Presentation only: the original mask still owns its cell fades, and the
// original cells retain every hitbox, highlight and hidden/reward decision.
internal static class CrystalFog
{
    private static readonly ConditionalWeakTable<NCrystalSphereMask, ShaderMaterial> Materials = new();
    private static readonly FieldInfo MaterialField = typeof(NCrystalSphereMask).GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static bool _rendererEnabled;
    internal static bool Enabled => MirrorRenderer.IsRenderer ? _rendererEnabled : SpectatorPreferences.Current.CrystalFogPreview;

    internal static void Initialize()
    {
        var harmony = new Harmony("RMP.CrystalFog");
        var postfix = new HarmonyMethod(typeof(CrystalFog).GetMethod(nameof(Apply), BindingFlags.Static | BindingFlags.NonPublic)!);
        harmony.Patch(typeof(NCrystalSphereMask).GetMethod("_Ready")!, postfix: postfix);
        harmony.Patch(typeof(NCrystalSphereMask).GetMethod("_Process")!, postfix: postfix);
        harmony.Patch(typeof(NGame).GetMethod("_Input")!, prefix: new HarmonyMethod(typeof(CrystalFog).GetMethod(nameof(OnInput), BindingFlags.Static | BindingFlags.NonPublic)!));
    }

    internal static void ProcessFrame()
    {
        if (MirrorRenderer.IsRenderer) return;
        SpectatorPreferences.LoadProfile(LiveSharingController.Suspend);
    }

    private static bool OnInput(InputEvent __0)
    {
        if (MirrorRenderer.IsRenderer || __0 is not InputEventKey key || key.Keycode != Key.F4) return true;
        NGame.Instance.GetViewport().SetInputAsHandled();
        if (!key.Pressed || key.Echo || SaveManager.Instance?.IsProfileInitialized != true) return false;
        SpectatorPreferences.LoadProfile(LiveSharingController.Suspend);
        Toggle();
        return false;
    }

    internal static void Toggle()
    {
        if (MirrorRenderer.IsRenderer || SaveManager.Instance?.IsProfileInitialized != true) return;
        SpectatorPreferences.Current.CrystalFogPreview = !Enabled;
        SpectatorPreferences.Save();
        Log.Info("[RMP:CrystalFog] F4 preview=" + Enabled);
    }

    internal static void SetRendererPreference(bool enabled) => _rendererEnabled = enabled;

    private static void Apply(NCrystalSphereMask __instance)
    {
        if (!Materials.TryGetValue(__instance, out var material))
        {
            if (!Enabled || __instance.Material is not ShaderMaterial original) return;
            // Clone per screen so cached scenes and all other users of the
            // native shader remain untouched. UpdateMat must use this same clone.
            string code = original.Shader.Code;
            int fragment = code.IndexOf("void fragment", StringComparison.Ordinal);
            int open = fragment < 0 ? -1 : code.IndexOf('{', fragment);
            int end = open + 1, depth = 1;
            if (open < 0) throw new InvalidOperationException("Crystal Sphere mask fragment missing");
            for (; end < code.Length && depth > 0; end++)
            {
                if (code[end] == '{') depth++;
                else if (code[end] == '}') depth--;
            }
            if (depth != 0) throw new InvalidOperationException("Crystal Sphere mask fragment incomplete");
            // Keep the native revealed-cell contour fully visible. Fading the
            // whole output would also fade that contour and blur the distinction.
            code = code.Insert(end - 1, "\nif (rmp_crystal_preview) { COLOR.rgb = mix(vec3(0.52), borderColor, border); COLOR.a = max(alpha * 0.16, border * 0.95) * lastmask * ModularCol.a; }\n");
            int declaration = code.IndexOf(';');
            code = code.Insert(declaration + 1, "\nuniform bool rmp_crystal_preview = false;\n");
            material = (ShaderMaterial)original.Duplicate();
            material.Shader = new Shader { Code = code };
            __instance.Material = material;
            MaterialField.SetValue(__instance, material);
            Materials.Add(__instance, material);
        }
        material.SetShaderParameter("rmp_crystal_preview", Enabled);
    }
}
