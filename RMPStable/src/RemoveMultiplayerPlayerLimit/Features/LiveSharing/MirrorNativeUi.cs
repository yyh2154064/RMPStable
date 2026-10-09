using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Native controls are addressed by their semantic tree position, never another
// process's instance ID. Auto-generated Godot names differ between processes.
internal static class MirrorNativeUi
{
    private static readonly FieldInfo GridCards = typeof(NCardGrid).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo GridCardSize = typeof(NCardGrid).GetField("_cardSize", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo GridAllocate = typeof(NCardGrid).GetMethod("AllocateCardHolders", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo GridColumns = typeof(NCardGrid).GetProperty("Columns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo GridPadding = typeof(NCardGrid).GetProperty("CardPadding", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo GridTop = typeof(NCardGrid).GetProperty("ScrollLimitTop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo GridBottom = typeof(NCardGrid).GetProperty("ScrollLimitBottom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static IEnumerable<(string Name, Node? Root)> Roots()
    {
        yield return ("overlay", NOverlayStack.Instance);
        yield return ("capstone", NCapstoneContainer.Instance);
        yield return ("modal", NModalContainer.Instance);
        yield return ("inspectCard", NGame.Instance?.InspectCardScreen);
        yield return ("inspectRelic", NGame.Instance?.InspectRelicScreen);
        yield return ("run", NRun.Instance);
    }
    private static string Part(Node node)
    {
        if (node is NMapPoint point) return node.GetType().Name + "@" + point.Point.coord.row + "," + point.Point.coord.col;
        if (node is NCardHolder holder && holder.CardModel is { } card)
        {
            // Grid holders move to the front on hover and are recycled when
            // scrolling. Address the native grid's card model, not its holders.
            if (holder is NGridCardHolder && Parent<NCardGrid>(holder) is { } grid && GridCards.GetValue(grid) is List<CardModel> cards)
                return node.GetType().Name + "!" + card.Id + ":" + card.CurrentUpgradeLevel + ":grid:" + cards.IndexOf(card);
            var copies = node.GetParent()?.GetChildren().OfType<NCardHolder>().Where(h => !h.IsQueuedForDeletion() && h.GetType() == node.GetType() && h.CardModel?.Id == card.Id && h.CardModel.CurrentUpgradeLevel == card.CurrentUpgradeLevel).ToArray();
            return node.GetType().Name + "!" + card.Id + ":" + card.CurrentUpgradeLevel + ":" + (copies == null ? 0 : Array.IndexOf(copies, holder));
        }
        var siblings = node.GetParent()?.GetChildren().Where(n => !n.IsQueuedForDeletion() && n.GetType() == node.GetType()).ToArray();
        return node.GetType().Name + "#" + (siblings == null ? 0 : Array.IndexOf(siblings, node));
    }
    internal static string Key(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.IsQueuedForDeletion()) return "";
        foreach (var (name, root) in Roots())
        {
            if (root == null || !GodotObject.IsInstanceValid(root) || node != root && !root.IsAncestorOf(node)) continue;
            var parts = new List<string>();
            for (var current = node; current != root; current = current.GetParent()) parts.Add(Part(current));
            parts.Reverse(); return name + "/" + string.Join("/", parts);
        }
        return "";
    }
    internal static Node? Resolve(string key, bool revealSelection = false)
    {
        var parts = key.Split('/');
        var node = Roots().FirstOrDefault(r => r.Name == parts[0]).Root;
        foreach (var part in parts.Skip(1).Where(p => p.Length > 0))
        {
            if (node == null || !GodotObject.IsInstanceValid(node)) return null;
            if (part.Contains('!'))
            {
                var marker = part.LastIndexOf(":grid:", StringComparison.Ordinal);
                if (marker >= 0 && Parent<NCardGrid>(node) is { } grid && GridCards.GetValue(grid) is List<CardModel> cards &&
                    int.TryParse(part.Substring(marker + 6), out var gridIndex) && gridIndex >= 0 && gridIndex < cards.Count &&
                    part.Substring(0, marker) == nameof(NGridCardHolder) + "!" + cards[gridIndex].Id + ":" + cards[gridIndex].CurrentUpgradeLevel)
                {
                    var holder = grid.GetCardHolder(cards[gridIndex]);
                    if (holder == null && revealSelection && Parent<NCardGridSelectionScreen>(grid) is { } selector &&
                        LocalSpectatorSource.ActiveNativeUi == selector && LocalSpectatorSource.PendingNativeChoice)
                    {
                        var size = (Vector2)GridCardSize.GetValue(grid)!;
                        int columns = (int)GridColumns.GetValue(grid)!;
                        float padding = (float)GridPadding.GetValue(grid)!;
                        float top = (float)GridTop.GetValue(grid)!, bottom = (float)GridBottom.GetValue(grid)!;
                        grid.SetScrollPosition(Math.Clamp(-(gridIndex / columns) * (size.Y + padding), Math.Min(top, bottom), Math.Max(top, bottom)));
                        GridAllocate.Invoke(grid, null);
                        holder = grid.GetCardHolder(cards[gridIndex]);
                    }
                    node = holder;
                }
                else node = node.GetChildren().OfType<NCardHolder>().FirstOrDefault(h => !h.IsQueuedForDeletion() && Part(h) == part);
                continue;
            }
            if (part.IndexOf('@') is var at && at >= 0)
            {
                var coordinate = part.Substring(at + 1);
                node = node.GetChildren().OfType<NMapPoint>().FirstOrDefault(p => p.GetType().Name == part.Substring(0, at) && p.Point.coord.row + "," + p.Point.coord.col == coordinate);
                continue;
            }
            var split = part.LastIndexOf('#');
            if (node == null || split < 0 || !int.TryParse(part.Substring(split + 1), out var index)) return null;
            node = node.GetChildren().Where(n => !n.IsQueuedForDeletion() && n.GetType().Name == part.Substring(0, split)).ElementAtOrDefault(index);
        }
        return node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree() ? node : null;
    }
    internal static T? Parent<T>(Node node) where T : Node
    {
        for (var current = node; current != null; current = current.GetParent()) if (current is T result) return result;
        return null;
    }
    internal static bool IsHandPlay(NCardHolder holder) => Parent<NPlayerHand>(holder) != null && NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.Play;
    // Browsing does not make a game decision. Opening these screens is local
    // presentation, so their back buttons/cards/scrolls cannot enter the journal.
    internal static bool IsLocalPresentation(Node node) => Parent<NCardPileScreen>(node) != null || Parent<NDeckViewScreen>(node) != null ||
        Parent<NPauseMenu>(node) != null || Under(node, NGame.Instance?.InspectCardScreen) || Under(node, NGame.Instance?.InspectRelicScreen) ||
        Under(node, RemoveMultiplayerPlayerLimit.Infrastructure.SceneMonitor.FindSettingsScreen()) ||
        node == NRun.Instance?.GlobalUi.TopBar.Deck || node == NRun.Instance?.GlobalUi.TopBar.Pause ||
        node.GetType().Name is "NDrawPileButton" or "NDiscardPileButton" or "NExhaustPileButton";
    private static bool Under(Node node, Node? root) => root != null && GodotObject.IsInstanceValid(root) && (node == root || root.IsAncestorOf(node));
    // A selector's scroll position is presentation; selection decisions remain
    // authoritative and use semantic card keys rather than scroll coordinates.
    internal static bool IsLocalScroll(Node node) => IsLocalPresentation(node) || Parent<NCardGridSelectionScreen>(node) != null;
    internal static bool IgnoreButton(NClickableControl button) => QuickSl.QuickSlController.ConfirmationOpen || Parent<NCardHolder>(button) != null ||
        Parent<NMerchantSlot>(button) != null || button == NCombatRoom.Instance?.Ui?.EndTurnButton;
}
