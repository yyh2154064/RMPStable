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
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
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
public static class Beta1131LoadProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("RMP_048_PROBE") is { } output &&
            Environment.GetEnvironmentVariable("RMP_MULTI_ROLE") != "renderer")
            TaskHelper.RunSafely(Run(output, Environment.GetEnvironmentVariable("RMP_048_ROLE") == "host"));
    }

    private static async Task Wait(Func<bool> ready, string stage)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed.TotalSeconds >= 55) throw new TimeoutException(stage);
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("[Beta1131LoadProbe] " + stage);
    }

    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }

    private static void Click(object target, string field)
        => ((NButton)target.GetType().GetField(field, Members)!.GetValue(target)!).ForceClick();

    private static bool ShowsPeer(Assembly payload, ulong peer)
    {
        const string prefix = "RemoveMultiplayerPlayerLimit.Features.LiveSharing.";
        Type session = payload.GetType(prefix + "MultiplayerMirrorSession", throwOnError: true)!;
        if (session.GetProperty("Selected", Members)!.GetValue(null) is not ulong selected || selected != peer)
            return false;
        object? mirror = session.GetProperty("Mirror", Members)!.GetValue(null);
        if (mirror == null || mirror.GetType().GetProperty("Verified", Members)!.GetValue(mirror) is not true)
            return false;
        object process = mirror.GetType().GetField("Process", Members)!.GetValue(mirror)!;
        return process.GetType().GetField("WindowVisible", Members)!.GetValue(process) is true;
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
            string Marker(string name) => Path.Combine(root, name);

            await Wait(() => NGame.Instance?.MainMenu?.IsNodeReady() == true && SaveManager.Instance.IsProfileInitialized, "main menu");
            var mod = ModManager.Mods.Single(entry => entry.manifest?.id == "RMPStable");
            Check(mod.state == ModLoadState.Loaded && Tree.Root.HasNode("RMPController"), "mod initializer and controller loaded");
            Assembly bootstrap = mod.assemblies.Single(a => a.GetName().Name == "RMPStable");
            Assembly payload = mod.assemblies.Single(a => a.GetName().Name == "RMPStablePayload_v01131");
            Check(bootstrap.GetName().Version?.ToString() == "0.4.8.0" &&
                payload.GetName().Version?.ToString() == "0.4.8.0", "v0.4.8 bootstrap selected the dedicated v0.113.1 payload");

            var select = bootstrap.GetType("RMPStable.Bootstrap.ModEntry", throwOnError: true)!
                .GetMethod("GetPayloadResourceName", Members)!;
            foreach (var (version, suffix) in new[] { ("v0.107.1", "v0107"), ("v0.111.0", "v0111"), ("v0.113.0", "v0113"), ("v0.113.1", "v01131") })
            {
                string resource = $"RMPStable.Payload.{suffix}.dll";
                Check((string?)select.Invoke(null, new object[] { version }) == resource &&
                    bootstrap.GetManifestResourceInfo(resource) != null, $"{version} maps to its embedded payload");
            }
            bool unsupported = false;
            try
            {
                select.Invoke(null, new object[] { "v0.113.99" });
            }
            catch (TargetInvocationException error) when (error.InnerException is NotSupportedException)
            {
                unsupported = true;
            }
            Check(unsupported, "unknown game version is rejected");
            foreach (string method in new[] { "Serialize", "Deserialize" })
            {
                var patches = Harmony.GetPatchInfo(typeof(StartRunLobbyPlayer).GetMethod(method)!);
                Check(patches?.Transpilers.Any(patch => patch.owner == "RMPStable.ExtendedNativeLobby") == true,
                    $"native lobby {method} Harmony patch is installed");
            }
            var typeToId = typeof(MessageTypes).GetMethods().Single(method => method.Name == "TypeToId" && method.IsGenericMethodDefinition);
            foreach (string name in new[] { "RmpExtendedReadyStateMessage", "RmpMirrorPacket" })
            {
                Type message = payload.GetTypes().Single(type => type.Name == name);
                int id = (int)typeToId.MakeGenericMethod(message).Invoke(null, null)!;
                Check(ReflectionHelper.ModTypes.Contains(message) &&
                    MessageTypes.TryGetMessageType(id, out Type? registered) && registered == message,
                    $"{name} is registered in the game's message map");
            }

            SaveManager.Instance.SetFtuesEnabled(false);
            foreach (string epoch in EpochModel.AllEpochIds)
                SaveManager.Instance.Progress.ObtainEpochOverride(epoch, EpochState.Revealed);
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
                File.WriteAllText(Marker("host-listening"), "ready");
            }
            else
            {
                await Wait(() => File.Exists(Marker("host-listening")), "host listening");
                multiplayer.GetType().GetMethod("OnJoinFriendsPressed", Members)!.Invoke(multiplayer, null);
            }
            await Wait(() => menu.SubmenuStack.Peek() is NCharacterSelectScreen screen && screen.Lobby.Players.Count == 2, "native two-player lobby");
            checks.Add("both peers joined the native ENet lobby");
            ((NCharacterSelectScreen)menu.SubmenuStack.Peek()!).Lobby.SetReady(true);
            await Wait(() => NRun.Instance?.IsNodeReady() == true &&
                RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null, "native multiplayer run");
            checks.Add("both peers entered the run");
            ulong peer = RunManager.Instance.DebugOnlyGetState()!.Players.First(
                player => player.NetId != RunManager.Instance.NetService.NetId).NetId;
            await Frames(30);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, Pressed = true });
            await Frames(5);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, Pressed = false });
            await Wait(() => ShowsPeer(payload, peer), "F8 shows the peer in an authenticated native renderer");
            checks.Add("F8 opened the authenticated peer renderer");

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
