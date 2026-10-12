using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private MultiplayerControlView? _battleAuthority;
    internal long LastMultiplayerResult { get; private set; }
    internal bool LastMultiplayerAccepted { get; private set; }
    private sealed record PendingPotion(PotionModel Potion, Creature? Target, long Generation, long Epoch, int Turn, ulong Created);
    private PendingPotion? _pendingPotion;
    private sealed record PendingChoice(ControlActionSnapshot Action, long Generation, long Epoch, uint Choice, ulong Created);
    private PendingChoice? _pendingChoice;
    private uint _pressedChoiceId;
    private Player? ViewPlayer => Multiplayer ? LocalContext.GetMe(_state) : _state?.Players[0];
    private System.Collections.Generic.List<Creature> PotionTargets => CombatManager.Instance.IsInProgress && ViewPlayer?.Creature.CombatState is { } combat ?
        combat.Creatures.ToList() : _state?.Players.Select(p => p.Creature).ToList() ?? new();
    private bool InputIdle => Multiplayer ? MultiplayerInputReady : NativeIdle;
    private bool MultiplayerInputReady => _control && !_busy && _sceneReady && !_multiplayerRoomWaiting && _generation == _desiredGeneration &&
        _battleAuthority?.Granted == true && _battleAuthority.Epoch == _epoch && _personalMessage?.Events <= _events && _error.Length == 0;
    private List<ControlActionSnapshot> InputActions() => Multiplayer ? _battleAuthority?.Actions ?? new() : _commands.CaptureCommands(_state!).Control.Actions;
    private void ApplyBattleAuthority(MirrorMessage personal)
    {
        _battleAuthority = personal.Authority;
        _authorityReady = _battleAuthority?.Granted == true && _battleAuthority.Epoch == _epoch;
        _authorityEvents = personal.Events;
    }
    private void SendMultiplayerIntent(string kind, int index, int targetIndex, string nodeKey)
    {
        if (!MultiplayerInputReady || _waitingForAuthority || _battleAuthority == null) return;
        var action = _battleAuthority.Actions.FirstOrDefault(a => a.Enabled && a.Kind == kind &&
            (kind is "play" or "potion" ? a.NativeIndex == index : kind == "endTurn" || a.NativePath == nodeKey));
        if (action == null) return;
        if (kind == "play" && (ViewPlayer?.PlayerCombatState?.Hand.Cards.ElementAtOrDefault(index) is not { } card ||
            action.CardId != Convert.ToBase64String(MirrorState.Pack(card.ToSerializable())))) return;
        if (kind == "potion" && ViewPlayer?.Potions.ElementAtOrDefault(index)?.Id.ToString() != action.CardId) return;
        var target = _battleAuthority.Targets.FirstOrDefault(t => t.NativeIndex == targetIndex);
        if (action.RequiresTarget && (target == null || !action.TargetIds.Contains(target.Id))) return;
        _waitingForAuthority = true; _submittedEvents = _events;
        _ = _wire.Send(new MirrorMessage { Kind = "intent", Multiplayer = true, Generation = _generation, Epoch = _epoch, Request = ++_request,
            Model = kind, NodeKey = action.Id, Value = _battleAuthority.Context, TargetKey = target?.Id ?? "", Events = _events });
    }
    private NClickableControl? _potionPressed;
    private bool ProcessMultiplayerChoiceInput(uint message, Vector2 point)
    {
        if (!Multiplayer || !_control || !_sceneReady || _personalMessage?.Choice == null) return false;
        if (UiOnlyOpen || NGame.Instance.InspectCardScreen?.IsVisibleInTree() == true || NGame.Instance.InspectRelicScreen?.IsVisibleInTree() == true ||
            MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance?.CurrentCapstoneScreen != null) return false;
        if (message == MirrorWin32.LeftDown)
        {
            var action = InputActions().LastOrDefault(a => a.Enabled && a.Kind is "select" or "choice" &&
                MirrorNativeUi.Resolve(a.NativePath) is Control node && Contains(InteractiveArea(node), point));
            if (action == null) return false;
            if (Diagnostic) GD.Print("[RMP:Mirror:Input] multiplayer choice press " + action.Id + " path=" + action.NativePath);
            CancelDrag(); _pressedAction = action; _pressedChoiceId = _personalMessage.Choice.Id; return true;
        }
        if (message != MirrorWin32.LeftUp || _pressedAction is not { Kind: "select" or "choice" } selected) return false;
        _pressedAction = null;
        if (Diagnostic) GD.Print("[RMP:Mirror:Input] multiplayer choice release " + selected.Id + " sameChoice=" + (_pressedChoiceId == _personalMessage.Choice.Id));
        if (_pressedChoiceId == _personalMessage.Choice.Id && MirrorNativeUi.Resolve(selected.NativePath) is Control released && Contains(InteractiveArea(released), point))
            _pendingChoice = new(selected, _generation, _epoch, _pressedChoiceId, Time.GetTicksMsec());
        return true;
    }
    private void ProcessPendingChoice()
    {
        if (_pendingChoice is not { } pending) return;
        if (!_control || pending.Generation != _generation || pending.Epoch != _epoch || _personalMessage?.Choice?.Id != pending.Choice ||
            Time.GetTicksMsec() - pending.Created > 8000)
        { _pendingChoice = null; return; }
        if (!MultiplayerInputReady || _waitingForAuthority) return;
        _pendingChoice = null;
        if (Diagnostic) GD.Print("[RMP:Mirror:Input] multiplayer choice ready " + pending.Action.Id);
        if (_battleAuthority?.Actions.Any(a => a.Id == pending.Action.Id && a.NativePath == pending.Action.NativePath && a.Enabled) == true)
            SendMultiplayerIntent(pending.Action.Kind, -1, -1, pending.Action.NativePath);
    }
    private void ProcessPendingPotion()
    {
        if (_pendingPotion is not { } pending) return;
        if (!_control || pending.Generation != _generation || pending.Epoch != _epoch || (ViewPlayer?.PlayerCombatState?.TurnNumber ?? 0) != pending.Turn ||
            Time.GetTicksMsec() - pending.Created > 8000 || ViewPlayer?.Potions.Contains(pending.Potion) != true || pending.Target is { IsAlive: false })
        { _pendingPotion = null; return; }
        if (!MultiplayerInputReady || _waitingForAuthority) return;
        int index = ViewPlayer!.Potions.ToList().IndexOf(pending.Potion);
        int target = pending.Target == null ? -1 : PotionTargets.IndexOf(pending.Target);
        if (_battleAuthority?.Actions.Any(a => a.Kind == "potion" && a.NativeIndex == index && a.Enabled) != true) return;
        _pendingPotion = null;
        SendMultiplayerIntent("potion", index, target, "");
    }
    private bool ProcessMultiplayerPotion(uint message, Vector2 point)
    {
        if (!Multiplayer || !_sceneReady) return false;
        if (NTargetManager.Instance?.IsInSelection == true && _dragCard == null)
        {
            if (!_control) { NTargetManager.Instance.CancelTargeting(); return true; }
            var hit = Hit("target", point);
            var target = hit == null ? null : PotionTargets.ElementAtOrDefault(hit.Index);
            Node? node = target == null ? null : MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(target);
            node ??= NRun.Instance == null ? null : LocalSpectatorSource.Descendants<MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState>(NRun.Instance)
                .LastOrDefault(p => p.Player.Creature.IsAlive && Contains(p.Hitbox, point));
            if (node != null && !NTargetManager.Instance.AllowedToTargetNode(node)) node = null;
            if (typeof(NTargetManager).GetProperty("HoveredNode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(NTargetManager.Instance) is Node previous && previous != node)
                NTargetManager.Instance.OnNodeUnhovered(previous);
            if (node != null) NTargetManager.Instance.OnNodeHovered(node);
            if (message == MirrorWin32.LeftUp)
                NTargetManager.Instance._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = ViewportPoint(point) });
            if (message == 0x204) NTargetManager.Instance.CancelTargeting();
            return true;
        }
        var popup = PotionPopup;
        if (popup != null && message == MirrorWin32.LeftDown && !Contains(popup, point)) { popup.Remove(); return true; }
        NClickableControl? button = popup != null ? MultiplayerPersonalUi.Read(popup, "_useButton") as NClickableControl :
            NRun.Instance == null ? null : LocalSpectatorSource.Descendants<NPotionHolder>(NRun.Instance.GlobalUi.TopBar).LastOrDefault(h => h.HasPotion && h.IsEnabled && Contains(h, point));
        if (button != null && (!button.IsEnabled || !Contains(button, point))) button = null;
        if (message == MirrorWin32.LeftDown && button != null)
        { if (MultiplayerInputReady && !_waitingForAuthority) _potionPressed = button; return true; }
        if (message == MirrorWin32.LeftUp && _potionPressed != null)
        {
            var pressed = _potionPressed; _potionPressed = null;
            if (MultiplayerInputReady && !_waitingForAuthority && GodotObject.IsInstanceValid(pressed) && Contains(pressed, point)) pressed.ForceClick();
            return true;
        }
        return popup != null;
    }
    internal static void InstallMultiplayerPotionGate()
    {
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var h = new Harmony("RMPStable.MultiplayerPotionControl");
        h.Patch(typeof(PotionModel).GetMethod("EnqueueManualUse")!, new HarmonyMethod(typeof(MirrorRenderer).GetMethod(nameof(ReplicaPotionUse), flags)));
        h.Patch(typeof(NPotionPopup).GetMethod("OnDiscardButtonPressed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!, new HarmonyMethod(typeof(MirrorRenderer).GetMethod(nameof(ReplicaPotionDiscard), flags)));
    }
    private static bool ReplicaPotionDiscard() => Active?.Multiplayer != true;
    private static bool ReplicaPotionUse(PotionModel __instance, Creature? target)
    {
        if (Active is not { Multiplayer: true } renderer) return true;
        int index = renderer.ViewPlayer?.Potions.ToList().IndexOf(__instance) ?? -1;
        // Native noncombat AnyPlayer potions are drunk, not thrown; the holder
        // passes null and EnqueueManualUse ordinarily resolves it to the owner.
        if (target == null && __instance.IsValidTarget(__instance.Owner.Creature)) target = __instance.Owner.Creature;
        if (renderer._control && index >= 0)
            renderer._pendingPotion = new(__instance, target, renderer._generation, renderer._epoch, renderer.ViewPlayer!.PlayerCombatState?.TurnNumber ?? 0, Time.GetTicksMsec());
        renderer.ProcessPendingPotion();
        if (NRun.Instance != null)
            foreach (var holder in LocalSpectatorSource.Descendants<NPotionHolder>(NRun.Instance.GlobalUi.TopBar)) holder.CancelPotionUseOrDiscard();
        return false;
    }
}
