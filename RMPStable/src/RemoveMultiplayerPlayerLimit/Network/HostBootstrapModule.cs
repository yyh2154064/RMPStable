using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

public class HostBootstrapModule : IRMPModule
{
	private class HostBootstrapNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";

			public static readonly StringName PatchMainMenuSubmenu = "PatchMainMenuSubmenu";

			public static readonly StringName PatchHostSubmenu = "PatchHostSubmenu";

			public static readonly StringName OnMainMenuHostPressed = "OnMainMenuHostPressed";

			public static readonly StringName OnMainMenuLoadPressed = "OnMainMenuLoadPressed";

			public static readonly StringName OnHostSubmenuPressed = "OnHostSubmenuPressed";

			public static readonly StringName GetPreferredHostPlatform = "GetPreferredHostPlatform";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly HashSet<ulong> _patchedMainMenuSubmenus = new HashSet<ulong>();

		private readonly HashSet<ulong> _patchedHostSubmenus = new HashSet<ulong>();

		private int _frameCounter;

		public HostBootstrapNode()
		{
			base.Name = "HostBootstrapNode";
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 2 == 0)
			{
				PatchMainMenuSubmenu(SceneMonitor.FindMultiplayerSubmenu());
				PatchHostSubmenu(SceneMonitor.FindMultiplayerHostSubmenu());
			}
		}

		private void PatchMainMenuSubmenu(NMultiplayerSubmenu? submenu)
		{
			NMultiplayerSubmenu submenu2 = submenu;
			if (submenu2 == null)
			{
				return;
			}
			ulong instanceId = submenu2.GetInstanceId();
			if (!_patchedMainMenuSubmenus.Contains(instanceId))
			{
				NButton nodeOrNull = submenu2.GetNodeOrNull<NButton>("ButtonContainer/HostButton");
				NButton nodeOrNull2 = submenu2.GetNodeOrNull<NButton>("ButtonContainer/LoadButton");
				if (nodeOrNull != null && nodeOrNull2 != null && ReplaceReleasedHandler(nodeOrNull, submenu2, "OnHostPressed", delegate
				{
					OnMainMenuHostPressed(submenu2);
				}) && ReplaceReleasedHandler(nodeOrNull2, submenu2, "StartLoad", delegate
				{
					OnMainMenuLoadPressed(submenu2);
				}))
				{
					_patchedMainMenuSubmenus.Add(instanceId);
					Log.Info("[RMP:HostBootstrap] Patched NMultiplayerSubmenu host/load handlers.");
				}
			}
		}

		private void PatchHostSubmenu(NMultiplayerHostSubmenu? submenu)
		{
			NMultiplayerHostSubmenu submenu2 = submenu;
			if (submenu2 == null)
			{
				return;
			}
			ulong instanceId = submenu2.GetInstanceId();
			if (!_patchedHostSubmenus.Contains(instanceId))
			{
				NButton nodeOrNull = submenu2.GetNodeOrNull<NButton>("StandardButton");
				NButton nodeOrNull2 = submenu2.GetNodeOrNull<NButton>("DailyButton");
				NButton nodeOrNull3 = submenu2.GetNodeOrNull<NButton>("CustomRunButton");
				if (nodeOrNull != null && nodeOrNull2 != null && nodeOrNull3 != null && ReplaceReleasedHandler(nodeOrNull, submenu2, "OnStandardPressed", delegate
				{
					OnHostSubmenuPressed(submenu2, GameMode.Standard);
				}) && ReplaceReleasedHandler(nodeOrNull2, submenu2, "OnDailyPressed", delegate
				{
					OnHostSubmenuPressed(submenu2, GameMode.Daily);
				}) && ReplaceReleasedHandler(nodeOrNull3, submenu2, "OnCustomPressed", delegate
				{
					OnHostSubmenuPressed(submenu2, GameMode.Custom);
				}))
				{
					_patchedHostSubmenus.Add(instanceId);
					Log.Info("[RMP:HostBootstrap] Patched NMultiplayerHostSubmenu handlers.");
				}
			}
		}

		private void OnMainMenuHostPressed(NMultiplayerSubmenu submenu)
		{
			NSubmenuStack ancestorOfType = submenu.GetAncestorOfType<NSubmenuStack>();
			if (ancestorOfType == null)
			{
				Log.Warn("[RMP:HostBootstrap] Failed to locate submenu stack for multiplayer submenu.");
				return;
			}
			if (SaveManager.Instance.Progress.NumberOfRuns > 0)
			{
				NMultiplayerHostSubmenu submenuType = ancestorOfType.GetSubmenuType<NMultiplayerHostSubmenu>();
				PatchHostSubmenu(submenuType);
				ancestorOfType.Push(submenuType);
				return;
			}
			Control nodeOrNull = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
			if (nodeOrNull == null)
			{
				Log.Warn("[RMP:HostBootstrap] Multiplayer submenu loading overlay not found.");
			}
			else
			{
				TaskHelper.RunSafely(StartNewRunHostAsync(GameMode.Standard, nodeOrNull, ancestorOfType, 16));
			}
		}

		private void OnMainMenuLoadPressed(NMultiplayerSubmenu submenu)
		{
			NSubmenuStack ancestorOfType = submenu.GetAncestorOfType<NSubmenuStack>();
			Control nodeOrNull = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
			NButton nodeOrNull2 = submenu.GetNodeOrNull<NButton>("ButtonContainer/LoadButton");
			if (ancestorOfType == null || nodeOrNull == null)
			{
				Log.Warn("[RMP:HostBootstrap] Failed to resolve load-run host prerequisites.");
				return;
			}
			PlatformType preferredHostPlatform = GetPreferredHostPlatform();
			ReadSaveResult<SerializableRun> readSaveResult = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(PlatformUtil.GetLocalPlayerId(preferredHostPlatform));
			if (!readSaveResult.Success || readSaveResult.SaveData == null)
			{
				Log.Warn("[RMP:HostBootstrap] Invalid multiplayer run save detected.");
				nodeOrNull2?.Disable();
				NErrorPopup nErrorPopup = NErrorPopup.Create(new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title"), new LocString("main_menu_ui", "INVALID_SAVE_POPUP.description_run"), new LocString("main_menu_ui", "INVALID_SAVE_POPUP.dismiss"), showReportBugButton: true);
				if (nErrorPopup != null && NModalContainer.Instance != null)
				{
					NModalContainer.Instance.Add(nErrorPopup);
					NModalContainer.Instance.ShowBackstop();
				}
			}
			else
			{
				TaskHelper.RunSafely(StartLoadedRunHostAsync(readSaveResult.SaveData, nodeOrNull, ancestorOfType, 16));
			}
		}

		private void OnHostSubmenuPressed(NMultiplayerHostSubmenu submenu, GameMode gameMode)
		{
			NSubmenuStack ancestorOfType = submenu.GetAncestorOfType<NSubmenuStack>();
			Control nodeOrNull = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
			if (ancestorOfType == null || nodeOrNull == null)
			{
				Log.Warn("[RMP:HostBootstrap] Failed to resolve multiplayer host submenu prerequisites.");
			}
			else
			{
				TaskHelper.RunSafely(StartNewRunHostAsync(gameMode, nodeOrNull, ancestorOfType, 16));
			}
		}

		private static bool ReplaceReleasedHandler(NButton button, object signalTarget, string originalMethodName, Action<NButton> replacement)
		{
			try
			{
				Callable callable = CreatePrivateReleasedCallable(signalTarget, originalMethodName);
				if (button.IsConnected(NClickableControl.SignalName.Released, callable))
				{
					button.Disconnect(NClickableControl.SignalName.Released, callable);
				}
				button.Connect(NClickableControl.SignalName.Released, Callable.From(replacement));
				return true;
			}
			catch (Exception ex)
			{
				Log.Warn($"[RMP:HostBootstrap] Failed to rewire {signalTarget.GetType().Name}.{originalMethodName}: {ex.Message}");
				return false;
			}
		}

		private static Callable CreatePrivateReleasedCallable(object target, string methodName)
		{
			System.Reflection.MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				throw new MissingMethodException(target.GetType().FullName, methodName);
			}
			return Callable.From((Action<NButton>)Delegate.CreateDelegate(typeof(Action<NButton>), target, method));
		}

		private static async Task StartNewRunHostAsync(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack, int hostCapacity)
		{
			loadingOverlay.Visible = true;
			try
			{
				NetHostGameService netService = new NetHostGameService();
				NetErrorInfo? netErrorInfo = await StartHostAsync(netService, hostCapacity);
				if (netErrorInfo.HasValue)
				{
					ShowNetError(netErrorInfo.Value);
					return;
				}
				TrackHostCapacity(netService, hostCapacity);
				switch (gameMode)
				{
				case GameMode.Standard:
				{
					NCharacterSelectScreen submenuType3 = stack.GetSubmenuType<NCharacterSelectScreen>();
					submenuType3.InitializeMultiplayerAsHost(netService, hostCapacity);
					stack.Push(submenuType3);
					break;
				}
				case GameMode.Daily:
				{
					NDailyRunScreen submenuType2 = stack.GetSubmenuType<NDailyRunScreen>();
					submenuType2.InitializeMultiplayerAsHost(netService);
					stack.Push(submenuType2);
					break;
				}
				default:
				{
					NCustomRunScreen submenuType = stack.GetSubmenuType<NCustomRunScreen>();
					submenuType.InitializeMultiplayerAsHost(netService, hostCapacity);
					stack.Push(submenuType);
					break;
				}
				}
				Log.Info($"[RMP:HostBootstrap] Hosted {gameMode} lobby via {GetTransportName(netService)} with capacity {hostCapacity}.");
			}
			catch (Exception value)
			{
				ShowNetError(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
				Log.Warn($"[RMP:HostBootstrap] New-run host startup failed: {value}");
				throw;
			}
			finally
			{
				loadingOverlay.Visible = false;
			}
		}

		internal static async Task StartLoadedRunHostAsync(SerializableRun run, Control loadingOverlay, NSubmenuStack stack, int hostCapacity)
		{
			loadingOverlay.Visible = true;
			try
			{
				NetHostGameService netService = new NetHostGameService();
				NetErrorInfo? netErrorInfo = await StartHostAsync(netService, hostCapacity);
				if (netErrorInfo.HasValue)
				{
					ShowNetError(netErrorInfo.Value);
					return;
				}
				TrackHostCapacity(netService, hostCapacity);
				GameMode gameMode = ResolveGameMode(run);
				switch (gameMode)
				{
				case GameMode.Daily:
				{
					NDailyRunLoadScreen submenuType3 = stack.GetSubmenuType<NDailyRunLoadScreen>();
					submenuType3.InitializeAsHost(netService, run);
					stack.Push(submenuType3);
					break;
				}
				case GameMode.Custom:
				{
					NCustomRunLoadScreen submenuType2 = stack.GetSubmenuType<NCustomRunLoadScreen>();
					submenuType2.InitializeAsHost(netService, run);
					stack.Push(submenuType2);
					break;
				}
				default:
				{
					NMultiplayerLoadGameScreen submenuType = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
					submenuType.InitializeAsHost(netService, run);
					stack.Push(submenuType);
					break;
				}
				}
				Log.Info($"[RMP:HostBootstrap] Hosted loaded {gameMode} lobby via {GetTransportName(netService)} with capacity {hostCapacity}, savePlayers={run.Players.Count}, connectedPlayers=1.");
			}
			catch (Exception value)
			{
				ShowNetError(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
				Log.Warn($"[RMP:HostBootstrap] Loaded-run host startup failed: {value}");
				throw;
			}
			finally
			{
				loadingOverlay.Visible = false;
			}
		}

		private static async Task<NetErrorInfo?> StartHostAsync(NetHostGameService netService, int hostCapacity)
		{
			if (GetPreferredHostPlatform() == PlatformType.Steam)
			{
				return await netService.StartSteamHost(hostCapacity);
			}
			return netService.StartENetHost(33771, hostCapacity);
		}

		internal static PlatformType GetPreferredHostPlatform()
		{
			if (!SteamInitializer.Initialized || CommandLineHelper.HasArg("fastmp"))
			{
				return PlatformType.None;
			}
			return PlatformType.Steam;
		}

		private static void TrackHostCapacity(INetGameService netService, int hostCapacity)
		{
			TrackedHostCapacities[netService] = hostCapacity;
		}

		private static void ShowNetError(NetErrorInfo error)
		{
			NErrorPopup nErrorPopup = NErrorPopup.Create(error);
			if (nErrorPopup != null && NModalContainer.Instance != null)
			{
				NModalContainer.Instance.Add(nErrorPopup);
			}
		}

		private static GameMode ResolveGameMode(SerializableRun run)
		{
			object obj = SerializableRunGameModeProperty?.GetValue(run);
			if (obj is GameMode)
			{
				return (GameMode)obj;
			}
			obj = SerializableRunGameModeField?.GetValue(run);
			if (obj is GameMode)
			{
				return (GameMode)obj;
			}
			if (run.DailyTime.HasValue)
			{
				return GameMode.Daily;
			}
			if (run.Modifiers.Count <= 0)
			{
				return GameMode.Standard;
			}
			return GameMode.Custom;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(7)
			{
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.PatchMainMenuSubmenu, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "submenu", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.PatchHostSubmenu, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "submenu", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnMainMenuHostPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "submenu", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnMainMenuLoadPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "submenu", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnHostSubmenuPressed, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "submenu", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Int, "gameMode", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.GetPreferredHostPlatform, new Godot.Bridge.PropertyInfo(Variant.Type.Int, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, null, null)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName._Process && args.Count == 1)
			{
				_Process(VariantUtils.ConvertTo<double>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.PatchMainMenuSubmenu && args.Count == 1)
			{
				PatchMainMenuSubmenu(VariantUtils.ConvertTo<NMultiplayerSubmenu>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.PatchHostSubmenu && args.Count == 1)
			{
				PatchHostSubmenu(VariantUtils.ConvertTo<NMultiplayerHostSubmenu>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnMainMenuHostPressed && args.Count == 1)
			{
				OnMainMenuHostPressed(VariantUtils.ConvertTo<NMultiplayerSubmenu>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnMainMenuLoadPressed && args.Count == 1)
			{
				OnMainMenuLoadPressed(VariantUtils.ConvertTo<NMultiplayerSubmenu>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnHostSubmenuPressed && args.Count == 2)
			{
				OnHostSubmenuPressed(VariantUtils.ConvertTo<NMultiplayerHostSubmenu>(in args[0]), VariantUtils.ConvertTo<GameMode>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.GetPreferredHostPlatform && args.Count == 0)
			{
				PlatformType from = GetPreferredHostPlatform();
				ret = VariantUtils.CreateFrom(in from);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName.GetPreferredHostPlatform && args.Count == 0)
			{
				PlatformType from = GetPreferredHostPlatform();
				ret = VariantUtils.CreateFrom(in from);
				return true;
			}
			ret = default(godot_variant);
			return false;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if (method == MethodName._Process)
			{
				return true;
			}
			if (method == MethodName.PatchMainMenuSubmenu)
			{
				return true;
			}
			if (method == MethodName.PatchHostSubmenu)
			{
				return true;
			}
			if (method == MethodName.OnMainMenuHostPressed)
			{
				return true;
			}
			if (method == MethodName.OnMainMenuLoadPressed)
			{
				return true;
			}
			if (method == MethodName.OnHostSubmenuPressed)
			{
				return true;
			}
			if (method == MethodName.GetPreferredHostPlatform)
			{
				return true;
			}
			return base.HasGodotClassMethod(in method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if (name == PropertyName._frameCounter)
			{
				_frameCounter = VariantUtils.ConvertTo<int>(in value);
				return true;
			}
			return base.SetGodotClassPropertyValue(in name, in value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			if (name == PropertyName._frameCounter)
			{
				value = VariantUtils.CreateFrom(in _frameCounter);
				return true;
			}
			return base.GetGodotClassPropertyValue(in name, out value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.PropertyInfo> GetGodotPropertyList()
		{
			return new List<Godot.Bridge.PropertyInfo>
			{
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
		}
	}

	private const ushort DefaultEnetPort = 33771;

	private static readonly Dictionary<INetGameService, int> TrackedHostCapacities = new Dictionary<INetGameService, int>();

	private static readonly System.Reflection.PropertyInfo? SerializableRunGameModeProperty = typeof(SerializableRun).GetProperty("GameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly FieldInfo? SerializableRunGameModeField = typeof(SerializableRun).GetField("GameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? typeof(SerializableRun).GetField("gameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private bool _deferToDcip;

	public string Name => "HostBootstrap";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		if (IsDirectConnectIpLoaded())
		{
			_deferToDcip = true;
			Log.Warn("[RMP:HostBootstrap] DirectConnectIP detected — RMP is yielding multiplayer host bootstrap to DCIP to avoid transport protocol conflicts (DCIP replaces NetHostGameService with its own DirectHost/DirectClient). While DCIP is loaded, RMP's 4-16 player slider will NOT affect host capacity: Steam mode stays at vanilla 4, Direct-IP mode is capped at DCIP's hardcoded 16. Remove one of the two mods to regain full control.");
		}
	}

	public Node? CreateNode()
	{
		if (!_deferToDcip)
		{
			return new HostBootstrapNode();
		}
		return null;
	}

	private static bool IsDirectConnectIpLoaded()
	{
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				if (string.Equals(assemblies[i].GetName().Name, "DirectConnectIP", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public void Cleanup()
	{
		TrackedHostCapacities.Clear();
	}

	internal static bool TryGetTrackedHostCapacity(INetGameService? netService, out int capacity)
	{
		if (netService != null && TrackedHostCapacities.TryGetValue(netService, out capacity))
		{
			return true;
		}
		capacity = 0;
		return false;
	}

	internal static string GetTransportName(INetGameService netService)
	{
		if (netService.Platform != PlatformType.Steam)
		{
			return "ENet";
		}
		return "Steam";
	}

	internal static async Task<bool> StartQuickSlLoadedHostAsync()
	{
		NMainMenu? mainMenu = NGame.Instance?.MainMenu;
		if (mainMenu == null)
		{
			Log.Warn("[RMP:QuickSL] Main menu was not ready for multiplayer reload.");
			return false;
		}
		NMultiplayerSubmenu submenu = mainMenu.OpenMultiplayerSubmenu();
		Control? loadingOverlay = submenu.GetNodeOrNull<Control>("%LoadingOverlay");
		if (loadingOverlay == null)
		{
			Log.Warn("[RMP:QuickSL] Multiplayer loading overlay was not found.");
			return false;
		}
		PlatformType platform = HostBootstrapNode.GetPreferredHostPlatform();
		ReadSaveResult<SerializableRun> result = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(PlatformUtil.GetLocalPlayerId(platform));
		if (!result.Success || result.SaveData == null)
		{
			Log.Warn("[RMP:QuickSL] Multiplayer checkpoint could not be loaded.");
			return false;
		}
		await HostBootstrapNode.StartLoadedRunHostAsync(result.SaveData, loadingOverlay, mainMenu.SubmenuStack, 16);
		return true;
	}
}
