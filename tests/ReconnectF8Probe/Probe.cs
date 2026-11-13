using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using Environment = System.Environment;

[ModInitializer(nameof(Initialize))]
[SupportedOSPlatform("windows")]
public static class ReconnectF8Probe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();
    private static Type Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).First(t => t != null)!;
    private static object? Session(string name) => Rmp("MultiplayerMirrorSession").GetProperty(name, Members)!.GetValue(null);
    private static object? SessionField(string name) => Rmp("MultiplayerMirrorSession").GetField(name, Members)!.GetValue(null);
    private static object? View => Rmp("LiveSharingController").GetField("_view", Members)!.GetValue(null);
    private static object? Get(object? target, string name) => target?.GetType().GetProperty(name, Members)!.GetValue(target);
    private static object? Replica => Session("Mirror");
    private static object ProcessOf(object mirror) => mirror.GetType().GetField("Process", Members)!.GetValue(mirror)!;
    private static bool Granted => Get(Get(Replica, "MultiplayerAuthority"), "Granted") is true;
    private static bool Visible => Get(Replica, "Verified") is true &&
        ProcessOf(Replica!).GetType().GetField("WindowVisible", Members)!.GetValue(ProcessOf(Replica!)) is true;
    private static object? _stalledReplica;
    private static bool _reloading;

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("RMP_047_PROBE") is { } output &&
            Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") != "renderer")
            TaskHelper.RunSafely(Run(output, Environment.GetEnvironmentVariable("RMP_047_ROLE") == "host"));
    }

    private static async Task Wait(Func<bool> ready, string stage)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed.TotalSeconds >= 60) throw new TimeoutException(stage);
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("[ReconnectF8Probe] " + stage);
    }

    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task PressF8()
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, Pressed = true });
        await Frames(5);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, Pressed = false });
        await Frames(5);
    }

    private static void Click(object target, string field)
        => ((NButton)target.GetType().GetField(field, Members)!.GetValue(target)!).ForceClick();

    private static bool Entered(RunState? previous = null) => RunManager.Instance.IsInProgress &&
        NRun.Instance?.IsNodeReady() == true && RunManager.Instance.DebugOnlyGetState() is { CurrentRoom: not null } state &&
        !ReferenceEquals(previous, state);

    private static bool PumpReplica(object __instance) => !ReferenceEquals(__instance, _stalledReplica);

    private static void ReloadHostPort(ref ushort __0)
    {
        if (_reloading && __0 == 33771) __0 = 33772;
    }

    private static void ReloadClientPort(ref ushort __2)
    {
        if (_reloading && __2 == 33771) __2 = 33772;
    }

    private static async Task Run(string output, bool host)
    {
        try
        {
            string root = Path.GetDirectoryName(output)!;
            string Marker(string name) => Path.Combine(root, name);
            void Mark(string name) => File.WriteAllText(Marker(name), "ready");
            var checks = new System.Collections.Generic.List<string>();
            async Task Check(Func<bool> ready, string name)
            {
                await Wait(ready, name);
                checks.Add(name);
            }

            await Wait(() => NGame.Instance?.MainMenu?.IsNodeReady() == true && SaveManager.Instance.IsProfileInitialized, "main menu");
            SaveManager.Instance.SetFtuesEnabled(false);
            foreach (string epoch in EpochModel.AllEpochIds) SaveManager.Instance.Progress.ObtainEpochOverride(epoch, EpochState.Revealed);
            SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<Ironclad>().Id).TotalWins = 1;

            var menu = NGame.Instance!.MainMenu!;
            var multiplayer = menu.OpenMultiplayerSubmenu();
            if (host)
            {
                await Frames(15);
                Click(multiplayer, "_hostButton");
                await Wait(() => menu.SubmenuStack.Peek() is NMultiplayerHostSubmenu or NCharacterSelectScreen, "host mode menu");
                if (menu.SubmenuStack.Peek() is NMultiplayerHostSubmenu modes)
                {
                    await Frames(15);
                    Click(modes, "_standardButton");
                }
                await Wait(() => menu.SubmenuStack.Peek() is NCharacterSelectScreen, "host listening");
                Mark("host-listening");
            }
            else
            {
                await Wait(() => File.Exists(Marker("host-listening")), "host listening");
                multiplayer.GetType().GetMethod("OnJoinFriendsPressed", Members)!.Invoke(multiplayer, null);
            }
            await Wait(() => menu.SubmenuStack.Peek() is NCharacterSelectScreen screen && screen.Lobby.Players.Count == 2, "native two-player lobby");
            ((NCharacterSelectScreen)menu.SubmenuStack.Peek()!).Lobby.SetReady(true);
            await Wait(() => Entered(), "native multiplayer run");
            var original = RunManager.Instance.DebugOnlyGetState()!;
            ulong peer = original.Players.First(p => p.NetId != RunManager.Instance.NetService.NetId).NetId;
            await Frames(30);
            await PressF8();
            await Check(() => View != null && Visible && (ulong)Session("Selected")! == peer, "initial F8 shows the other player");
            if (host)
            {
                View!.GetType().GetMethod("SetPreferredControl", Members)!.Invoke(View, new object[] { true });
                await Check(() => Granted, "host gained control of the friend before the drop");
                await Wait(() => File.Exists(Marker("client-showing")), "client is also showing the live window");
                if (SaveManager.Instance.CurrentRunSaveTask is { } save) await save;
                ((NetHostGameService)RunManager.Instance.NetService).DisconnectClient(peer, NetError.Timeout);
                await Check(() => ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers.Count == 0 &&
                    View == null && Replica == null && (ulong)Session("Selected")! == 0 &&
                    Session("ControlRequested") is false, "timeout clears viewer subscription and window");
                await Frames(60);
                await NGame.Instance.ReturnToMainMenu();
            }
            else
            {
                Mark("client-showing");
                await Check(() => Replica == null && View == null && (ulong)SessionField("_controlViewer")! == 0, "disconnected client revokes source permission");
                if (NGame.Instance.MainMenu == null) await NGame.Instance.ReturnToMainMenu();
            }

            await Wait(() => NGame.Instance.MainMenu?.IsNodeReady() == true, "menu after drop");
            // Use a fresh loopback port for the second lobby. The game may keep
            // the old ENet socket alive while its prior peers finish disconnecting.
            var ports = new Harmony("RMP.ReconnectF8Probe.Ports");
            _reloading = true;
            ports.Patch(typeof(NetHostGameService).GetMethod(nameof(NetHostGameService.StartENetHost))!,
                prefix: new HarmonyMethod(typeof(ReconnectF8Probe).GetMethod(nameof(ReloadHostPort), Members)!));
            ports.Patch(typeof(ENetClientConnectionInitializer).GetConstructor(new[] { typeof(ulong), typeof(string), typeof(ushort) })!,
                prefix: new HarmonyMethod(typeof(ReconnectF8Probe).GetMethod(nameof(ReloadClientPort), Members)!));
            menu = NGame.Instance.MainMenu!;
            multiplayer = menu.OpenMultiplayerSubmenu();
            if (host)
            {
                await Frames(20);
                Click(multiplayer, "_loadButton");
                await Wait(() => menu.SubmenuStack.Peek() is NMultiplayerLoadGameScreen, "host loaded-run lobby");
                Mark("reload-listening");
            }
            else
            {
                await Wait(() => File.Exists(Marker("reload-listening")), "host loaded-run lobby");
                multiplayer.GetType().GetMethod("OnJoinFriendsPressed", Members)!.Invoke(multiplayer, null);
            }
            await Wait(() => menu.SubmenuStack.Peek() is NMultiplayerLoadGameScreen screen &&
                ((LoadRunLobby)screen.GetType().GetField("_runLobby", Members)!.GetValue(screen)!).PlayerIds.Count() == 2,
                "two original players in loaded-run lobby");
            var loaded = menu.SubmenuStack.Peek()!;
            ((LoadRunLobby)loaded.GetType().GetField("_runLobby", Members)!.GetValue(loaded)!).SetReady(true);
            await Check(() => Entered(original), "native multiplayer save reloaded");
            _reloading = false;
            await Frames(30);
            if (View != null) await PressF8(); // The client may have remembered auto-open.
            await PressF8();
            await Check(() => View != null && Visible && (ulong)Session("Selected")! == peer, "F8 shows the reconnected friend");

            if (host)
            {
                await Wait(() => Granted, "new session independently grants control");
                var preferences = Rmp("SpectatorPreferences").GetProperty("Current", Members)!.GetValue(null)!;
                preferences.GetType().GetProperty("MultiplayerControlMode", Members)!.SetValue(preferences, false);
                var old = Replica!;
                var oldProcess = ProcessOf(old);
                int oldPid = ((Process)Get(oldProcess, "Child")!).Id;
                string oldToken = (string)SessionField("_token")!;
                var wire = oldProcess.GetType().GetField("Wire", Members)!.GetValue(oldProcess)!;
                var harmony = new Harmony("RMP.ReconnectF8Probe");
                var tick = old.GetType().GetMethod("Tick", Members)!;
                harmony.Patch(tick, prefix: new HarmonyMethod(typeof(ReconnectF8Probe).GetMethod(nameof(PumpReplica), Members)!));
                try
                {
                    // Let the real renderer fill the pipe while the host stops draining it.
                    _stalledReplica = old;
                    await Check(() => ((string)Get(wire, "Error")!).Contains("Mirror receive backlog exceeded", StringComparison.Ordinal),
                        "real local pipe reached its bounded receive backlog");
                }
                finally
                {
                    _stalledReplica = null;
                    harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
                }
                await PressF8();
                await Check(() => View != null && Visible && !ReferenceEquals(old, Replica) &&
                    ((Process)Get(ProcessOf(Replica!), "Child")!).Id != oldPid &&
                    Get(oldProcess, "Child") == null && (string)SessionField("_token")! != oldToken && !Granted,
                    "one F8 replaces the failed child and resubscribes");
                Mark("recovery-checked");
            }
            else await Wait(() => File.Exists(Marker("recovery-checked")), "host pipe recovery completed");

            File.WriteAllText(output, JsonSerializer.Serialize(new { checks }));
            await Wait(() => File.Exists(Marker("host.json")) && File.Exists(Marker("client.json")), "both results saved");
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
