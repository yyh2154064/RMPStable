using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed partial class MirrorRenderer
{
    private sealed record BufferedCard(CardModel Card, Creature? Target, long Generation, long Epoch, int Turn, ulong Created);
    private readonly Queue<BufferedCard> _pendingCards = new();
    private CardModel? _submittedCard;
    private bool IsCardReserved(CardModel? card) => card != null && (ReferenceEquals(card, _submittedCard) || _pendingCards.Any(p => ReferenceEquals(p.Card, card)));
    private bool _authorityReady;
    private int _authorityEvents;
    private string _authorityHash = "";
    private bool CanBufferCard => _control && _sceneReady && _generation == _desiredGeneration && _error.Length == 0 &&
        !PresentationOpen && (!LocalSpectatorSource.PendingNativeChoice || _nativePlay != null && NTargetManager.Instance?.IsInSelection == true) && NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.Play &&
        !CombatManager.Instance.IsStarting && !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo &&
        _state?.Players[0].PlayerCombatState?.Phase == PlayerTurnPhase.Play;
    private void ProcessBufferedCards()
    {
        // Buffer identities, never screen coordinates or speculative game results.
        // Re-resolve the same card/target in the current verified state before sending.
        if (!CanBufferCard) { _pendingCards.Clear(); return; }
        var state = _state!.Players[0].PlayerCombatState!;
        if (_submittedCard != null && !state.Hand.Cards.Contains(_submittedCard)) _submittedCard = null;
        while (_pendingCards.TryPeek(out var pending))
        {
            if (pending.Generation != _generation || pending.Epoch != _epoch || pending.Turn != state.TurnNumber ||
                Time.GetTicksMsec() - pending.Created > 8000 || !state.Hand.Cards.Contains(pending.Card) || pending.Target is { IsAlive: false })
            { _pendingCards.Dequeue(); continue; }
            if (!NativeIdle || _waitingForAuthority || !_authorityReady || _authorityEvents != _events) return;
            var hash = MirrorState.Hash(_state);
            if (hash != _authorityHash) return;
            _pendingCards.Dequeue();
            if (!pending.Card.CanPlayTargeting(pending.Target)) continue;
            int target = pending.Target == null ? -1 : pending.Target.CombatState.Creatures.ToList().IndexOf(pending.Target);
            if (pending.Target != null && target < 0) continue;
            _submittedCard = pending.Card;
            SendIntent("play", state.Hand.Cards.ToList().IndexOf(pending.Card), target, hash, _events);
            return;
        }
    }
}
