using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using RemoveMultiplayerPlayerLimit.Features.CampfireLayout;
using RemoveMultiplayerPlayerLimit.Features.DifficultyScaling;
using RemoveMultiplayerPlayerLimit.Features.SettingsUI;
using RemoveMultiplayerPlayerLimit.Features.QuickSl;
using RemoveMultiplayerPlayerLimit.Features.ShopLayout;
using RemoveMultiplayerPlayerLimit.Features.TreasureRoom;
using RemoveMultiplayerPlayerLimit.Features.VictoryFlow;
using RemoveMultiplayerPlayerLimit.Infrastructure;
using RemoveMultiplayerPlayerLimit.Network;
using RemoveMultiplayerPlayerLimit.Platform;

namespace RemoveMultiplayerPlayerLimit.Core;

[ModInitializer("Initialize")]
public static class ModEntry
{
	internal const int VanillaMultiplayerHolderCount = 4;

	private static readonly List<IRMPModule> Modules = new List<IRMPModule>();

	private static Node? _root;

	public static void Initialize()
	{
		Log.Warn("[RMP Stable] Initializing standalone v0.3.5 for STS2 v0.107.1...");
		Modules.Clear();
		ConfigManager configManager = new ConfigManager();
		ReflectionCache cache = new ReflectionCache();
		int value = 16;
		int value2 = 32;
		Modules.Add(new DifficultyModule());
		Modules.Add(new CampfireModule());
		Modules.Add(new ShopModule());
		Modules.Add(new TreasureModule());
		Modules.Add(new SettingsModule());
		Modules.Add(new QuickSlModule());
		Modules.Add(new VictoryModule());
		Modules.Add(new HostBootstrapModule());
		Modules.Add(new ExtendedLobbyModule());
		Modules.Add(new LobbyManagerModule());
		if (PlatformDetector.IsMacOS)
		{
			Modules.Add(new MacOsTlsModule());
		}
		foreach (IRMPModule module in Modules)
		{
			try
			{
				module.Initialize(configManager, cache);
			}
			catch (Exception value3)
			{
				Log.Warn($"[RMP] Failed to initialize module {module.Name}: {value3}");
			}
		}
		SceneTree sceneTree = (SceneTree)Engine.GetMainLoop();
		_root = new Node
		{
			Name = "RMPController"
		};
		_root.AddChild(SceneMonitor.CreateRegistryNode(), forceReadableName: false, Node.InternalMode.Disabled);
		foreach (IRMPModule module2 in Modules)
		{
			try
			{
				Node node = module2.CreateNode();
				if (node != null)
				{
					_root.AddChild(node, forceReadableName: false, Node.InternalMode.Disabled);
				}
			}
			catch (Exception value4)
			{
				Log.Warn($"[RMP] Failed to create node for module {module2.Name}: {value4}");
			}
		}
		sceneTree.Root.CallDeferred("add_child", _root);
		Log.Warn($"[RMP Stable] All modules loaded. Player limit fixed at {16}, slot bits: {4} (cap {value}), lobby bits: {5} (cap {value2}), treasure reward desync fix: enabled, difficulty scaling: {ProtocolConfig.DifficultyScalingEnabled}, macOS TLS: {configManager.MacOsTlsWorkaround}");
	}
}
