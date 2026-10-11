using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static partial class MultiplayerMirrorSession
{
    private static MultiplayerControlView CaptureRoomControl()
    {
        var view = new MultiplayerControlView { Epoch = _permissionEpoch };
        var player = LocalContext.GetMe(_state); var root = LocalSpectatorSource.ActiveNativeUi;
        if (player == null || root == null || NModalContainer.Instance?.OpenModal != null || NCapstoneContainer.Instance?.CurrentCapstoneScreen != null ||
            NGame.Instance.InspectCardScreen?.IsVisibleInTree() == true || NGame.Instance.InspectRelicScreen?.IsVisibleInTree() == true) return view;
        foreach (var button in LocalSpectatorSource.Descendants<NClickableControl>(root))
        {
            if (!button.IsEnabled || !button.IsVisibleInTree() || button.MouseFilter == Control.MouseFilterEnum.Ignore ||
                MirrorNativeUi.Parent<NCardHolder>(button) != null || MirrorNativeUi.Parent<NMerchantSlot>(button) != null ||
                button is NEventOptionButton option && option.Option.IsLocked ||
                button is NMapPoint point && MultiplayerNoncombat.Invoke(point, "get_IsTravelable") is not true) continue;
            view.Actions.Add(new() { Id = "room:" + ControlId(button), Kind = "native", NativePath = MirrorNativeUi.Key(button), Enabled = true });
        }
        foreach (var holder in LocalSpectatorSource.Descendants<NCardHolder>(root).Where(h => h.CardModel != null && h.IsVisibleInTree() && MirrorNativeUi.Parent<NMerchantSlot>(h) == null && LocalSpectatorSource.NativeReplayReady(h, LocalSpectatorSource.HandSelectionContext(h))))
            view.Actions.Add(new() { Id = "select:" + ControlId(holder.CardModel!) + ":" + holder.GetType().Name, Kind = "select", CardId = holder.CardModel!.Id.ToString(), NativePath = MirrorNativeUi.Key(holder), Enabled = true });
        foreach (var slot in LocalSpectatorSource.Descendants<NMerchantSlot>(root).Where(s => s.IsVisibleInTree() && s.Hitbox.IsEnabled && s.Entry.IsStocked && MirrorNativeUi.Parent<NMerchantInventory>(s)?.IsOpen == true))
            view.Actions.Add(new() { Id = "buy:" + ControlId(slot.Entry), Kind = "buy", NativePath = MirrorNativeUi.Key(slot), Enabled = true });
        // Private player and native page state, not the whole team's hash. A
        // teammate finishing their personal page must not block our own click.
        string context = JsonSerializer.Serialize(new { _generation, _controlRevision, player = Convert.ToBase64String(MirrorState.Pack(player.ToSerializable())),
            room = _state!.CurrentRoom?.RoomType, page = root.GetInstanceId(), choice = MultiplayerPersonalUi.Capture(), actions = view.Actions,
            shop = _state.CurrentRoom is MerchantRoom merchant ? merchant.GetLocalInventory().AllEntries.Select(e => new { id = ControlId(e), e.Cost, e.IsStocked }).ToArray() : null });
        view.Context = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        return view;
    }
    private static bool ExecuteRoomAction(ControlActionSnapshot action)
    {
        var node = MirrorNativeUi.Resolve(action.NativePath, true);
        if (action.Kind == "buy" && node is NMerchantSlot slot && slot.Hitbox.IsEnabled && slot.Entry.IsStocked && slot.IsVisibleInTree())
        { if (MultiplayerNoncombat.Invoke(slot, "OnSelected") is System.Threading.Tasks.Task task) MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(task); return true; }
        if (node is not NClickableControl button || !button.IsEnabled || !button.IsVisibleInTree()) return false;
        if (button is NEventOptionButton option && option.Option.IsLocked || button is NMapPoint point && MultiplayerNoncombat.Invoke(point, "get_IsTravelable") is not true) return false;
        button.ForceClick(); return true;
    }
}
