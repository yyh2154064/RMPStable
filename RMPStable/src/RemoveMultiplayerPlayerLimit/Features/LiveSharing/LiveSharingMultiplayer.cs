using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Runs;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static partial class LiveSharingController
{
    private static bool _viewMultiplayer;
    private static void ProcessMultiplayer(RunState? state, bool pressed)
    {
        if (!RunManager.Instance.IsInProgress || state == null || NRun.Instance?.IsNodeReady() != true)
        { if (_viewMultiplayer) HideView(); return; }
        if (pressed && _viewMultiplayer) { Close(); return; }
        if (_viewMultiplayer && (MultiplayerMirrorSession.Mirror == null || !MultiplayerMirrorSession.Sources().Any(p => p.Id == MultiplayerMirrorSession.Selected.ToString())))
        { HideView(); SpectatorPreferences.Current.Open = false; SpectatorPreferences.Save(); return; }
        if (_view == null && (pressed || SpectatorPreferences.Current.Open))
        {
            var choices = MultiplayerMirrorSession.Sources();
            var source = choices.FirstOrDefault(p => p.Id == SpectatorPreferences.Current.MultiplayerSourceId) ?? choices.FirstOrDefault(p => p.Id != RunManager.Instance.NetService.NetId.ToString()) ?? choices.FirstOrDefault();
            if (source != null) SelectMultiplayerSource(source.Id);
        }
        if (!_viewMultiplayer || _view == null || MultiplayerMirrorSession.Mirror is not { } mirror) return;
        _view.Update(new SpectatorSnapshot { SourceId = MultiplayerMirrorSession.Selected.ToString(), Sources = MultiplayerMirrorSession.Sources(),
            Room = MultiplayerMirrorSession.Latest?.Room ?? "", Page = MultiplayerMirrorSession.Latest?.Screen ?? "" }, mirror.Verified,
            MultiplayerMirrorSession.Error.Length > 0 ? MultiplayerMirrorSession.Error : mirror.Error);
    }
    internal static void OpenMultiplayer()
    { ProcessMultiplayer(RunManager.Instance.DebugOnlyGetState(), pressed: false); }
    internal static void SelectMultiplayerSource(string id)
        => ShowMultiplayerSource(id, false);
    private static void ShowMultiplayerSource(string id, bool rebuild)
    {
        if (!ulong.TryParse(id, out var source) || !MultiplayerMirrorSession.Sources().Any(p => p.Id == id)) return;
        HideView();
        if (rebuild || MultiplayerMirrorSession.Selected != source || MultiplayerMirrorSession.Mirror == null || MultiplayerMirrorSession.Mirror.Error.Length > 0)
            MultiplayerMirrorSession.Select(source);
        var top = NRun.Instance.GlobalUi.TopBar;
        float status = new Control[] { top.Hp, top.Gold, top.Portrait, top.Deck, top.Map, top.Pause }.Where(c => c.IsVisibleInTree()).Select(c => c.GetGlobalRect().End.Y).DefaultIfEmpty(80).Max();
        float relic = LocalSpectatorSource.Descendants<NRelicInventoryHolder>(NRun.Instance.GlobalUi.RelicInventory).Where(c => c.IsVisibleInTree()).Select(c => c.GetGlobalRect().End.Y).DefaultIfEmpty(0).Max();
        SpectatorPreferences.DefaultTop = Math.Max(status, relic) + 6;
        _view = new SpectatorView(Close, MultiplayerMirrorSession.Mirror!, _ => 0, SelectMultiplayerSource); _viewMultiplayer = true;
        SpectatorPreferences.Current.Open = true; SpectatorPreferences.Current.MultiplayerSourceId = id; SpectatorPreferences.Save();
    }
}
