using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MultiplayerChoiceView
{
    public uint Id { get; set; }
    public string Kind { get; set; } = "";
    public string Prompt { get; set; } = "";
    public int Min { get; set; }
    public int Max { get; set; }
    public bool Manual { get; set; }
    public bool Cancelable { get; set; }
    public bool Confirm { get; set; }
    public bool CanSkip { get; set; }
    public bool Preview { get; set; }
    public List<byte[]> Cards { get; set; } = new();
    public List<int> Eligible { get; set; } = new();
    public List<int> Selected { get; set; } = new();
}

// Show the native local selector alongside the original Replay remote-choice
// task. Only the recorded result completes it; UI never sends a game choice.
internal static class MultiplayerPersonalUi
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private sealed class Selection
    {
        internal string Kind = "";
        internal Player Player = null!;
        internal CardSelectorPrefs Prefs;
        internal Func<CardModel, bool>? Filter;
        internal AbstractModel? Source;
        internal CardPile? Pile;
        internal List<CardModel> Cards = new();
        internal List<CardCreationResult>? Results;
        internal bool CanSkip;
    }
    private static readonly Dictionary<ulong, Selection> Pending = new();
    private static readonly Dictionary<(ulong, uint), Selection> Reserved = new();
    private static Node? _view;
    private static uint _choice;
    private static string _kind = "";
    private static int _applying;
    private static bool _installed;
    private static bool Replica => MirrorRenderer.IsRenderer && MirrorRenderer.Active?.Multiplayer == true;
    internal static object? Read(object o, string field)
    { for (Type? type = o.GetType(); type != null; type = type.BaseType) if (type.GetField(field, Any | BindingFlags.DeclaredOnly) is { } f) return f.GetValue(o); return null; }
    private static void Call(object o, string method, params object[] args)
    { for (Type? type = o.GetType(); type != null; type = type.BaseType) if (type.GetMethod(method, Any | BindingFlags.DeclaredOnly) is { } m) { m.Invoke(o, args); return; } throw new MissingMethodException(o.GetType().Name, method); }
    internal static void Initialize()
    {
        if (_installed) return; _installed = true;
        var h = new Harmony("RMPStable.MultiplayerPersonalUi");
        void Patch(MethodBase m, string? before = null, string? after = null) => h.Patch(m,
            before == null ? null : new HarmonyMethod(typeof(MultiplayerPersonalUi).GetMethod(before, Any)),
            after == null ? null : new HarmonyMethod(typeof(MultiplayerPersonalUi).GetMethod(after, Any)));
        foreach (var method in typeof(CardSelectCmd).GetMethods(Any).Where(m => m.Name.StartsWith("From", StringComparison.Ordinal))) Patch(method, nameof(CaptureCommand));
        Patch(typeof(PlayerChoiceSynchronizer).GetMethod("ReserveChoiceId")!, after: nameof(ReservedChoice));
        Patch(typeof(PlayerChoiceSynchronizer).GetMethod("WaitForRemoteChoice")!, after: nameof(WaitChoice));
        foreach (var pair in new[] { (typeof(NPlayerHand), "OnHolderPressed"), (typeof(NPlayerHand), "OnSelectModeConfirmButtonPressed"),
            (typeof(NSelectedHandCardContainer), "DeselectHolder"),
            (typeof(NPlayerHand), "CheckIfSelectionComplete"), (typeof(NSimpleCardSelectScreen), "OnCardClicked"), (typeof(NSimpleCardSelectScreen), "CompleteSelection"),
            (typeof(NCombatPileCardSelectScreen), "OnCardClicked"), (typeof(NCombatPileCardSelectScreen), "CompleteSelection"),
            (typeof(NChooseACardSelectionScreen), "SelectHolder"), (typeof(NChooseACardSelectionScreen), "OnSkipButtonReleased") })
            Patch(pair.Item1.GetMethod(pair.Item2, Any)!, nameof(AllowSelection));
        foreach (var type in new[] { typeof(NDeckCardSelectScreen), typeof(NDeckUpgradeSelectScreen) })
            foreach (var m in type.GetMethods(Any).Where(m => m.DeclaringType == type && m.Name is "OnCardClicked" or "ConfirmSelection" or "CloseSelection" or "CancelSelection" or "CheckIfSelectionComplete" or "PreviewSelection")) Patch(m, nameof(AllowSelection));
    }
    private static bool AllowSelection() => !Replica || _applying > 0;
    private static void CaptureCommand(MethodBase __originalMethod, object[] __args)
    {
        if (!Replica) return;
        var player = __args.OfType<Player>().FirstOrDefault(); if (player == null || !LocalContext.IsMe(player)) return;
        Pending.Remove(player.NetId);
        string kind = __originalMethod.Name switch { "FromHand" => "Hand", "FromHandForUpgrade" => "Upgrade", "FromSimpleGrid" or "FromSimpleGridForRewards" => "Grid",
            "FromCombatPile" => "Pile", "FromChooseACardScreen" => "Choose", "FromDeckGeneric" or "FromDeckForRemoval" => "Deck", "FromDeckForUpgrade" => "DeckUpgrade", _ => "" };
        if (kind.Length == 0) return;
        var selection = new Selection { Player = player, Kind = kind, Prefs = __args.OfType<CardSelectorPrefs>().FirstOrDefault(),
            Pile = __args.OfType<CardPile>().FirstOrDefault(), Filter = __args.OfType<Func<CardModel, bool>>().FirstOrDefault(),
            Source = __args.OfType<AbstractModel>().FirstOrDefault(), CanSkip = __args.OfType<bool>().FirstOrDefault() };
        selection.Cards = __args.OfType<IEnumerable<CardModel>>().FirstOrDefault()?.ToList() ?? new();
        selection.Results = __args.OfType<IEnumerable<CardCreationResult>>().FirstOrDefault()?.ToList();
        if (selection.Results != null) selection.Cards = selection.Results.Select(c => c.Card).ToList();
        if (kind == "Upgrade") { selection.Prefs = new CardSelectorPrefs(new LocString("gameplay_ui", "CHOOSE_CARD_UPGRADE_HEADER"), 1); selection.Filter = c => c.IsUpgradable; }
        if (kind is "Deck" or "DeckUpgrade")
        {
            if (__originalMethod.Name == "FromDeckForRemoval")
            {
                var extra = selection.Filter; selection.Filter = c => c.IsRemovable && (extra?.Invoke(c) ?? true);
            }
            selection.Cards = player.Deck.Cards.Where(kind == "DeckUpgrade" ? c => c.IsUpgradable : selection.Filter ?? (_ => true)).ToList();
            if (__args.OfType<Func<CardModel, int>>().FirstOrDefault() is { } sort) selection.Cards = selection.Cards.OrderBy(sort).ToList();
            else if (__originalMethod.Name == "FromDeckForRemoval") selection.Cards = selection.Cards.OrderBy(c => c.Type == CardType.Curse ? -999999999 : player.Deck.Cards.ToList().IndexOf(c)).ToList();
        }
        Pending[player.NetId] = selection;
    }
    private static void ReservedChoice(Player player, uint __result)
    {
        if (Replica && Pending.Remove(player.NetId, out var selection)) Reserved[(player.NetId, __result)] = selection;
    }
    private static void WaitChoice(Player player, uint choiceId, ref Task<PlayerChoiceResult> __result)
    {
        if (Replica && LocalContext.IsMe(player) && Reserved.Remove((player.NetId, choiceId), out var selection))
            __result = Show(selection, choiceId, __result);
    }
    private static async Task<PlayerChoiceResult> Show(Selection selection, uint choice, Task<PlayerChoiceResult> resultTask)
    {
        if (resultTask.IsCompleted) return await resultTask; // Historical choice: no transient selector flashes.
        var renderer = MirrorRenderer.Active!; long generation = renderer.MultiplayerGeneration;
        Node view; Task<IEnumerable<CardModel>> display;
        switch (selection.Kind)
        {
            case "Hand": case "Upgrade":
                view = NPlayerHand.Instance!;
                display = ((NPlayerHand)view).SelectCards(selection.Prefs, selection.Filter, selection.Source, selection.Kind == "Upgrade" ? NPlayerHand.Mode.UpgradeSelect : NPlayerHand.Mode.SimpleSelect); break;
            case "Grid":
                var grid = selection.Results == null ? NSimpleCardSelectScreen.Create(selection.Cards, selection.Prefs) : NSimpleCardSelectScreen.Create(selection.Results, selection.Prefs);
                NOverlayStack.Instance.Push(grid); view = grid; display = grid.CardsSelected(); break;
            case "Pile":
                var pile = NCombatPileCardSelectScreen.Create(selection.Pile!, selection.Prefs, selection.Filter ?? (_ => true));
                NOverlayStack.Instance.Push(pile); view = pile; display = pile.CardsSelected(); break;
            case "Choose":
                var choose = NChooseACardSelectionScreen.ShowScreen(selection.Cards, selection.CanSkip);
                view = choose; display = choose.CardsSelected(); break;
            case "Deck":
                var deck = NDeckCardSelectScreen.Create(selection.Cards, selection.Prefs); NOverlayStack.Instance.Push(deck); view = deck; display = deck.CardsSelected(); break;
            case "DeckUpgrade":
                var upgrades = NDeckUpgradeSelectScreen.ShowScreen(selection.Cards, selection.Prefs, selection.Player.RunState); view = upgrades; display = upgrades.CardsSelected(); break;
            default: return await resultTask;
        }
        _view = view; _choice = choice; _kind = selection.Kind;
        try
        {
            var result = await resultTask;
            if (renderer.MultiplayerGeneration != generation || !GodotObject.IsInstanceValid(view)) return result;
            IEnumerable<CardModel> cards = selection.Kind == "Choose" ? result.AsIndex() < 0 ? Array.Empty<CardModel>() : new[] { selection.Cards[result.AsIndex()] } :
                selection.Kind == "Grid" ? result.AsIndexes().Select(i => selection.Cards[i]).ToList() :
                selection.Kind is "Deck" or "DeckUpgrade" ? result.AsDeckCards() : result.AsCombatCards();
            ((TaskCompletionSource<IEnumerable<CardModel>>)Read(view, view is NPlayerHand ? "_selectionCompletionSource" : "_completionSource")!).TrySetResult(cards.ToList());
            await display;
            if (view is NCardGridSelectionScreen && GodotObject.IsInstanceValid(view) && view.IsInsideTree()) NOverlayStack.Instance.Remove((IOverlayScreen)view);
            return result;
        }
        finally { if (ReferenceEquals(_view, view)) { _view = null; _kind = ""; } }
    }
    internal static void Reset() { Pending.Clear(); Reserved.Clear(); _view = null; _kind = ""; }
    private static List<CardModel> Cards(Node view)
    {
        if (view is NCardRewardSelectionScreen) return (Read(view, "_options") as IEnumerable<CardCreationResult>)?.Select(c => c.Card).ToList() ?? new();
        if (view is NPlayerHand) return LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())?.PlayerCombatState?.Hand.Cards.ToList() ?? new();
        // Combat-pile selectors leave the base _cards empty; their live grid
        // owns the filtered, sorted cards and updates on pile contents changes.
        if (view is NCombatPileCardSelectScreen && Read(view, "_grid") is NCardGrid grid)
            return grid.CurrentlyDisplayedCards.Where(c => c != null).ToList();
        return (Read(view, "_cards") as IEnumerable<CardModel>)?.ToList() ?? new();
    }
    internal static MultiplayerChoiceView? Capture()
    {
        Node? view = NPlayerHand.Instance?.CurrentMode is NPlayerHand.Mode.SimpleSelect or NPlayerHand.Mode.UpgradeSelect ? NPlayerHand.Instance :
            NOverlayStack.Instance?.Peek() as Node;
        if (view is not NPlayerHand and not NSimpleCardSelectScreen and not NCombatPileCardSelectScreen and not NChooseACardSelectionScreen and not NDeckCardSelectScreen and not NDeckUpgradeSelectScreen and not NCardRewardSelectionScreen) return null;
        var player = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState()); if (player == null) return null;
        int slot = RunManager.Instance.DebugOnlyGetState()!.GetPlayerSlotIndex(player);
        var ids = RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds; if (slot >= ids.Count || ids[slot] == 0) return null;
        var cards = Cards(view); var prefs = Read(view, "_prefs") is CardSelectorPrefs p ? p : default;
        var selected = (Read(view, "_selectedCards") as IEnumerable<CardModel>)?.ToList() ?? new();
        var filter = (Read(view, "_currentSelectionFilter") ?? Read(view, "_filter")) as Func<CardModel, bool>;
        bool choose = view is NChooseACardSelectionScreen, canSkip = Read(view, "_canSkip") is true;
        return new MultiplayerChoiceView { Id = ids[slot] - 1, Kind = view is NPlayerHand ? NPlayerHand.Instance.CurrentMode == NPlayerHand.Mode.UpgradeSelect ? "Upgrade" : "Hand" : view is NSimpleCardSelectScreen ? "Grid" : view is NCombatPileCardSelectScreen ? "Pile" : view is NDeckCardSelectScreen ? "Deck" : view is NDeckUpgradeSelectScreen ? "DeckUpgrade" : view is NCardRewardSelectionScreen ? "Reward" : "Choose",
            Prompt = choose ? new LocString("gameplay_ui", "CHOOSE_CARD_HEADER").GetFormattedText() : prefs.Prompt?.GetFormattedText() ?? "",
            Min = choose ? canSkip ? 0 : 1 : prefs.MinSelect, Max = choose ? 1 : prefs.MaxSelect, Manual = prefs.RequireManualConfirmation, Cancelable = choose ? canSkip : prefs.Cancelable,
            Confirm = Read(view, view is NPlayerHand ? "_selectModeConfirmButton" : "_confirmButton") is NClickableControl confirm && confirm.IsEnabled,
            CanSkip = canSkip,
            Preview = new[] { "_previewContainer", "_upgradeSinglePreviewContainer", "_upgradeMultiPreviewContainer" }.Any(f => Read(view, f) is Control { Visible: true }),
            Cards = cards.Select(c => MirrorState.Pack(c.ToSerializable())).ToList(), Eligible = cards.Select((c, i) => (c, i)).Where(c => filter?.Invoke(c.c) ?? true).Select(c => c.i).ToList(),
            Selected = selected.Select(c => cards.IndexOf(c)).Where(i => i >= 0).ToList() };
    }
    internal static void Apply(MultiplayerChoiceView? source)
    {
        if (!Replica || source == null || _view == null || !GodotObject.IsInstanceValid(_view) || source.Id != _choice || source.Kind != _kind) return;
        var cards = Cards(_view);
        if (cards.Count != source.Cards.Count || !cards.Select(c => MirrorState.Pack(c.ToSerializable())).Zip(source.Cards).All(p => p.First.SequenceEqual(p.Second))) return;
        var selected = (Read(_view, "_selectedCards") as IEnumerable<CardModel>)?.ToList() ?? new();
        var desired = source.Selected.Where(i => i >= 0 && i < cards.Count).Select(i => cards[i]).ToList();
        bool same = selected.SequenceEqual(desired);
        if (same && source.Kind is not "Deck" and not "DeckUpgrade") return;
        _applying++;
        try
        {
            if (_view is NPlayerHand hand)
            {
                foreach (var card in selected.Except(desired).ToArray())
                    if (_kind == "Hand") Call(Read(hand, "_selectedHandCardContainer")!, "DeselectCard", card);
                    else hand.DeselectCard(NCard.Create(card));
                foreach (var card in desired.Except(selected))
                    if (hand.ActiveHolders.FirstOrDefault(h => h.CardModel == card) is { } holder) Call(hand, _kind == "Hand" ? "SelectCardInSimpleMode" : "SelectCardInUpgradeMode", holder);
                if (Read(hand, "_selectedCards") is List<CardModel> list) { list.Clear(); list.AddRange(desired); }
                if (_kind == "Hand" && Read(hand, "_selectedHandCardContainer") is Node container)
                {
                    var holders = container.GetChildren().OfType<NSelectedHandCardHolder>().ToList();
                    for (int i = 0; i < desired.Count; i++)
                        if (holders.FirstOrDefault(h => h.CardModel == desired[i]) is { } holder) container.MoveChild(holder, i);
                    Call(container, "RefreshHolderPositions");
                }
            }
            else if (source.Kind is "Deck" or "DeckUpgrade")
            {
                bool preview = new[] { "_previewContainer", "_upgradeSinglePreviewContainer", "_upgradeMultiPreviewContainer" }.Any(f => Read(_view, f) is Control { Visible: true });
                // Native CancelSelection needs the previous selected card to
                // restore focus before it clears the set. Cancel first.
                if (!source.Preview && preview)
                {
                    Call(_view, "CancelSelection", (object)null!);
                    selected = (Read(_view, "_selectedCards") as IEnumerable<CardModel>)?.ToList() ?? new();
                }
                foreach (var card in selected.Except(desired).Concat(desired.Except(selected)).ToArray()) Call(_view, "OnCardClicked", card);
                preview = new[] { "_previewContainer", "_upgradeSinglePreviewContainer", "_upgradeMultiPreviewContainer" }.Any(f => Read(_view, f) is Control { Visible: true });
                if (source.Preview && !preview && _view is NDeckCardSelectScreen) _view.GetType().GetMethods(Any).First(m => m.Name == "PreviewSelection" && m.GetParameters().Length == 0).Invoke(_view, null);
            }
            else if (Read(_view, "_selectedCards") is HashSet<CardModel> set && Read(_view, "_grid") is { } grid)
            {
                foreach (var card in selected.Except(desired)) Call(grid, "UnhighlightCard", card);
                set.Clear(); foreach (var card in desired) { set.Add(card); Call(grid, "HighlightCard", card); }
                if (Read(_view, "_confirmButton") is NClickableControl button) { if (source.Confirm) button.Enable(); else button.Disable(); }
            }
        }
        finally { _applying--; }
    }
}
