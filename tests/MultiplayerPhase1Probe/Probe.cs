using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Env = System.Environment;

[ModInitializer(nameof(Initialize))]
public static class MultiplayerPhase1Probe
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static bool Renderer => Env.GetEnvironmentVariable("RMP_MULTI_ROLE") == "renderer";
    private static bool Host => Env.GetEnvironmentVariable("RMP_PHASE1_ROLE") == "host";
    private static string Root => Env.GetEnvironmentVariable("RMP_PHASE1_EVIDENCE")!;
    private static string Role => Host ? "host" : "client";
    private static int _checks;
    private static Type Rmp(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Features.LiveSharing." + name)).First(t => t != null)!;
    private static object? Get(object? o, string name, Type? type = null) => (o?.GetType() ?? type)!.GetProperty(name, Any)?.GetValue(o) ?? (o?.GetType() ?? type)!.GetField(name, Any)?.GetValue(o);
    private static object? Session(string name) => Get(null, name, Rmp("MultiplayerMirrorSession"));
    private static object Mirror => Session("Mirror")!;
    private static object Process => Get(Mirror, "Process")!;
    private static bool Verified => Session("Mirror") is { } m && (bool)Get(m, "Verified")!;
    private static string Hash(RunState state) => (string)Rmp("MirrorState").GetMethod("MultiplayerHash", Any)!.Invoke(null, new object[] { state })!;
    private static async Task Frames(int n = 1) { var tree = (SceneTree)Engine.GetMainLoop(); for (int i = 0; i < n; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
    private static async Task Wait(Func<bool> condition, string label, int seconds = 90)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (Time.GetTicksMsec() < end) { if (condition()) return; if (Session("Error") is string e && e.Length > 0) throw new Exception(e); await Frames(); }
        throw new Exception("Timeout: " + label + " mirror=" + (Session("Mirror") is { } m ? Get(m, "Error") : "none"));
    }
    private static void Check(bool condition, string label)
    {
        File.AppendAllText(Path.Combine(Root, Role + "-checks.jsonl"), JsonSerializer.Serialize(new { label, passed = condition }) + "\n");
        if (!condition) throw new Exception(label); _checks++; GD.Print("[MultiplayerPhase1] PASS " + label);
    }
    private static void Write(string name, object value) { string p = Path.Combine(Root, name); File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(value)); File.Move(p + ".tmp", p, true); }
    private static JsonElement Read(string name) { try { using var s = new FileStream(Path.Combine(Root, name), FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var d = JsonDocument.Parse(s); return d.RootElement.Clone(); } catch { return default; } }
    private static async Task Barrier(string stage)
    { Write(Role + "-" + stage + "-barrier.json", new { ready = true }); await Wait(() => File.Exists(Path.Combine(Root, (Host ? "client" : "host") + "-" + stage + "-barrier.json")), stage); }
    public static void Initialize()
    {
        if (Env.GetEnvironmentVariable("RMP_PHASE1_TEST") != "1") return;
        if (Renderer) TaskHelper.RunSafely(Observe()); else TaskHelper.RunSafely(Run());
    }
    private static async Task Observe()
    {
        string p = Path.Combine(Env.GetEnvironmentVariable("RMP_MULTI_PROFILE_ROOT")!, "phase1-state.json");
        while (true)
        {
            await Frames(3); var state = RunManager.Instance.DebugOnlyGetState(); if (state == null) continue;
            try
            {
                var data = new { local = LocalContext.NetId, service = RunManager.Instance.NetService.Type.ToString(),
                    players = state.Players.Select(x => x.NetId).ToArray(), hand = LocalContext.GetMe(state)?.PlayerCombatState?.Hand.Cards.Select(c => c.Id.ToString()).ToArray(),
                    choices = RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds.ToArray(), nextAction = RunManager.Instance.ActionQueueSet.NextActionId,
                    save = RunManager.Instance.ShouldSave, nativeRun = NRun.Instance != null, nativeHand = NPlayerHand.Instance != null, portraits = Portraits() };
                File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(data)); File.Move(p + ".tmp", p, true);
            }
            catch (IOException) { }
        }
    }
    private static IEnumerable<NCreature> Creatures(Node node)
    { foreach (var child in node.GetChildren()) { if (child is NCreature creature) yield return creature; foreach (var nested in Creatures(child)) yield return nested; } }
    private static object[] Portraits() => NRun.Instance?.CombatRoom is { } room ? Creatures(room).Where(c => c.Entity.IsPlayer)
        .Select(c => (object)new { id = c.Entity.Player!.NetId, x = c.Position.X, index = c.GetIndex() }).ToArray() : Array.Empty<object>();
    private static bool Foreground(JsonElement portraits, ulong id)
    { var all = portraits.EnumerateArray().ToArray(); var selected = all.Single(p => p.GetProperty("id").GetUInt64() == id); return all.Length == 2 && all.All(p => p.GetProperty("x").GetSingle() <= selected.GetProperty("x").GetSingle() && p.GetProperty("index").GetInt32() <= selected.GetProperty("index").GetInt32()); }
    private static async Task Rejections(ulong remote)
    {
        int before = (int)Session("RejectedPackets")!;
        var frame = Activator.CreateInstance(Rmp("MultiplayerMirrorFrame"))!;
        frame.GetType().GetProperty("Kind")!.SetValue(frame, "subscribe"); frame.GetType().GetProperty("Build")!.SetValue(frame, new string('x', 50000));
        Rmp("MultiplayerMirrorSession").GetMethod("Send", Any)!.Invoke(null, new object[] { remote, Guid.NewGuid().ToString("N"), frame });
        await Wait(() => (int)Session("RejectedPackets")! > before, "fragmented incompatible subscription or obsolete reply rejected");
        Check(Session("Error") as string == "" && Session("Mirror") != null, "32 KB fragmentation rejects incompatible build and obsolete token without replacing active view");
        var packetType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Network.RmpMirrorPacket")).First(t => t != null)!;
        object packet = Activator.CreateInstance(packetType)!;
        packetType.GetField("Origin")!.SetValue(packet, 999UL); packetType.GetField("Destination")!.SetValue(packet, RunManager.Instance.NetService.NetId);
        before = (int)Session("RejectedPackets")!;
        Rmp("MultiplayerMirrorSession").GetMethod("Receive", Any)!.Invoke(null, new object[] { packet, remote });
        Check((int)Session("RejectedPackets")! == before + 1, "dispatch boundary rejects forged origin");
    }
    private static JsonElement Child()
    {
        try { using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine((string)Get(Process, "DirectoryPath")!, "phase1-state.json"))); return d.RootElement.Clone(); } catch { return default; }
    }
    private static async Task Converged(string stage, RunState state)
    {
        await Wait(() => Verified && (string)Get(Mirror, "SourceHash")! == Hash(state) && Child().ValueKind == JsonValueKind.Object, stage + " original peers and replica agree");
        Check((string)Get(Process, "LastHash")! == (string)Get(Mirror, "SourceHash")!, stage + " exact selected source hash");
        Write(Role + "-" + stage + "-state.json", new { ownHash = Hash(state), sourceHash = Get(Mirror, "SourceHash"), replicaHash = Get(Process, "LastHash"),
            events = Get(Process, "LastEvents"), source = Get(Session("Latest"), "Events"), child = Child() });
    }
    private static async Task Run()
    {
        int exit = 0; INetGameService? service = null;
        try
        {
            await Wait(() => NGame.Instance?.MainMenu != null, "main menu"); await Frames(60);
            Engine.MaxFps = 30; SaveManager.Instance.SetFtuesEnabled(false);
            Rmp("LiveSharingController").GetMethod("Suspend", Any)!.Invoke(null, null);
            var registration = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RemoveMultiplayerPlayerLimit.Network.PayloadMessageRegistration")).First(t => t != null)!;
            registration.GetMethod("EnsureRegistered", Any)!.Invoke(null, null);
            ushort port = ushort.Parse(Env.GetEnvironmentVariable("RMP_PHASE1_PORT")!);
            if (Host)
            {
                var host = new NetHostGameService(PeerVersionInfo.LocalDefault()); service = host;
                Check(!host.StartENetHost(port, 2).HasValue, "original ENet host started"); Write("host-listening.json", new { port });
                ulong end = Time.GetTicksMsec() + 60000;
                while (host.ConnectedPeers.Count == 0 && Time.GetTicksMsec() < end) { host.Update(); await Frames(); }
                Check(host.ConnectedPeers.Count == 1, "one original client connected without a replica slot");
                host.SetPeerReadyForBroadcasting(2);
            }
            else
            {
                await Wait(() => File.Exists(Path.Combine(Root, "host-listening.json")), "host listening");
                var client = new NetClientGameService(PeerVersionInfo.LocalDefault()); service = client; var transport = new ENetClient(client);
                client.Initialize(transport, MegaCrit.Sts2.Core.Platform.PlatformType.None);
                Check(!(await transport.ConnectToHost(2, "127.0.0.1", port)).HasValue, "original ENet client handshake");
            }
            var character = ModelDb.Character<Ironclad>();
            var players = new[] { Player.CreateForNewRun(character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 1), Player.CreateForNewRun(character, SaveManager.Instance.GenerateUnlockStateFromProgress(), 2) };
            var state = RunState.CreateForNewRun(players, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), Array.Empty<ModifierModel>(), GameMode.Standard, 0, "RMPMULTIPHASE1");
            foreach (var player in players)
            {
                player.AddRelicInternal(ModelDb.Relic<GamblingChip>().ToMutable(), silent: true);
                player.Deck.Clear(true); var grit = state.CreateCard<TrueGrit>(player); grit.UpgradeInternal(); grit.FinalizeUpgradeInternal(); player.Deck.AddInternal(grit, silent: true);
                for (int i = 0; i < 3; i++) player.Deck.AddInternal(state.CreateCard<DefendIronclad>(player), silent: true);
            }
            RunManager.Instance.SetUpTest(state, service, disableCombatStateSync: false, shouldSave: false);
            RunManager.Instance.GenerateRooms();
            await PreloadManager.LoadRunAssets(players.Select(p => p.Character)); RunManager.Instance.Launch();
            NGame.Instance.RootSceneContainer.SetCurrentScene(NRun.Create(state)); await RunManager.Instance.SetActInternal(0);
            var entry = RunManager.Instance.EnterMapCoord(state.Map.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
            await Wait(() => NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.SimpleSelect, "original Gambling Chip hook choice");
            await Frames(12);
            ((NClickableControl)typeof(NPlayerHand).GetField("_selectModeConfirmButton", Any)!.GetValue(NPlayerHand.Instance)!).ForceClick();
            await entry;
            await Wait(() => CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsStarting && players.All(p => p.PlayerCombatState?.Hand.Cards.Count == 4) && !RunManager.Instance.ActionExecutor.IsRunning, "native multiplayer combat starts");
            Check(RunManager.Instance.NetService.Type == (Host ? NetGameType.Host : NetGameType.Client), "real multiplayer service preserved");
            await Barrier("combat-ready"); await Frames(60);
            var replay = (CombatReplay)Get(null, "CurrentReplay", Rmp("MirrorState"))!;
            Check(replay.events.Any(e => e.eventType == CombatReplayEventType.HookAction) && replay.events.Any(e => e.eventType == CombatReplayEventType.PlayerChoice), "original replay contains HookAction and its actual choice result");
            Write(Role + "-initial.json", new { hash = Hash(state) }); await Barrier("initial");
            Check(Read((Host ? "client" : "host") + "-initial.json").GetProperty("hash").GetString() == Hash(state), "original peers agree before subscribing");
            Rmp("MultiplayerMirrorSession").GetMethod("Select", Any)!.Invoke(null, new object[] { Host ? 2UL : 1UL });
            await Converged("initial", state); var child = Child();
            Check(child.GetProperty("local").GetUInt64() == (Host ? 2UL : 1UL) && child.GetProperty("players").EnumerateArray().Select(x => x.GetUInt64()).SequenceEqual(new[] { 1UL, 2UL }), "selected view identity without player slot reordering");
            Check(child.GetProperty("service").GetString() == "Replay" && !child.GetProperty("save").GetBoolean() && child.GetProperty("nativeRun").GetBoolean() && child.GetProperty("nativeHand").GetBoolean(), "native passive replica with no room connection or saves");
            Check(Foreground(child.GetProperty("portraits"), Host ? 2UL : 1UL), "all native portraits present with selected player in front");
            Check(Foreground(JsonSerializer.SerializeToElement(Portraits()), Host ? 1UL : 2UL), "source retains its own native portrait in front");
            await Barrier("subscribed");
            await Rejections(Host ? 2UL : 1UL); await Barrier("rejections");
            var me = players[Host ? 0 : 1];
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState!.Hand.Cards.OfType<DefendIronclad>().First(), null));
            await Wait(() => players.All(p => p.PlayerCombatState!.DiscardPile.Cards.Count == 1) && !RunManager.Instance.ActionExecutor.IsRunning, "simultaneous plays applied by original host ordering");
            await Converged("simultaneous", state); await Barrier("simultaneous");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(me.PlayerCombatState.Hand.Cards.OfType<TrueGrit>().Single(), null));
            await Wait(() => NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.SimpleSelect, "both players await original hand choice");
            await Wait(() => Session("Latest") is { } f && (string)Get(f, "HandMode")! == "SimpleSelect", "remote personal hand choice metadata");
            await Converged("waiting-choice", state); Check(NPlayerHand.Instance.CurrentMode == NPlayerHand.Mode.SimpleSelect, "replica does not resolve the source player's choice");
            await Barrier("waiting-choice");
            var holder = NPlayerHand.Instance.ActiveHolders.First(h => h.CardModel is DefendIronclad);
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            ((NClickableControl)typeof(NPlayerHand).GetField("_selectModeConfirmButton", Any)!.GetValue(NPlayerHand.Instance)!).ForceClick();
            await Wait(() => players.All(p => p.PlayerCombatState!.ExhaustPile.Cards.Count == 1) && !RunManager.Instance.ActionExecutor.IsRunning, "choice results and resume messages execute for both players");
            await Converged("choice-completed", state);
            Check(Child().GetProperty("choices").EnumerateArray().Select(x => x.GetUInt32()).SequenceEqual(RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds), "every player choice counter matches native replica");
            Check(Child().GetProperty("nextAction").GetUInt32() == RunManager.Instance.ActionQueueSet.NextActionId, "action and resume numbering match source");
            await Barrier("choice-completed");
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, me.PlayerCombatState!.TurnNumber));
            await Wait(() => players.All(p => p.PlayerCombatState!.TurnNumber >= 2 && p.PlayerCombatState.Phase == PlayerTurnPhase.Play) && !RunManager.Instance.ActionExecutor.IsRunning, "original enemy turn and next player turn");
            await Converged("next-turn", state); await Barrier("next-turn");
            Check((string)Get(Process, "LastHash")! == Hash(state), "original peers and both native replicas converge after enemy turn");
            Rmp("MultiplayerMirrorSession").GetMethod("Stop", Any)!.Invoke(null, null);
            Check(Session("Mirror") == null, "unsubscribe closes only owned replica");
            Write(Role + "-result.json", new { passed = true, checks = _checks, transport = "original ENet", headless = true, receivedFrames = Session("ReceivedFrames"), sentFrames = Session("SentFrames") });
            await Barrier("done"); GD.Print("[MultiplayerPhase1] RESULT checks=" + _checks);
        }
        catch (Exception e) { exit = 1; Write(Role + "-result.json", new { passed = false, checks = _checks, error = e.ToString() }); GD.PrintErr("[MultiplayerPhase1] FAIL " + e); }
        finally { Rmp("MultiplayerMirrorSession").GetMethod("Stop", Any)!.Invoke(null, null); ((SceneTree)Engine.GetMainLoop()).Quit(exit); }
    }
}
