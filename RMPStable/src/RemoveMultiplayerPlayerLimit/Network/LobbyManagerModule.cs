using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Features.QuickSl;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Network;

public class LobbyManagerModule : IRMPModule
{
	private class LobbyManagerNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";

			public static readonly StringName _lastPlayerCount = "_lastPlayerCount";

			public static readonly StringName _lastTargetPlayerLimit = "_lastTargetPlayerLimit";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly LobbyManagerModule _module;

		private int _frameCounter;

		private StartRunLobby? _lastLobby;

		private LoadRunLobby? _lastLoggedLoadLobby;

		private int _lastPlayerCount = -1;

		private int _lastTargetPlayerLimit = -1;

		public LobbyManagerNode(LobbyManagerModule module)
		{
			_module = module;
			base.Name = "LobbyManagerNode";
		}

		public override void _Process(double delta)
		{
			QuickSlController.ProcessFrame();
			HandleStartRunLobby(SceneMonitor.FindActiveStartRunLobby());
			if (++_frameCounter % 15 == 0)
			{
				HandleLoadedRunLobby(SceneMonitor.FindActiveLoadRunLobby());
			}
			if (_frameCounter % 60 == 0)
			{
				RmpProtocol.RequestInitialSync();
			}
		}

		private void HandleStartRunLobby(StartRunLobby? lobby)
		{
			if (lobby != _lastLobby)
			{
				if (_lastLobby != null && lobby == null)
				{
					RmpProtocol.Unbind();
				}
				_lastLobby = lobby;
				_lastPlayerCount = -1;
				_lastTargetPlayerLimit = -1;
				if (lobby != null)
				{
					OnLobbyActivated(lobby);
				}
			}
			if (lobby != null)
			{
				RmpProtocol.Bind(lobby.NetService);
				int lobbyPlayerCount = GetLobbyPlayerCount(lobby);
				int num = 16;
				if (lobbyPlayerCount != _lastPlayerCount || num != _lastTargetPlayerLimit)
				{
					SyncLobbyState(lobby, num);
					_lastPlayerCount = lobbyPlayerCount;
					_lastTargetPlayerLimit = num;
				}
			}
		}

		private void OnLobbyActivated(StartRunLobby lobby)
		{
			NetGameType? netGameType = lobby.NetService?.Type;
			if ((netGameType.HasValue && (uint)(netGameType.GetValueOrDefault() - 2) <= 1u) ? true : false)
			{
				RmpProtocol.Bind(lobby.NetService);
			}
			SyncLobbyState(lobby, 16);
		}

		private void SyncLobbyState(StartRunLobby lobby, int targetLimit)
		{
			if (_module._maxPlayersField != null && lobby.MaxPlayers != targetLimit)
			{
				_module._maxPlayersField.SetValue(lobby, targetLimit);
				Log.Info($"[RMP] StartRunLobby.MaxPlayers synchronized to {targetLimit}");
			}
			INetGameService netService = lobby.NetService;
			if (netService != null && netService.Type == NetGameType.Host)
			{
				int currentMemberLimit = SteamLobbyHelper.GetCurrentMemberLimit(lobby.NetService);
				if (currentMemberLimit != -1 && currentMemberLimit != targetLimit)
				{
					SteamLobbyHelper.TryUpdateMemberLimit(lobby.NetService, targetLimit);
				}
				else if (currentMemberLimit == -1)
				{
					SteamLobbyHelper.TryUpdateMemberLimit(lobby.NetService, targetLimit);
				}
				if (ExtendedLobbyModule.ShouldUseExtendedLobbyProtocol(lobby))
				{
					RmpProtocol.BroadcastLobbySnapshot(lobby.Players);
				}
			}
		}

		private void HandleLoadedRunLobby(LoadRunLobby? loadRunLobby)
		{
			if (loadRunLobby == null)
			{
				_lastLoggedLoadLobby = null;
			}
			else if (loadRunLobby != _lastLoggedLoadLobby)
			{
				_lastLoggedLoadLobby = loadRunLobby;
				int count = loadRunLobby.Run.Players.Count;
				int count2 = loadRunLobby.ConnectedPlayerIds.Count;
				int capacity;
				int value = (HostBootstrapModule.TryGetTrackedHostCapacity(loadRunLobby.NetService, out capacity) ? capacity : count);
				string value2 = SceneMonitor.GetActiveLoadLobbyScreenName() ?? "UnknownLoadLobbyScreen";
				Log.Info($"[RMP] Loaded-run lobby active: screen={value2}, transport={HostBootstrapModule.GetTransportName(loadRunLobby.NetService)}, hostCapacity={value}, savePlayers={count}, connectedPlayers={count2}");
			}
		}

		private static int GetLobbyPlayerCount(StartRunLobby lobby)
		{
			try
			{
				return lobby.Players?.Count ?? 0;
			}
			catch
			{
				return 0;
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(1)
			{
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null)
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
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if (method == MethodName._Process)
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
			if (name == PropertyName._lastPlayerCount)
			{
				_lastPlayerCount = VariantUtils.ConvertTo<int>(in value);
				return true;
			}
			if (name == PropertyName._lastTargetPlayerLimit)
			{
				_lastTargetPlayerLimit = VariantUtils.ConvertTo<int>(in value);
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
			if (name == PropertyName._lastPlayerCount)
			{
				value = VariantUtils.CreateFrom(in _lastPlayerCount);
				return true;
			}
			if (name == PropertyName._lastTargetPlayerLimit)
			{
				value = VariantUtils.CreateFrom(in _lastTargetPlayerLimit);
				return true;
			}
			return base.GetGodotClassPropertyValue(in name, out value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.PropertyInfo> GetGodotPropertyList()
		{
			return new List<Godot.Bridge.PropertyInfo>
			{
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._lastPlayerCount, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._lastTargetPlayerLimit, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
			info.AddProperty(PropertyName._lastPlayerCount, Variant.From(in _lastPlayerCount));
			info.AddProperty(PropertyName._lastTargetPlayerLimit, Variant.From(in _lastTargetPlayerLimit));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
			if (info.TryGetProperty(PropertyName._lastPlayerCount, out var value2))
			{
				_lastPlayerCount = value2.As<int>();
			}
			if (info.TryGetProperty(PropertyName._lastTargetPlayerLimit, out var value3))
			{
				_lastTargetPlayerLimit = value3.As<int>();
			}
		}
	}

	private FieldInfo? _maxPlayersField;

	public string Name => "LobbyManager";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_maxPlayersField = cache.GetField(typeof(StartRunLobby), "<MaxPlayers>k__BackingField");
	}

	public Node? CreateNode()
	{
		return new LobbyManagerNode(this);
	}

	public void Cleanup()
	{
		RmpProtocol.Unbind();
	}
}
