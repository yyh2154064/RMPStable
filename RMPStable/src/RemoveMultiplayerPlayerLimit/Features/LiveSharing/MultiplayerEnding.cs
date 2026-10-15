using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static partial class MultiplayerMirrorSession
{
    private static RunState? _finalState;
    private static readonly List<MirrorOperation> FinalOperations = new();
    private static NClickableControl? _pendingFinalButton;
    private static bool InFinal => ReferenceEquals(_finalState, _state) && _state != null && FinalOperations.Count > 0;
    internal static void RecordFinal(MirrorOperation operation, RunState state)
    {
        if (MirrorRenderer.IsRenderer || !ReferenceEquals(_state, state)) return;
        RevokeControl(); _finalState = state; FinalOperations.Clear(); FinalOperations.Add(operation); _timer = 0;
    }
    private static IEnumerable<NClickableControl> FinalButtons()
    {
        if (!InFinal || NOverlayStack.Instance?.Peek() is not NGameOverScreen screen) yield break;
        foreach (string name in new[] { "_continueButton", "_mainMenuButton" })
            if (MultiplayerPersonalUi.Read(screen, name) is NClickableControl button) yield return button;
    }
    internal static void RecordFinalButton(NClickableControl button)
    {
        if (MirrorRenderer.IsRenderer || !button.IsEnabled || !button.IsVisibleInTree() || !FinalButtons().Contains(button)) return;
        string key = MirrorNativeUi.Key(button);
        if (key.Length > 0) FinalOperations.Add(new() { Kind = "finalButton", NodeKey = key });
    }
    private static MultiplayerControlView CaptureFinalControl()
    {
        var view = new MultiplayerControlView { Epoch = _permissionEpoch };
        foreach (var button in FinalButtons().Where(b => b.IsEnabled && b.IsVisibleInTree() && b != _pendingFinalButton))
            view.Actions.Add(new() { Id = "ending:" + ControlId(button), Kind = "ending", NativePath = MirrorNativeUi.Key(button), Enabled = true });
        string context = JsonSerializer.Serialize(new { _generation, _controlRevision, hash = MirrorJournal.EndHash(_state!), actions = view.Actions });
        view.Context = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context))); return view;
    }
    private static bool ExecuteFinalAction(ControlActionSnapshot action)
    {
        if (MirrorNativeUi.Resolve(action.NativePath) is not NClickableControl button || !button.IsEnabled || !button.IsVisibleInTree() || !FinalButtons().Contains(button)) return false;
        // The host's native main-menu handler disconnects ENet immediately.
        // Queue it after the accepted result has entered the reliable channel.
        // Continue/summary stays synchronous; neither path bypasses native UI.
        if (NOverlayStack.Instance?.Peek() is NGameOverScreen screen && MultiplayerPersonalUi.Read(screen, "_mainMenuButton") == button)
        {
            if (_pendingFinalButton != null) return false;
            _pendingFinalButton = button;
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(FinishFinalButton(button, _state!, _permissionEpoch));
        }
        else button.ForceClick();
        return true;
    }
    private static async System.Threading.Tasks.Task FinishFinalButton(NClickableControl button, RunState state, long epoch)
    {
        try
        {
            var game = MegaCrit.Sts2.Core.Nodes.NGame.Instance;
            for (int frame = 0; frame < 6; frame++) await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ReferenceEquals(state, _state) && _permissionEpoch == epoch && GodotObject.IsInstanceValid(button) && button.IsEnabled && button.IsVisibleInTree() && FinalButtons().Contains(button)) button.ForceClick();
        }
        finally { if (_pendingFinalButton == button) _pendingFinalButton = null; }
    }
}
