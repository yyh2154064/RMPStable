using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MultiplayerControlView
{
    public long Epoch { get; set; }
    public bool Granted { get; set; }
    public string Context { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<ControlActionSnapshot> Actions { get; set; } = new();
    public List<ControlTargetSnapshot> Targets { get; set; } = new();
}
internal sealed class MultiplayerControlRequest
{
    public long Epoch { get; set; }
    public long Id { get; set; }
    public long Generation { get; set; }
    public int Events { get; set; }
    public string Context { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
}

// The selected player's original game owns permission and resolves all opaque
// model IDs. The room host still orders actions through the original service.
internal static partial class MultiplayerMirrorSession
{
    private static ulong _controlViewer, _pendingViewer, _permissionDeadline;
    private static string _controlToken = "", _pendingToken = "";
    private static long _permissionEpoch, _controlRevision, _lastControlRequest;
    private static readonly Dictionary<long, (bool Accepted, string Reason)> ControlResults = new();
    private static readonly Dictionary<object, string> ControlIds = new();
    private static long _controlId;
    private static CanvasLayer? _permissionUi;
    private static Label? _permissionLabel;
    private static Button? _allowButton, _denyButton;
    private static string _controlNotice = "";
    private static ulong _nextControlHeartbeat;
    internal static bool ControlRequested { get; private set; }
    internal static int AcceptedControlRequests { get; private set; }
    internal static int RejectedControlRequests { get; private set; }
    internal static void InstallBattleInputTracking()
    {
        new Harmony("RMPStable.MultiplayerOwnInput").Patch(typeof(ActionQueueSynchronizer).GetMethod("RequestEnqueue")!,
            new HarmonyMethod(typeof(MultiplayerMirrorSession).GetMethod(nameof(OwnActionRequested), BindingFlags.Static | BindingFlags.NonPublic)));
    }
    private static void OwnActionRequested(GameAction __0)
    { if (!MirrorRenderer.IsRenderer && _controlViewer != 0 && _service?.NetId == __0.OwnerId) _controlRevision++; }
    private static string ControlId(object model)
    { if (!ControlIds.TryGetValue(model, out var id)) ControlIds[model] = id = "battle:" + ++_controlId; return id; }
    private static bool InBattle => InFinal || _state != null && _checkpoint != null &&
        (_state.CurrentRoom is CombatRoom && ReferenceEquals(_checkpoint, MirrorState.CurrentReplay) ||
         MultiplayerNoncombat.IsCurrent(_state) && ReferenceEquals(_checkpoint, MultiplayerNoncombat.Checkpoint));
    private static bool _permissionCombat;
    internal static void RequestControl(bool enabled)
    {
        ControlRequested = enabled;
        if (_selected == 0 || _service == null || _remoteSession.Length == 0) return;
        Send(_selected, _token, new() { Kind = enabled ? "control-request" : "control-release", Build = MirrorState.Build, Session = _remoteSession, Generation = _receivedGeneration });
        if (!enabled) Mirror?.ApplyMultiplayerPermission(new MultiplayerControlView());
    }
    private static void ReleaseControl(ulong viewer, string token)
    {
        if (_pendingViewer == viewer && _pendingToken == token) { _pendingViewer = 0; _pendingToken = ""; }
        if (_controlViewer == viewer && _controlToken == token) RevokeControl();
    }
    internal static void RevokeControl()
    {
        if (_controlViewer != 0) { _permissionEpoch++; _controlViewer = 0; _controlToken = ""; _controlRevision++; ControlResults.Clear(); _lastControlRequest = 0; }
        _pendingViewer = 0; _pendingToken = "";
        if (_permissionUi != null && GodotObject.IsInstanceValid(_permissionUi)) _permissionUi.Hide();
    }
    internal static void DecideControl(bool allow)
    {
        if (_pendingViewer == 0) return;
        ulong viewer = _pendingViewer; string token = _pendingToken;
        _pendingViewer = 0; _pendingToken = "";
        if (allow && InBattle && Connected(viewer) && Subscribers.TryGetValue(viewer, out var sub) && sub.Token == token && _controlViewer == 0)
        { _controlViewer = viewer; _controlToken = token; _permissionEpoch++; _controlRevision++; _permissionDeadline = Time.GetTicksMsec() + 15000; ControlResults.Clear(); _lastControlRequest = 0; }
        else SendControlResult(viewer, token, null, false, "玩家未授权房间控制");
    }
    private static void TickControl()
    {
        if (_selected != 0 && Mirror is { } mirror && mirror.MultiplayerAuthority?.Granted == true && !mirror.Process.Child.HasExited && mirror.Error.Length == 0 && Time.GetTicksMsec() >= _nextControlHeartbeat)
        {
            _nextControlHeartbeat = Time.GetTicksMsec() + 1000;
            Send(_selected, _token, new() { Kind = "control-heartbeat", Build = MirrorState.Build, Session = _remoteSession, Generation = _receivedGeneration, Request = new() { Epoch = mirror.ControlEpoch } });
        }
        bool combat = CombatManager.Instance.IsInProgress || CombatManager.Instance.IsStarting;
        if (_permissionCombat != combat) { _permissionCombat = combat; RevokeControl(); }
        if (_controlViewer != 0 && (!InBattle || !Connected(_controlViewer) || Time.GetTicksMsec() > _permissionDeadline || !Subscribers.TryGetValue(_controlViewer, out var sub) || sub.Token != _controlToken)) RevokeControl();
        if (_pendingViewer != 0 && (!InBattle || !Connected(_pendingViewer) || Time.GetTicksMsec() > _permissionDeadline))
        {
            if (Connected(_pendingViewer)) SendControlResult(_pendingViewer, _pendingToken, null, false, "房间控制申请已过期，请重新申请");
            _pendingViewer = 0; _pendingToken = "";
        }
        if (_pendingViewer == 0 && _controlViewer == 0) { if (_permissionUi != null && GodotObject.IsInstanceValid(_permissionUi)) _permissionUi.Hide(); return; }
        if (_permissionUi == null || !GodotObject.IsInstanceValid(_permissionUi))
        {
            _permissionUi = new CanvasLayer { Name = "MultiplayerBattlePermission", Layer = 121 }; NGame.Instance.AddChild(_permissionUi);
            var box = new PanelContainer { Position = new Vector2(20, 140), CustomMinimumSize = new Vector2(390, 0) }; _permissionUi.AddChild(box);
            var row = new VBoxContainer(); box.AddChild(row);
            _permissionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(390, 0) }; row.AddChild(_permissionLabel);
            var buttons = new HBoxContainer(); row.AddChild(buttons);
            _allowButton = new Button { Name = "AllowBattleControl", Text = "允许本房间" }; buttons.AddChild(_allowButton); _allowButton.Pressed += () => DecideControl(true);
            _denyButton = new Button { Name = "RevokeBattleControl", Text = "拒绝" }; buttons.AddChild(_denyButton); _denyButton.Pressed += () => { if (_controlViewer != 0) RevokeControl(); else DecideControl(false); };
        }
        ulong id = _controlViewer != 0 ? _controlViewer : _pendingViewer;
        string name = Sources().FirstOrDefault(p => p.Id == id.ToString())?.Name ?? id.ToString();
        _permissionLabel!.Text = name + (_controlViewer != 0 ? " 已获房间控制权。你仍可操作，随时可撤销。" : " 请求控制你的窗口。是否允许？");
        _allowButton!.Visible = _controlViewer == 0; _denyButton!.Text = _controlViewer != 0 ? "撤销控制" : "拒绝"; _permissionUi.Show();
    }
    private static MultiplayerControlView ControlFor(ulong viewer, string token)
    {
        bool granted = InBattle && viewer == _controlViewer && token == _controlToken;
        var view = granted ? CaptureBattleControl() : new MultiplayerControlView();
        view.Granted = granted; view.Epoch = _permissionEpoch;
        view.Reason = granted ? "已获玩家授权 · 本房间" : viewer == _pendingViewer ? "等待玩家本体授权" : "观战 · 控制须由玩家授权";
        return view;
    }
    private static void ReceiveControl(ulong viewer, string token, MultiplayerMirrorFrame frame)
    {
        if (frame.Build != MirrorState.Build || frame.Session != _session || !Subscribers.TryGetValue(viewer, out var sub) || sub.Token != token) { RejectedPackets++; return; }
        if (frame.Kind == "control-release") { ReleaseControl(viewer, token); return; }
        if (frame.Kind == "control-heartbeat")
        {
            if (viewer == _controlViewer && token == _controlToken && frame.Request?.Epoch == _permissionEpoch && frame.Generation == _generation) _permissionDeadline = Time.GetTicksMsec() + 15000;
            return;
        }
        if (frame.Kind == "control-request")
        {
            if (!InBattle || frame.Generation != _generation || _controlViewer != 0 || _pendingViewer != 0)
            { SendControlResult(viewer, token, null, false, "当前无法申请房间控制"); return; }
            _pendingViewer = viewer; _pendingToken = token; _permissionDeadline = Time.GetTicksMsec() + 15000;
            if (viewer == _service!.NetId) DecideControl(true);
            return;
        }
        var request = frame.Request;
        if (request == null) { RejectedPackets++; return; }
        bool accepted = false; string reason = "房间控制授权已失效";
        if (InBattle && viewer == _controlViewer && token == _controlToken && request.Epoch == _permissionEpoch && request.Generation == _generation)
        {
            if (ControlResults.TryGetValue(request.Id, out var prior)) { SendControlResult(viewer, token, request, prior.Accepted, prior.Reason); return; }
            if (request.Id > _lastControlRequest && request.Id > 0)
            {
                _lastControlRequest = request.Id;
                var control = CaptureBattleControl();
                reason = "玩家状态或选择已变化，请等待刷新";
                if (request.Context == control.Context && request.Events >= 0 && request.Events <= (_checkpoint?.events.Count ?? 0) + MultiplayerNoncombat.Operations.Count + (InFinal ? FinalOperations.Count : 0))
                {
                    var action = control.Actions.FirstOrDefault(a => a.Id == request.Action && a.Enabled);
                    if (action != null && (!action.RequiresTarget || action.TargetIds.Contains(request.Target)))
                    {
                        try { accepted = ExecuteBattleAction(action, request.Target, control); reason = accepted ? "已提交原版多人流程" : "当前操作或目标不可用"; if (accepted) _controlRevision++; }
                        catch (Exception e) { reason = "操作失败：" + e.GetBaseException().Message; }
                    }
                }
                ControlResults[request.Id] = (accepted, reason);
                while (ControlResults.Count > 64) ControlResults.Remove(ControlResults.Keys.Min());
            }
            else reason = "旧操作编号已失效";
        }
        if (accepted) AcceptedControlRequests++; else RejectedControlRequests++;
        SendControlResult(viewer, token, request, accepted, reason);
    }
    private static void SendControlResult(ulong viewer, string token, MultiplayerControlRequest? request, bool accepted, string reason)
        => Send(viewer, token, new() { Kind = "control-result", Build = MirrorState.Build, Session = _session, Generation = _generation, Request = request, Accepted = accepted, Reason = reason });
    private static void ForwardControlIntent(MirrorMessage message)
    {
        if (_selected == 0 || Mirror is not { } mirror || message.Generation != mirror.LocalGeneration || mirror.SourceGeneration != _receivedGeneration || _receivedGeneration == 0) return;
        Send(_selected, _token, new() { Kind = "control-intent", Build = MirrorState.Build, Session = _remoteSession, Generation = _receivedGeneration,
            Request = new() { Epoch = message.Epoch, Id = message.Request, Generation = _receivedGeneration, Events = message.Events, Context = message.Value, Action = message.NodeKey, Target = message.TargetKey } });
    }
    private static void ReceiveControlResult(MultiplayerMirrorFrame frame)
    {
        _controlNotice = frame.Reason;
        if (frame.Request == null) { ControlRequested = false; return; }
        if (Mirror is not { } mirror) return;
        var request = frame.Request;
        if (request.Generation != mirror.SourceGeneration) return;
        _ = mirror.Process.Wire.Send(new MirrorMessage { Kind = "result", Generation = mirror.LocalGeneration, Epoch = request.Epoch, Request = request.Id, Accepted = frame.Accepted, Room = frame.Reason });
    }
    private static MultiplayerControlView CaptureBattleControl()
    {
        if (InFinal) return CaptureFinalControl();
        if (!CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsStarting) return CaptureRoomControl();
        var view = new MultiplayerControlView { Epoch = _permissionEpoch }; var player = LocalContext.GetMe(_state); var state = player?.PlayerCombatState;
        if (player == null || state == null) return view;
        var choice = MultiplayerPersonalUi.Capture();
        string context = JsonSerializer.Serialize(new { _generation, _controlRevision, state.TurnNumber, state.Phase, state.Energy, state.Stars,
            hand = state.Hand.Cards.Select(c => new { id = ControlId(c), card = Convert.ToBase64String(MirrorState.Pack(c.ToSerializable())) }).ToArray(),
            potions = player.Potions.Select(p => ControlId(p)).ToArray(), choice, screen = (NOverlayStack.Instance?.Peek() as Node)?.GetInstanceId(), map = NMapScreen.Instance?.IsOpen,
            modal = (NModalContainer.Instance?.OpenModal as Node)?.GetInstanceId(), pause = NCapstoneContainer.Instance?.CurrentCapstoneScreen != null, target = NTargetManager.Instance?.IsInSelection });
        view.Context = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        bool unblocked = NMapScreen.Instance?.IsOpen != true && NModalContainer.Instance?.OpenModal == null && NCapstoneContainer.Instance?.CurrentCapstoneScreen == null && NGame.Instance.InspectCardScreen?.IsVisibleInTree() != true && NGame.Instance.InspectRelicScreen?.IsVisibleInTree() != true;
        if (!unblocked || !player.Creature.IsAlive) return view;
        if (choice != null)
        {
            Node? root = choice.Kind is "Hand" or "Upgrade" ? NPlayerHand.Instance : NOverlayStack.Instance.Peek() as Node;
            if (root == null) return view;
            foreach (var holder in LocalSpectatorSource.Descendants<NCardHolder>(root).Where(h => h.CardModel != null && LocalSpectatorSource.NativeReplayReady(h, LocalSpectatorSource.HandSelectionContext(h))))
                view.Actions.Add(new() { Id = "select:" + ControlId(holder.CardModel!) + ":" + holder.GetType().Name, Kind = "select", CardId = holder.CardModel!.Id.ToString(), NativePath = MirrorNativeUi.Key(holder), Enabled = true });
            var confirm = MultiplayerPersonalUi.Read(root, root is NPlayerHand ? "_selectModeConfirmButton" : "_confirmButton") as NClickableControl;
            if (confirm?.IsEnabled == true && confirm.IsVisibleInTree()) view.Actions.Add(new() { Id = "confirm:" + choice.Id, Kind = "choice", NativePath = MirrorNativeUi.Key(confirm), Enabled = true });
            var skip = MultiplayerPersonalUi.Read(root, "_skipButton") as NClickableControl;
            if (choice.CanSkip && skip?.IsEnabled == true && skip.IsVisibleInTree()) view.Actions.Add(new() { Id = "skip:" + choice.Id, Kind = "choice", NativePath = MirrorNativeUi.Key(skip), Enabled = true });
            return view;
        }
        if (NOverlayStack.Instance.Peek() != null || NTargetManager.Instance?.IsInSelection == true || state.Phase != PlayerTurnPhase.Play || CombatManager.Instance.PlayerActionsDisabled || CombatManager.Instance.IsStarting || CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo) return view;
        var creatures = player.Creature.CombatState.Creatures;
        foreach (var c in creatures.Where(c => c.IsAlive)) view.Targets.Add(new() { Id = ControlId(c), NativeIndex = creatures.ToList().IndexOf(c), Enemy = c.IsEnemy });
        foreach (var card in state.Hand.Cards)
        {
            bool targeted = card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly;
            var targets = creatures.Where(c => c.IsAlive && card.CanPlayTargeting(c)).Select(ControlId).ToList();
            view.Actions.Add(new() { Id = "play:" + ControlId(card), Kind = "play", NativeIndex = state.Hand.Cards.ToList().IndexOf(card), CardId = Convert.ToBase64String(MirrorState.Pack(card.ToSerializable())), RequiresTarget = targeted, TargetIds = targets, Enabled = targeted ? targets.Count > 0 : card.CanPlayTargeting(null) });
        }
        if (NCombatRoom.Instance?.Ui?.EndTurnButton.IsEnabled == true) view.Actions.Add(new() { Id = "end:" + state.TurnNumber, Kind = "endTurn", NativePath = MirrorNativeUi.Key(NCombatRoom.Instance.Ui.EndTurnButton), Enabled = true });
#if STS2_0111
        bool canUsePotions = player.CanUseOrRemovePotions;
#else
        bool canUsePotions = player.CanRemovePotions;
#endif
        foreach (var potion in player.Potions.Where(p => !p.IsQueued && canUsePotions && p.PassesCustomUsabilityCheck))
        {
            bool targeted = potion.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer;
            var targets = creatures.Where(c => potion.IsValidTarget(c)).Select(ControlId).ToList();
            // Automatic potions and out-of-combat-only targets are not commands.
            if (potion.Usage.ToString() is "Automatic" or "None" || potion.TargetType == TargetType.TargetedNoCreature) continue;
            view.Actions.Add(new() { Id = "potion:" + ControlId(potion), Kind = "potion", NativeIndex = player.Potions.ToList().IndexOf(potion), CardId = potion.Id.ToString(), RequiresTarget = targeted, TargetIds = targets, Enabled = targeted ? targets.Count > 0 : potion.IsValidTarget(potion.TargetType == TargetType.Self ? player.Creature : null) });
        }
        return view;
    }
    private static bool ExecuteBattleAction(ControlActionSnapshot action, string targetId, MultiplayerControlView view)
    {
        if (action.Kind == "ending") return ExecuteFinalAction(action);
        if (action.Kind is "native" or "buy") return ExecuteRoomAction(action);
        if (action.Kind == "select")
        {
            if (MirrorNativeUi.Resolve(action.NativePath, true) is not NCardHolder selected || !LocalSpectatorSource.NativeReplayReady(selected, LocalSpectatorSource.HandSelectionContext(selected))) return false;
            selected.EmitSignal(NCardHolder.SignalName.Pressed, selected); return true;
        }
        if (action.Kind == "choice")
        {
            if (MirrorNativeUi.Resolve(action.NativePath) is not NClickableControl choiceButton || !choiceButton.IsEnabled) return false;
            choiceButton.ForceClick(); return true;
        }
        var player = LocalContext.GetMe(_state)!; var state = player.PlayerCombatState!;
        var target = view.Targets.FirstOrDefault(t => t.Id == targetId);
        var creature = target == null ? null : (CombatManager.Instance.IsInProgress ? player.Creature.CombatState.Creatures.ToList() : _state!.Players.Select(p => p.Creature).ToList()).ElementAtOrDefault(target.NativeIndex);
        switch (action.Kind)
        {
            case "play": return state.Hand.Cards[action.NativeIndex].TryManualPlay(creature);
            case "potion":
                var potion = player.Potions.ElementAt(action.NativeIndex); if (potion.TargetType == TargetType.Self) creature = player.Creature;
                if (!potion.IsValidTarget(creature)) return false; potion.EnqueueManualUse(creature); return true;
            case "endTurn": RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, state.TurnNumber)); return true;
            default: return false;
        }
    }
}
