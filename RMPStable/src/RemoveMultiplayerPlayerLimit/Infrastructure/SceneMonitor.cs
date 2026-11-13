using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
#if STS2_0113
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;
#endif
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

public static class SceneMonitor
{
	private static readonly FieldInfo? DailyRunLobbyField = typeof(NDailyRunScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo? MultiplayerLoadLobbyField = typeof(NMultiplayerLoadGameScreen).GetField("_runLobby", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo? CustomRunLoadLobbyField = typeof(NCustomRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo? DailyRunLoadLobbyField = typeof(NDailyRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

	public static Node CreateRegistryNode()
	{
		return new SceneRegistry();
	}

	public static string GetCurrentSceneName()
	{
		return ((SceneTree)Engine.GetMainLoop()).CurrentScene?.Name.ToString() ?? string.Empty;
	}

	public static bool IsSceneActive(string nameContains)
	{
		return ((SceneTree)Engine.GetMainLoop()).CurrentScene?.Name.ToString().Contains(nameContains, StringComparison.OrdinalIgnoreCase) ?? false;
	}

	public static Node GetRoot()
	{
		return ((SceneTree)Engine.GetMainLoop()).Root;
	}

	public static NSettingsScreen? FindSettingsScreen()
	{
		NSettingsScreen nSettingsScreen = SceneRegistry.Instance?.SettingsScreen;
		if (nSettingsScreen == null || !nSettingsScreen.IsVisibleInTree())
		{
			return null;
		}
		return nSettingsScreen;
	}

	public static NRestSiteRoom? FindRestSiteRoom()
	{
		return SceneRegistry.Instance?.RestSiteRoom;
	}

	public static NMerchantRoom? FindMerchantRoom()
	{
		return SceneRegistry.Instance?.MerchantRoom;
	}

	public static NTreasureRoomRelicCollection? FindTreasureRoomRelicCollection()
	{
		return SceneRegistry.Instance?.TreasureRoomRelicCollection;
	}

	public static NMultiplayerSubmenu? FindMultiplayerSubmenu()
	{
		return SceneRegistry.Instance?.MultiplayerSubmenu;
	}

	public static NMultiplayerHostSubmenu? FindMultiplayerHostSubmenu()
	{
		return SceneRegistry.Instance?.MultiplayerHostSubmenu;
	}

	public static NMultiplayerLoadGameScreen? FindMultiplayerLoadGameScreen()
	{
		return SceneRegistry.Instance?.MultiplayerLoadGameScreen;
	}

	public static NCustomRunLoadScreen? FindCustomRunLoadScreen()
	{
		return SceneRegistry.Instance?.CustomRunLoadScreen;
	}

	public static NDailyRunLoadScreen? FindDailyRunLoadScreen()
	{
		return SceneRegistry.Instance?.DailyRunLoadScreen;
	}

	public static Node? FindNodeByName(Node root, string nameContains)
	{
		if (root.Name.ToString().Contains(nameContains, StringComparison.Ordinal))
		{
			return root;
		}
		foreach (Node child in root.GetChildren())
		{
			Node node = FindNodeByName(child, nameContains);
			if (node != null)
			{
				return node;
			}
		}
		return null;
	}

	public static T? FindNodeOfType<T>(Node root) where T : Node
	{
		if (root == GetRoot() && TryGetCachedNode<T>(out T node))
		{
			return node;
		}
		if (root is T result)
		{
			return result;
		}
		foreach (Node child in root.GetChildren())
		{
			T val = FindNodeOfType<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	public static Node? FindNode(Node root, Func<Node, bool> predicate)
	{
		if (predicate(root))
		{
			return root;
		}
		foreach (Node child in root.GetChildren())
		{
			Node node = FindNode(child, predicate);
			if (node != null)
			{
				return node;
			}
		}
		return null;
	}

	public static StartRunLobby? FindActiveStartRunLobby()
	{
		var active = NGame.Instance?.MainMenu?.SubmenuStack?.Peek();
		return active switch
		{
			NCharacterSelectScreen character => character.Lobby,
			NCustomRunScreen custom => custom.Lobby,
			NDailyRunScreen daily => DailyRunLobbyField?.GetValue(daily) as StartRunLobby,
#if STS2_0113
			NBlitzScreen blitz => blitz.Lobby,
			NDreadScreen dread => dread.Lobby,
#endif
			_ => null
		};
	}

	public static LoadRunLobby? FindActiveLoadRunLobby()
	{
		var active = NGame.Instance?.MainMenu?.SubmenuStack?.Peek();
		return active switch
		{
			NMultiplayerLoadGameScreen standard => MultiplayerLoadLobbyField?.GetValue(standard) as LoadRunLobby,
			NCustomRunLoadScreen custom => CustomRunLoadLobbyField?.GetValue(custom) as LoadRunLobby,
			NDailyRunLoadScreen daily => DailyRunLoadLobbyField?.GetValue(daily) as LoadRunLobby,
			_ => null
		};
	}

	public static string? GetActiveLoadLobbyScreenName()
	{
		if (MultiplayerLoadLobbyField?.GetValue(SceneRegistry.Instance?.MultiplayerLoadGameScreen) is LoadRunLobby)
		{
			return "NMultiplayerLoadGameScreen";
		}
		if (CustomRunLoadLobbyField?.GetValue(SceneRegistry.Instance?.CustomRunLoadScreen) is LoadRunLobby)
		{
			return "NCustomRunLoadScreen";
		}
		if (DailyRunLoadLobbyField?.GetValue(SceneRegistry.Instance?.DailyRunLoadScreen) is LoadRunLobby)
		{
			return "NDailyRunLoadScreen";
		}
		return null;
	}

	private static bool TryGetCachedNode<T>(out T? node) where T : Node
	{
		SceneRegistry instance = SceneRegistry.Instance;
		T val = ((instance == null) ? null : ((typeof(T) == typeof(NSettingsScreen)) ? (instance.SettingsScreen as T) : ((typeof(T) == typeof(NRestSiteRoom)) ? (instance.RestSiteRoom as T) : ((typeof(T) == typeof(NMerchantRoom)) ? (instance.MerchantRoom as T) : ((typeof(T) == typeof(NTreasureRoomRelicCollection)) ? (instance.TreasureRoomRelicCollection as T) : ((typeof(T) == typeof(NCharacterSelectScreen)) ? (instance.CharacterSelectScreen as T) : ((typeof(T) == typeof(NCustomRunScreen)) ? (instance.CustomRunScreen as T) : ((typeof(T) == typeof(NDailyRunScreen)) ? (instance.DailyRunScreen as T) : ((typeof(T) == typeof(NMultiplayerLoadGameScreen)) ? (instance.MultiplayerLoadGameScreen as T) : ((typeof(T) == typeof(NCustomRunLoadScreen)) ? (instance.CustomRunLoadScreen as T) : ((typeof(T) == typeof(NDailyRunLoadScreen)) ? (instance.DailyRunLoadScreen as T) : ((typeof(T) == typeof(NMultiplayerSubmenu)) ? (instance.MultiplayerSubmenu as T) : ((!(typeof(T) == typeof(NMultiplayerHostSubmenu))) ? null : (instance.MultiplayerHostSubmenu as T))))))))))))));
		node = val;
		return node != null;
	}
}
