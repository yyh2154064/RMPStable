using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using Environment = System.Environment;

[ModInitializer(nameof(Initialize))]
public static class Beta113StartupProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();
    private static Type? Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).FirstOrDefault(t => t != null);
    private static object? Host => Rmp("MirrorResident")?.GetProperty("Host", Members)?.GetValue(null);

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("RMP_046_PROBE") is { } output && Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") != "renderer")
            TaskHelper.RunSafely(Run(output));
    }

    private static async Task Wait(Func<bool> ready, List<double> frames, string stage)
    {
        var clock = Stopwatch.StartNew();
        double previous = 0;
        while (!ready())
        {
            if (clock.ElapsedMilliseconds >= 60_000) throw new TimeoutException(stage);
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            double now = clock.Elapsed.TotalMilliseconds;
            frames.Add(now - previous);
            previous = now;
        }
    }

    private static NButton Button(object target, string name) => (NButton)target.GetType().GetField(name, Members)!.GetValue(target)!;

    private static object Measure(List<double> frames)
    {
        var ordered = frames.OrderBy(ms => ms).ToArray();
        return new { count = ordered.Length, p95 = ordered.Length == 0 ? 0 : ordered[(int)Math.Ceiling(ordered.Length * .95) - 1], over33ms = ordered.Count(ms => ms > 33.33), over50ms = ordered.Count(ms => ms > 50) };
    }

    private static async Task Run(string output)
    {
        try
        {
            var startup = new List<double>();
            await Wait(() => NGame.Instance?.MainMenu?.IsNodeReady() == true && SaveManager.Instance.IsProfileInitialized, startup, "main menu");
            double menuUtcMs = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            bool childAtMenu = Host != null;
            var menu = NGame.Instance!.MainMenu!;
            if (Environment.GetEnvironmentVariable("RMP_046_NET_ROLE") is { } role)
            {
                UnlockTestEpochs();
                await RunMultiplayer(menu, role, output);
                Tree.Quit(0);
                return;
            }
            if (Environment.GetEnvironmentVariable("RMP_046_OPEN") == "1") UnlockTestEpochs();
            var selection = new List<double>();
            var clock = Stopwatch.StartNew();
            Button(menu, "_singleplayerButton").ForceClick();
            await Wait(() => menu.SubmenuStack.Peek() is NSingleplayerSubmenu or NCharacterSelectScreen, selection, "singleplayer menu");
            if (menu.SubmenuStack.Peek() is NSingleplayerSubmenu submenu) Button(submenu, "_standardButton").ForceClick();
            await Wait(() => selection.Count > 0 && menu.SubmenuStack.Peek() is NCharacterSelectScreen screen && screen.IsVisibleInTree() && Button(screen, "_embarkButton").IsEnabled, selection, "select a character");
            double selectMs = clock.Elapsed.TotalMilliseconds;
            int selectFrames = selection.Count;
            await Wait(() => selection.Count >= selectFrames + 120, selection, "settled character-selection frames");
            bool childAtSelection = Host != null;
            bool modLoaded = Rmp("LiveSharingController") != null;
            double? replicaMs = null;
            bool? quickSl = null;
            string? playedCard = null;
            if (Environment.GetEnvironmentVariable("RMP_046_OPEN") == "1")
            {
                var screen = (NCharacterSelectScreen)menu.SubmenuStack.Peek()!;
                SaveManager.Instance.SetFtuesEnabled(false);
                var buttons = (Control)screen.GetType().GetField("_charButtonContainer", Members)!.GetValue(screen)!;
                var fool = buttons.GetChildren().OfType<NCharacterSelectButton>().First(b => b.Character is Fool);
                if (fool.IsLocked) throw new InvalidOperationException("The isolated Fool unlock seed was not applied");
                screen.GetType().GetMethod("SelectCharacter", Members)!.Invoke(screen, new object[] { fool });
                Button(screen, "_embarkButton").ForceClick();
                var extra = new List<double>();
                await Wait(() => RunManager.Instance.IsInProgress && NRun.Instance?.IsNodeReady() == true, extra, "run entry");
                if (Host != null) throw new InvalidOperationException("Replica acquired while the spectator window was closed");
                var state = RunManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("Run state is missing");
                if (state.Players[0].Character is not Fool) throw new InvalidOperationException("The Fool was not selected");
                clock.Restart();
                Rmp("LiveSharingController")!.GetMethod("Open", Members)!.Invoke(null, new object[] { state });
                await Wait(() => Host is { } host && host.GetType().GetProperty("Verified", Members)?.GetValue(host) is true, extra, "replica verification");
                replicaMs = clock.Elapsed.TotalMilliseconds;

                NRun.Instance!.GlobalUi.TopBar.Pause.ForceClick();
                await Wait(() => NRun.Instance?.GlobalUi.SubmenuStack.Stack.Peek() is NPauseMenu pause && pause.GetNodeOrNull<NButton>("%ButtonContainer/RmpQuickSl") != null, extra, "Quick SL pause-menu button");
                var resident = Host;
                var slController = Rmp("LiveSharingController")!.Assembly.GetType("RemoveMultiplayerPlayerLimit.Features.QuickSl.QuickSlController")!;
                await (Task)slController.GetMethod("RunSingleplayerSlAsync", Members)!.Invoke(null, null)!;
                await Wait(() => !ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), state) && NRun.Instance?.IsNodeReady() == true &&
                    Host?.GetType().GetProperty("Verified", Members)?.GetValue(Host) is true, extra, "Quick SL native restore and mirror catch-up");
                quickSl = ReferenceEquals(resident, Host) && RunManager.Instance.DebugOnlyGetState()!.Players[0].Character is Fool;
                if (quickSl != true) throw new InvalidOperationException("Quick SL replaced the replica or lost The Fool");

                var restored = RunManager.Instance.DebugOnlyGetState()!;
                var point = restored.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster);
                await RunManager.Instance.EnterMapCoord(point.coord);
                var player = restored.Players[0];
                await Wait(() => CombatManager.Instance.IsInProgress && player.PlayerCombatState?.Hand.Cards.OfType<DefendFool>().Any() == true &&
                    Host?.GetType().GetProperty("Verified", Members)?.GetValue(Host) is true, extra, "The Fool combat and mirror");
                var defend = player.PlayerCombatState!.Hand.Cards.OfType<DefendFool>().First();
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(defend, null));
                await Wait(() => player.Creature.Block > 0 && !player.PlayerCombatState.Hand.Cards.Contains(defend) &&
                    Host?.GetType().GetProperty("Verified", Members)?.GetValue(Host) is true, extra, "The Fool card effect and replay");
                playedCard = defend.Id.ToString();
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { menuUtcMs, selectMs, childAtMenu, childAtSelection, modLoaded, startup = Measure(startup), selection = Measure(selection), replicaMs, quickSl, playedCard }));
            Tree.Quit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(output + ".error", error.ToString());
            GD.PushError(error.ToString());
            Tree.Quit(1);
        }
    }

    private static async Task RunMultiplayer(NMainMenu menu, string role, string output)
    {
        var frames = new List<double>();
        bool isHost = role == "host";
        var mode = Enum.Parse<GameMode>(Environment.GetEnvironmentVariable("RMP_046_MODE")!);
        string marker = Path.Combine(Path.GetDirectoryName(output)!, "host-ready");
        SaveManager.Instance.SetFtuesEnabled(false);
        if (isHost)
        {
            Button(menu, "_multiplayerButton").ForceClick();
            await Wait(() => menu.SubmenuStack.Peek() is NMultiplayerSubmenu, frames, "multiplayer menu");
            ((NMultiplayerSubmenu)menu.SubmenuStack.Peek()!).GetNode<NButton>("ButtonContainer/HostButton").ForceClick();
            await Wait(() => menu.SubmenuStack.Peek() is NMultiplayerHostSubmenu || ActiveLobby(menu)?.GameMode == mode, frames, "host mode menu");
            if (menu.SubmenuStack.Peek() is NMultiplayerHostSubmenu modes)
            {
                await WaitFrames(12);
                string button = mode switch { GameMode.Blitz => "_blitzButton", GameMode.Dread => "_dreadButton", _ => "_standardButton" };
                Button(modes, button).ForceClick();
            }
            await Wait(() => ActiveLobby(menu)?.GameMode == mode, frames, "host lobby");
            var lobby = ActiveLobby(menu)!;
            if (lobby.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host ||
                (int)typeof(StartRunLobby).GetField("_maxPlayers", Members)!.GetValue(lobby)! != 16)
                throw new InvalidOperationException("The host did not create the expected extended lobby");
            if (mode == GameMode.Blitz)
            {
                // Use the test seed without sending the seed-edit event disabled by Blitz.
                typeof(StartRunLobby).GetProperty(nameof(StartRunLobby.Seed), Members)!.SetValue(lobby, "7FZGNXAWE7K7");
            }
            File.WriteAllText(marker, "ready");
        }
        else
        {
            await Wait(() => File.Exists(marker), frames, "host readiness");
            // The native join screen automatically connects to localhost when Steam is off.
            var multiplayer = menu.OpenMultiplayerSubmenu();
            multiplayer.GetType().GetMethod("OnJoinFriendsPressed", Members)!.Invoke(multiplayer, null);
        }
        await Wait(() => ActiveLobby(menu)?.Players.Count == 2 && ActiveLobby(menu)?.GameMode == mode, frames, "two players joined the intended lobby");
        var active = ActiveLobby(menu)!;
        var highestSlot = active.Players[0];
        highestSlot.slotId = 15;
        var writer = new PacketWriter();
        writer.Write(highestSlot);
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        if (reader.Read<MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer>().slotId != 15)
            throw new InvalidOperationException("Native lobby packets truncated slot 15");
        active.SetReady(true);
        await Wait(() => RunManager.Instance.IsInProgress && NRun.Instance?.IsNodeReady() == true && RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null, frames, "multiplayer run entered");
        var state = RunManager.Instance.DebugOnlyGetState()!;
        if (state.GameMode != mode || state.Players.Count != 2) throw new InvalidOperationException("Run mode or player count diverged");
        if (Host != null) throw new InvalidOperationException("Unexpected multiplayer replica before opening the window");
        string source = state.Players.First(p => p.NetId != RunManager.Instance.NetService.NetId).NetId.ToString();
        var clock = Stopwatch.StartNew();
        Rmp("LiveSharingController")!.GetMethod("SelectMultiplayerSource", Members)!.Invoke(null, new object[] { source });
        await Wait(() => Host is { } host && host.GetType().GetProperty("Verified", Members)?.GetValue(host) is true, frames, "multiplayer mirror verification");
        double replicaMs = clock.Elapsed.TotalMilliseconds;
        string opening = state.CurrentRoom!.GetType().Name;
        string? purchase = null;
        if (mode == GameMode.Standard)
        {
            var eventModel = ModelDb.Event<WuppyInTrouble>();
            await RunManager.Instance.EnterRoom(new EventRoom(eventModel));
            await WaitFrames(30);
            await Wait(() => state.CurrentRoom is EventRoom room && room.ModelId == eventModel.Id &&
                Host is { } host && host.GetType().GetProperty("Verified", Members)?.GetValue(host) is true,
                frames, "new event mirror");
        }
        else if (mode == GameMode.Blitz)
        {
            // Exercise the new native shop and synchronize its inventory under the same two-player run.
            await RunManager.Instance.EnterRoom(new MerchantRoom());
            await WaitFrames(30);
            await Wait(() => NRun.Instance?.MerchantRoom is { } merchant && merchant.GetType().Name == "NBlitzMerchantRoom" &&
                Host is { } host && host.GetType().GetProperty("Verified", Members)?.GetValue(host) is true,
                frames, "Blitz merchant replay");
            if (isHost)
            {
                var inventory = RunManager.Instance.MerchantSynchronizer.GetLocalInventory();
                var entry = inventory.BlitzEntries.FirstOrDefault(e => e is BlitzMerchantFullHealAndMaxHpEntry or BlitzMerchantRandomCardUpgradesEntry or
                    BlitzMerchantPotionsEntry or BlitzMerchantTwoRelicsEntry);
                if (entry == null)
                    throw new InvalidOperationException("The Blitz test seed did not offer a direct purchase");
                int choices = inventory.BlitzChoicesRemaining;
                var buying = RunManager.Instance.MerchantSynchronizer.PurchaseLocalEntry(entry);
                await Wait(() => buying.IsCompleted || NOverlayStack.Instance?.Peek() is NRewardsScreen ready && Button(ready, "_proceedButton").IsEnabled, frames, "Blitz purchase or reward screen");
                if (!buying.IsCompleted && NOverlayStack.Instance?.Peek() is NRewardsScreen rewards)
                    Button(rewards, "_proceedButton").ForceClick();
                if (!await buying.WaitAsync(TimeSpan.FromSeconds(25)))
                    throw new InvalidOperationException("The native Blitz purchase failed");
                await Wait(() => inventory.BlitzChoicesRemaining < choices, frames, "Blitz purchase applied");
                purchase = entry.GetType().Name;
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "blitz-purchase"), purchase);
            }
            else
            {
                string markerPath = Path.Combine(Path.GetDirectoryName(output)!, "blitz-purchase");
                await Wait(() => File.Exists(markerPath), frames, "host purchase outcome");
                purchase = File.ReadAllText(markerPath);
                await WaitFrames(30);
                await Wait(() => Host is { } host && host.GetType().GetProperty("Verified", Members)?.GetValue(host) is true,
                    frames, "Blitz purchase replay");
            }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { role, mode = mode.ToString(), players = state.Players.Count, opening, room = state.CurrentRoom?.GetType().Name, model = state.CurrentRoom?.ModelId?.ToString(), replicaMs, purchase }));
        await Wait(() => File.Exists(Path.Combine(Path.GetDirectoryName(output)!, "host.json")) && File.Exists(Path.Combine(Path.GetDirectoryName(output)!, "client.json")), frames, "peer result");
    }

    private static async Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }

    private static StartRunLobby? ActiveLobby(NMainMenu menu) => menu.SubmenuStack.Peek() switch
    {
        NCharacterSelectScreen standard => standard.Lobby,
        NBlitzScreen blitz => blitz.Lobby,
        NDreadScreen dread => dread.Lobby,
        _ => null
    };

    private static void UnlockTestEpochs()
    {
        foreach (string epoch in EpochModel.AllEpochIds)
            SaveManager.Instance.Progress.ObtainEpochOverride(epoch, EpochState.Revealed);
        if (SaveManager.Instance.Progress.NumberOfRuns == 0)
            SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<Ironclad>().Id).TotalWins = 1;
    }
}
