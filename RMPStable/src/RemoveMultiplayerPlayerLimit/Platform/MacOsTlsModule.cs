using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Logging;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Platform;

public class MacOsTlsModule : IRMPModule
{
	private class MacOsTlsNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";

			public static readonly StringName _wasConnecting = "_wasConnecting";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly MacOsTlsModule _mod;

		private int _frameCounter;

		private bool _wasConnecting;

		public MacOsTlsNode(MacOsTlsModule mod)
		{
			_mod = mod;
			base.Name = "MacOsTlsNode";
		}

		public override void _Process(double delta)
		{
			if (!_mod._config.MacOsTlsWorkaround || ++_frameCounter % 60 != 0)
			{
				return;
			}
			try
			{
				MultiplayerPeer multiplayerPeer = ((SceneTree)Engine.GetMainLoop()).GetMultiplayer()?.MultiplayerPeer;
				bool flag = multiplayerPeer != null && multiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting;
				if (flag && !_wasConnecting && !_mod._workaroundLogged)
				{
					Log.Warn("[RMP:TLS] Multiplayer connection detected — TLS workaround is active.");
					_mod._workaroundLogged = true;
				}
				_wasConnecting = flag;
			}
			catch
			{
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
			if (name == PropertyName._wasConnecting)
			{
				_wasConnecting = VariantUtils.ConvertTo<bool>(in value);
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
			if (name == PropertyName._wasConnecting)
			{
				value = VariantUtils.CreateFrom(in _wasConnecting);
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
				new Godot.Bridge.PropertyInfo(Variant.Type.Bool, PropertyName._wasConnecting, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
			info.AddProperty(PropertyName._wasConnecting, Variant.From(in _wasConnecting));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
			if (info.TryGetProperty(PropertyName._wasConnecting, out var value2))
			{
				_wasConnecting = value2.As<bool>();
			}
		}
	}

	private ConfigManager _config;

	private ReflectionCache _cache;

	private bool _workaroundLogged;

	public string Name => "MacOSTls";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_config = config;
		_cache = cache;
		if (config.MacOsTlsWorkaround)
		{
			ApplyEnvironmentWorkaround();
		}
	}

	public Node? CreateNode()
	{
		return new MacOsTlsNode(this);
	}

	public void Cleanup()
	{
	}

	private void ApplyEnvironmentWorkaround()
	{
		try
		{
			if (!_workaroundLogged)
			{
				Log.Warn("[RMP:TLS] macOS TLS workaround active — multiplayer cert validation may be relaxed.");
				_workaroundLogged = true;
			}
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:TLS] Environment workaround failed: " + ex.Message);
		}
	}

	internal static TlsOptions? CreateUnsafeTlsOptions(X509Certificate? trustedChain)
	{
		try
		{
			System.Reflection.MethodInfo method = typeof(TlsOptions).GetMethod("ClientUnsafe", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(X509Certificate) }, null);
			if (method != null)
			{
				return (TlsOptions)method.Invoke(null, new object[1] { trustedChain });
			}
			System.Reflection.MethodInfo method2 = typeof(TlsOptions).GetMethod("ClientUnsafe", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
			if (method2 != null)
			{
				return (TlsOptions)method2.Invoke(null, Array.Empty<object>());
			}
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP:TLS] Failed to create unsafe TLS options: " + ex.Message);
		}
		return null;
	}

	internal static bool IsMultiplayerContext()
	{
		return (new StackTrace(fNeedFileInfo: false).GetFrames() ?? Array.Empty<StackFrame>()).Any(delegate(StackFrame f)
		{
			string text = f.GetMethod()?.DeclaringType?.FullName;
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			return text.StartsWith("MegaCrit.Sts2.Core.Multiplayer.", StringComparison.Ordinal) || text.StartsWith("MegaCrit.Sts2.Core.Platform.Steam.SteamJoinCallbackHandler", StringComparison.Ordinal) || text.StartsWith("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NJoinFriendScreen", StringComparison.Ordinal) || text.StartsWith("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayer", StringComparison.Ordinal) || text.StartsWith("MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer.", StringComparison.Ordinal);
		});
	}
}
