using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private bool? _localMapOpen;
    private void ClickPresentation(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl button)
    {
        button.ForceClick();
        if (button == NRun.Instance?.GlobalUi.TopBar.Map)
            _localMapOpen = MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen;
    }
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
        if (NGame.Instance.InspectRelicScreen?.IsVisibleInTree() == true) { NGame.Instance.InspectRelicScreen.Close(); return true; }
        if (LocalSpectatorSource.ActiveNativeUi is not { } root) return false;
        // Merchant cards use NCard rather than NCardHolder/AltPressed. Invoke
        // the slot's original virtual preview so upgrade UI stays native/local.
        var merchant = LocalSpectatorSource.Descendants<NMerchantSlot>(root).LastOrDefault(s =>
            s is NMerchantCard or NMerchantRelic && s.IsVisibleInTree() && s.Entry.IsStocked && s.Hitbox.IsEnabled && Contains(s.Hitbox, point));
        if (merchant != null && TryPreviewMerchant(merchant)) return true;
        var holder = LocalSpectatorSource.Descendants<NCardHolder>(root).LastOrDefault(h => h.Hitbox.IsEnabled && Contains(h.Hitbox, point) &&
            h.GetSignalConnectionList(NCardHolder.SignalName.AltPressed).Count > 0);
        if (holder == null) return false;
        // Original reward/choose/grid screens connect their own inspect handler.
        // AltPressed never completes the native CardsSelected task.
        holder.EmitSignal(NCardHolder.SignalName.AltPressed, holder);
        return true;
    }
    private static bool TryPreviewMerchant(NMerchantSlot slot)
    {
        if (slot is not (NMerchantCard or NMerchantRelic) || !slot.IsVisibleInTree() || !slot.Entry.IsStocked ||
            !slot.Hitbox.IsEnabled || MirrorNativeUi.Parent<NMerchantInventory>(slot)?.IsOpen != true) return false;
        typeof(NMerchantSlot).GetMethod("OnPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(slot, null);
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
