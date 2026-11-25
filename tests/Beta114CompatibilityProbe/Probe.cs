using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.TreasureRooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using Environment = System.Environment;

[ModInitializer(nameof(Initialize))]
[SupportedOSPlatform("windows")]
public static class Beta114CompatibilityProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string LiveSharing = "RemoveMultiplayerPlayerLimit.Features.LiveSharing.";
    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("RMP_049_PROBE") is { } output &&
            Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") != "renderer")
            TaskHelper.RunSafely(Run(output, Environment.GetEnvironmentVariable("RMP_049_ROLE") == "host"));
    }

    private static async Task Wait(Func<bool> ready, string stage, Func<string>? describe = null)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed.TotalSeconds >= 55) throw new TimeoutException(stage + (describe == null ? "" : ". " + describe()));
            await Frames(1);
        }
        GD.Print("[Beta114CompatibilityProbe] " + stage);
    }

    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(5);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(5);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Members)!.GetValue(target)!;

    private static T Property<T>(object target, string name) =>
        (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static bool ShowsPeer(Type session, ulong peer)
    {
        if (session.GetProperty("Selected", Members)!.GetValue(null) is not ulong selected || selected != peer)
            return false;
        object? mirror = session.GetProperty("Mirror", Members)!.GetValue(null);
        return mirror != null && Property<bool>(mirror, "Verified") &&
            Field<bool>(Field<object>(mirror, "Process"), "WindowVisible");
    }

    private static Assembly CheckLoad(Action<bool, string> check)
    {
        var mod = ModManager.Mods.Single(entry => entry.manifest?.id == "RMPStable");
        check(mod.state == ModLoadState.Loaded && Tree.Root.HasNode("RMPController"), "mod initializer and controller loaded");
        Assembly bootstrap = mod.assemblies.Single(a => a.GetName().Name == "RMPStable");
        Assembly payload = mod.assemblies.Single(a => a.GetName().Name == "RMPStablePayload_v0114");
        check(bootstrap.GetName().Version?.ToString() == "0.4.9.0" &&
            payload.GetName().Version?.ToString() == "0.4.9.0", "bootstrap selected the v0.114.0 payload");

        var select = bootstrap.GetType("RMPStable.Bootstrap.ModEntry", throwOnError: true)!
            .GetMethod("GetPayloadResourceName", Members)!;
        foreach (var (version, suffix) in new[] { ("v0.107.1", "v0107"), ("v0.111.0", "v0111"),
            ("v0.113.0", "v0113"), ("v0.113.1", "v01131"), ("v0.114.0", "v0114") })
        {
            string resource = $"RMPStable.Payload.{suffix}.dll";
            check((string?)select.Invoke(null, new object[] { version }) == resource &&
                bootstrap.GetManifestResourceInfo(resource) != null, $"{version} maps to its embedded payload");
        }
        bool rejected = false;
        try { select.Invoke(null, new object[] { "v0.114.99" }); }
        catch (TargetInvocationException error) when (error.InnerException is NotSupportedException) { rejected = true; }
        check(rejected, "unknown game version is rejected");

        var patches = Harmony.GetPatchInfo(typeof(StartRunLobbyPlayer).GetMethod("Serialize")!);
        check(patches?.Transpilers.Any(p => p.owner == "RMPStable.ExtendedNativeLobby") == true,
            "native lobby packet Harmony patch is installed");
        Type mirrorMessage = payload.GetTypes().Single(type => type.Name == "RmpMirrorPacket");
        var typeToId = typeof(MessageTypes).GetMethods().Single(method => method.Name == "TypeToId" && method.IsGenericMethodDefinition);
        int id = (int)typeToId.MakeGenericMethod(mirrorMessage).Invoke(null, null)!;
        check(MessageTypes.TryGetMessageType(id, out Type? registered) && registered == mirrorMessage,
            "the native message map contains the mirror packet");
        check(payload.GetType("RemoveMultiplayerPlayerLimit.Network.RmpSkipRelicNetAction") == null,
            "new payload uses the native round-aware relic action");
        return payload;
    }

    private static async Task CheckPreview(Assembly payload, CardModel card, Action<bool, string> check)
    {
        var inspector = NGame.Instance!.GetInspectCardScreen();
        using var source = (IDisposable)Activator.CreateInstance(
            payload.GetType(LiveSharing + "LocalSpectatorSource", throwOnError: true)!, nonPublic: true)!;
        MethodInfo captureCard = source.GetType().GetMethod("CaptureCard", Members)!;

        int originalLevel = card.CurrentUpgradeLevel;
        string originalText = card.GetDescriptionForPile(PileType.None);
        object snapshot = captureCard.Invoke(source, new object?[] { card, PileType.None, null, false, true })!;
        object upgraded = Property<object>(snapshot, "Upgrade");
        inspector.Open(new List<CardModel> { card }, 0, true);
        var nativeCard = Field<NCard>(inspector, "_card");
        await Wait(() => inspector.IsVisibleInTree() && nativeCard.Model?.IsUpgraded == true, "native upgrade inspection");
        var native = nativeCard.Model!;
        check(Property<bool>(upgraded, "Upgraded") &&
            Property<string>(upgraded, "Description") == native.GetDescriptionForUpgradePreview(),
            "mirror upgrade preview matches native card inspection");
        inspector.Close();
        await Wait(() => !inspector.IsVisibleInTree(), "native card inspector closed");
        check(card.CurrentUpgradeLevel == originalLevel && card.GetDescriptionForPile(PileType.None) == originalText,
            "preview leaves the source card unchanged");

        var sourceUpgraded = (CardModel)ModelDb.Card<DingyDagger>().MutableClone();
        string daggerBaseText = sourceUpgraded.GetDescriptionForPile(PileType.None);
        for (int level = 0; level < 2; level++)
        {
            sourceUpgraded.UpgradeInternal();
            sourceUpgraded.FinalizeUpgradeInternal();
        }
        snapshot = captureCard.Invoke(source, new object?[] { sourceUpgraded, PileType.None, null, false, true })!;
        object unupgraded = Property<object>(snapshot, "Base");
        check(!Property<bool>(unupgraded, "Upgraded") &&
            Property<string>(unupgraded, "Description") == daggerBaseText && sourceUpgraded.CurrentUpgradeLevel == 2,
            "base preview strips both Dingy Dagger upgrades from the copy");
    }

    private static async Task CheckHotkeys(Type session, ulong peer, Action<bool, string> check)
    {
        var hotkeys = NHotkeyManager.Instance!;
        var run = NRun.Instance!;
        var capstone = NCapstoneContainer.Instance!;
        const string action = "rmp_049_probe_background";
        Action background = () => { };
        hotkeys.PushHotkeyPressedBinding(action, run, background);
        try
        {
            check(hotkeys.GetFiringPressedBinding(action) == background, "gameplay hotkey can fire in the run");
            run.GlobalUi.TopBar.Pause.ForceClick();
            await Wait(() => capstone.CurrentCapstoneScreen is Control,
                "native pause menu opened");
            check(hotkeys.GetFiringPressedBinding(action) != background, "pause menu blocks the background native hotkey");
            capstone.Close();
            await Wait(() => capstone.CurrentCapstoneScreen == null, "pause menu closed");

            var modal = NModalContainer.Instance!;
            var popup = NErrorPopup.Create("Compatibility probe", "Isolated hotkey target", false)!;
            modal.Add(popup, showBackstop: true);
            await Wait(() => popup.IsVisibleInTree(), "native error popup opened");
            check(hotkeys.GetFiringPressedBinding(action) != background, "error popup blocks the background native hotkey");
            await Press(Key.F5);
            check(ReferenceEquals(modal.OpenModal, popup), "F5 leaves an existing native popup in place");
            await Press(Key.F8);
            await Wait(() => !ShowsPeer(session, peer), "F8 hides the local renderer over a popup");
            check(ReferenceEquals(modal.OpenModal, popup), "F8 closes only the local renderer");
            modal.ClearIfOpen(popup);
            await Wait(() => modal.OpenModal == null, "native error popup closed");
            await Wait(() => hotkeys.GetFiringPressedBinding(action) == background, "native hotkey resumes after overlays");
            await Press(Key.F5);
            await Wait(() => modal.OpenModal is NGenericPopup confirmation && confirmation.IsVisibleInTree(), "F5 confirmation on the run");
            var cancel = Descendants<NButton>((Node)modal.OpenModal!).Single(button => button.Name == "NoButton");
            cancel.ForceClick();
            await Wait(() => modal.OpenModal == null, "F5 confirmation cancelled");
            check(NRun.Instance == run && RunManager.Instance.IsInProgress, "F5 cancellation leaves the original run active");
        }
        finally
        {
            hotkeys.RemoveHotkeyPressedBinding(action, run, background);
        }
    }

    private static async Task Run(string output, bool host)
    {
        try
        {
            var checks = new List<string>();
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                checks.Add(name);
            }
            string root = Path.GetDirectoryName(output)!;
            string Marker(string stage, bool forHost) => Path.Combine(root, stage + (forHost ? "-host" : "-client"));
            async Task Barrier(string stage)
            {
                File.WriteAllText(Marker(stage, host), "ready");
                await Wait(() => File.Exists(Marker(stage, !host)), stage);
            }

            await Wait(() => NGame.Instance?.MainMenu?.IsNodeReady() == true && SaveManager.Instance.IsProfileInitialized, "main menu");
            Assembly payload = CheckLoad(Check);
            SaveManager.Instance.SetFtuesEnabled(false);
            foreach (string epoch in EpochModel.AllEpochIds)
                SaveManager.Instance.Progress.ObtainEpochOverride(epoch, EpochState.Revealed);
            SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<Ironclad>().Id).TotalWins = 1;
            var menu = NGame.Instance!.MainMenu!;
            var multiplayer = menu.OpenMultiplayerSubmenu();
            if (host)
            {
                await Frames(15);
                Field<NButton>(multiplayer, "_hostButton").ForceClick();
                await Wait(() => menu.SubmenuStack.Peek() is NMultiplayerHostSubmenu, "host mode menu");
                await Frames(15);
                Field<NButton>(menu.SubmenuStack.Peek()!, "_blitzButton").ForceClick();
                await Wait(() => menu.SubmenuStack.Peek() is NBlitzScreen, "host Blitz listening");
                File.WriteAllText(Marker("listening", true), "ready");
            }
            else
            {
                await Wait(() => File.Exists(Marker("listening", true)), "host listening");
                multiplayer.GetType().GetMethod("OnJoinFriendsPressed", Members)!.Invoke(multiplayer, null);
            }
            await Wait(() => menu.SubmenuStack.Peek() is NBlitzScreen screen && screen.Lobby.Players.Count == 2, "native two-player Blitz lobby");
            Check(true, "both peers joined the native ENet Blitz lobby");
            ((NBlitzScreen)menu.SubmenuStack.Peek()!).Lobby.SetReady(true);
            await Wait(() => NRun.Instance?.IsNodeReady() == true &&
                RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null, "native multiplayer run");

            var runManager = RunManager.Instance;
            var state = runManager.DebugOnlyGetState()!;
            var me = state.Players.Single(player => player.NetId == runManager.NetService.NetId);
            var peer = state.Players.Single(player => player.NetId != runManager.NetService.NetId);
            Check(state.GameMode == GameMode.Blitz && state.Players.Count == 2, "both peers entered the native Blitz run");
            await Frames(30);
            var session = payload.GetType(LiveSharing + "MultiplayerMirrorSession", throwOnError: true)!;
            await Press(Key.F8);
            await Wait(() => ShowsPeer(session, peer.NetId), "F8 shows peer in an authenticated native renderer");
            Check(true, "F8 opens the authenticated peer renderer");

            CardModel card = me.Deck.Cards.First(c => !c.IsUpgraded && c.IsUpgradable);
            await CheckPreview(payload, card, Check);
            await CheckHotkeys(session, peer.NetId, Check);
            await Barrier("treasure-transition");
            await runManager.EnterRoomDebug(RoomType.Treasure, showTransition: false);
            await Barrier("treasure-entered");

            var room = NRun.Instance!.TreasureRoom!;
            var chest = Descendants<NTreasureButton>(room).First(button => button.IsVisibleInTree());
            await Wait(() => chest.IsEnabled, "native chest enabled");
            chest.ForceClick();
            var synchronizer = runManager.TreasureRoomRelicSynchronizer;
            Check(Field<int>(synchronizer, "_rounds") == 2, "Blitz uses the native two-round relic vote");
            var collection = Field<NTreasureRoomRelicCollection>(room, "_relicCollection");
            bool VotingReady() => synchronizer.CurrentRelics is { Count: >= 2 } relics &&
                collection.HoldersInUse.Count == relics.Count &&
                collection.HoldersInUse.All(holder => holder.IsVisibleInTree() && holder.IsEnabled);
            string VotingStatus() => $"relics={synchronizer.CurrentRelics?.Count}, " +
                $"holders={string.Join(",", collection.HoldersInUse.Select(h => $"{h.Index}/{h.IsVisibleInTree()}/{h.IsEnabled}"))}, " +
                $"skip={room.ProceedButton.IsSkip}/{room.ProceedButton.IsEnabled}, " +
                $"overlay={NOverlayStack.Instance?.Peek()?.GetType().Name}";
            await Wait(VotingReady, "first native relic choices ready", VotingStatus);

            var initialRelics = state.Players.ToDictionary(p => p.NetId, p => p.Relics.Count);
            await Barrier("first-round");
            if (host) collection.HoldersInUse[0].ForceClick();
            else synchronizer.SkipRelicLocally();
            int Round() => Field<int>(synchronizer, "_currentRound");
            var hostPlayer = host ? me : peer;
            var clientPlayer = host ? peer : me;
            await Wait(() => Round() == 1 && VotingReady() &&
                hostPlayer.Relics.Count == initialRelics[hostPlayer.NetId] + 1, "second native relic choices ready after first award", VotingStatus);
            Check(clientPlayer.Relics.Count == initialRelics[clientPlayer.NetId], "first round awards only the voting player");

            await Barrier("second-round");
            if (!host)
                runManager.ActionQueueSynchronizer.RequestEnqueue(new PickRelicAction(me, 0, 0));
            string sourceLog = Path.Combine(root, host ? "host" : "client", "source.log");
            bool LateVoteDiscarded()
            {
                using var stream = new FileStream(sourceLog, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
                    if (line.Contains("Discarding vote from player") && line.Contains("cast on round 0, but it's currently round 1"))
                        return true;
                return false;
            }
            await Wait(LateVoteDiscarded, "original queue discarded the late vote");
            await Barrier("late-vote-delivered");
            Check(Round() == 1 && synchronizer.GetPlayerVote(clientPlayer) is not { voteReceived: true } &&
                clientPlayer.Relics.Count == initialRelics[clientPlayer.NetId], "late vote cannot count toward the second round");

            if (host) synchronizer.SkipRelicLocally();
            else collection.HoldersInUse[0].ForceClick();
            await Wait(() => synchronizer.CurrentRelics == null &&
                state.Players.All(player => player.Relics.Count == initialRelics[player.NetId] + 1), "second native relic vote completed and awarded");
            Check(true, "both rounds granted one relic to the player who picked");
            MethodInfo hash = payload.GetType(LiveSharing + "MirrorState", throwOnError: true)!.GetMethod("MultiplayerHash", Members)!;
            string finalHash = (string)hash.Invoke(null, new object[] { state })!;
            object renderer = session.GetProperty("Mirror", Members)!.GetValue(null)!;
            await Wait(() => Property<bool>(renderer, "Verified") && Property<string>(renderer, "SourceHash") == finalHash,
                "hidden renderer finished the original relic awards and matched the source state");
            Check(true, "renderer relic outcomes converge before shutdown");
            File.WriteAllText(output, JsonSerializer.Serialize(new { checks, relics = state.Players.Select(player =>
                new { player.NetId, ids = player.Relics.Select(relic => relic.Id.ToString()).ToArray() }).ToArray() }));
            await Barrier("result-saved");
            Tree.Quit();
        }
        catch (Exception error)
        {
            File.WriteAllText(output + ".error", error.ToString());
            GD.PushError(error.ToString());
            Tree.Quit(1);
        }
    }
}
