using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class RestTargetView
{
    public float[] From { get; set; } = Array.Empty<float>();
    public float[] To { get; set; } = Array.Empty<float>();
    public string Hovered { get; set; } = "";
}

internal static class MultiplayerPresentation042
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    internal static List<ParticipantSnapshot> Names = new();
    internal static RestTargetView? RestTarget;
    private static bool _showingTarget;
    internal static void Initialize(Harmony h)
    {
        // Keep every native heal/hook/VFX and the original choice ID. Only the
        // replica's Mend decision uses the native remote-choice branch.
        var machine = typeof(MendRestSiteOption).GetMethod("OnSelect")!.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        h.Patch(machine.GetMethod("MoveNext", Any)!, transpiler: new HarmonyMethod(typeof(MultiplayerPresentation042).GetMethod(nameof(MendRemote), Any)));
        h.Patch(typeof(PlatformUtil).GetMethod("GetPlayerNameRaw")!, postfix: new HarmonyMethod(typeof(MultiplayerPresentation042).GetMethod(nameof(PlayerName), Any)));
        foreach (var pair in new[] { ("BeginLine", nameof(DrawingBegin)), ("UpdateCurrentLinePosition", nameof(DrawingMove)), ("StopDrawingLine", nameof(DrawingEnd)), ("SetDrawingMode", nameof(DrawingModeChanged)) })
            h.Patch(typeof(NMapDrawings).GetMethod(pair.Item1, Any)!, prefix: new HarmonyMethod(typeof(MultiplayerPresentation042).GetMethod(pair.Item2, Any)));
    }
    private static IEnumerable<CodeInstruction> MendRemote(IEnumerable<CodeInstruction> code)
    {
        var original = typeof(LocalContext).GetMethods().Single(m => m.Name == "IsMe" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Player));
        foreach (var instruction in code)
        {
            if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = typeof(MultiplayerPresentation042).GetMethod(nameof(MendLocal), Any)!; }
            yield return instruction;
        }
    }
    private static bool MendLocal(Player player) => !MultiplayerNoncombat.Replica && LocalContext.IsMe(player);
    private static string Point(Vector2 point) => point.X.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + point.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static void DrawingBegin(NMapDrawings __instance, object __0, Vector2 __1, DrawingMode? __2)
        => Record(__0, "drawStart", (int)(__2 ?? __instance.GetDrawingMode((ulong)MultiplayerPersonalUi.Read(__0, "playerId")!)) + "," + Point(MapDrawing042.ToNet(__instance, __1)));
    private static void DrawingMove(NMapDrawings __instance, object __0, Vector2 __1) => Record(__0, "drawMove", Point(MapDrawing042.ToNet(__instance, __1)));
    private static void DrawingEnd(object __0) => Record(__0, "drawEnd", "");
    private static void DrawingModeChanged(object __0, DrawingMode __1) => Record(__0, "drawMode", ((int)__1).ToString());
    private static void Record(object drawingState, string kind, string value)
        => MultiplayerNoncombat.RecordDrawing((ulong)MultiplayerPersonalUi.Read(drawingState, "playerId")!, kind, value);
    private static void PlayerName(ulong __1, ref string __result)
    {
        if (MirrorRenderer.Active?.Multiplayer == true && Names.FirstOrDefault(p => p.Id == __1.ToString()) is { Name.Length: > 0 } name)
            __result = name.Name;
    }
    internal static RestTargetView? CaptureTarget()
    {
        if (NRun.Instance?.RestSiteRoom == null || NTargetManager.Instance is not { IsInSelection: true } manager) return null;
        if (MultiplayerPersonalUi.Read(manager, "_targetingArrow") is not NTargetingArrow arrow) return null;
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        var start = (Vector2)typeof(NTargetingArrow).GetProperty("From", Any)!.GetValue(arrow)! / size;
        var end = NGame.Instance.GetViewport().GetMousePosition() / size;
        return new() { From = new[] { start.X, start.Y }, To = new[] { end.X, end.Y },
            Hovered = typeof(NTargetManager).GetProperty("HoveredNode", Any)!.GetValue(manager) is Node hovered ? MirrorNativeUi.Key(hovered) : "" };
    }
    internal static void Apply(RestTargetView? target)
    {
        if (NRun.Instance?.GlobalUi?.TargetManager is not { } manager || MultiplayerPersonalUi.Read(manager, "_targetingArrow") is not NTargetingArrow arrow) return;
        RestTarget = target;
        if (target?.From.Length != 2 || target.To.Length != 2 || NRun.Instance?.RestSiteRoom == null || MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen == true)
        {
            if (_showingTarget) arrow.StopDrawing();
            _showingTarget = false; return;
        }
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        if (!_showingTarget) arrow.StartDrawingFrom(new Vector2(target.From[0], target.From[1]) * size, true);
        _showingTarget = true;
        // Controller-style native arrow updates from the source, never the
        // observer's mouse. No local target manager/choice task is opened.
        arrow.UpdateDrawingTo(new Vector2(target.To[0], target.To[1]) * size);
    }
    internal static void Reset() { Names.Clear(); RestTarget = null; _showingTarget = false; }
}
