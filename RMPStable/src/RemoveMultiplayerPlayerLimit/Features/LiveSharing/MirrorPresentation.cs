using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private NPotionPopup? PotionPopup => NRun.Instance == null ? null : LocalSpectatorSource.Descendants<NPotionPopup>(NRun.Instance)
        .LastOrDefault(p => !p.IsMarkedForRemoval && p.IsVisibleInTree());
    private bool TryCancelPotionPopup(Vector2 point)
    {
        if (PotionPopup is not { } popup || Contains(popup, point)) return false;
        if (_control && NativeIdle && !_waitingForAuthority)
            SendIntent("cancel", -1, -1, CurrentHash, _events, MirrorNativeUi.Key(popup));
        return true;
    }
    private bool TryInspectCard(Vector2 point)
    {
        if (PotionPopup != null || _dragCard != null || NTargetManager.Instance?.IsInSelection == true) return false;
        if (NGame.Instance.InspectCardScreen?.IsVisibleInTree() == true) { NGame.Instance.InspectCardScreen.Close(); return true; }
        if (LocalSpectatorSource.ActiveNativeUi is not { } root) return false;
        var holder = LocalSpectatorSource.Descendants<NCardHolder>(root).LastOrDefault(h => h.Hitbox.IsEnabled && Contains(h.Hitbox, point) &&
            h.GetSignalConnectionList(NCardHolder.SignalName.AltPressed).Count > 0);
        if (holder == null) return false;
        // Original reward/choose/grid screens connect their own inspect handler.
        // AltPressed never completes the native CardsSelected task.
        holder.EmitSignal(NCardHolder.SignalName.AltPressed, holder);
        return true;
    }
    internal void SubmitAbandon(NAbandonRunConfirmPopup popup)
    {
        if (_control && NativeIdle && !_waitingForAuthority && _finalSave == null)
        {
            popup.QueueFree(); NCapstoneContainer.Instance.Close();
            SendIntent("menu", -1, -1, CurrentHash, _events, value: "GiveUpConfirmed");
        }
    }
}
