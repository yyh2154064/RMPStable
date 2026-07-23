using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Singleton;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.DifficultyScaling;

public class DifficultyModule : IRMPModule
{
	private class DifficultyNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";

			public static readonly StringName IsCombatActive = "IsCombatActive";

			public static readonly StringName ReapplyMonsterScaling = "ReapplyMonsterScaling";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _wasInCombat = "_wasInCombat";

			public static readonly StringName _scalingChecked = "_scalingChecked";

			public static readonly StringName _frameCounter = "_frameCounter";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly DifficultyModule _module;

		private bool _wasInCombat;

		private bool _scalingChecked;

		private int _frameCounter;

		public DifficultyNode(DifficultyModule module)
		{
			_module = module;
			base.Name = "DifficultyNode";
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 10 != 0)
			{
				return;
			}
			bool flag = IsCombatActive();
			if (flag && !_wasInCombat)
			{
				_scalingChecked = false;
			}
			if (flag && !_scalingChecked)
			{
				int playerCount = GameStateAccessor.GetPlayerCount();
				if (playerCount > 4)
				{
					GameStateAccessor.GetEffectivePlayerCount(playerCount);
					if (ProtocolConfig.DifficultyScalingEnabled)
					{
						ReapplyMonsterScaling(playerCount);
					}
				}
				_scalingChecked = true;
			}
			if (!flag && _wasInCombat)
			{
				_scalingChecked = false;
			}
			_wasInCombat = flag;
		}

		private static bool IsCombatActive()
		{
			try
			{
				return SceneMonitor.IsSceneActive("Combat") || SceneMonitor.IsSceneActive("combat");
			}
			catch
			{
				return false;
			}
		}

		private void ReapplyMonsterScaling(int actualPlayerCount)
		{
			Log.Warn($"[RMP:Difficulty] Scaling check: {actualPlayerCount} players, enabled={ProtocolConfig.DifficultyScalingEnabled}");
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(3)
			{
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.IsCombatActive, new Godot.Bridge.PropertyInfo(Variant.Type.Bool, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, null, null),
				new Godot.Bridge.MethodInfo(MethodName.ReapplyMonsterScaling, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Int, "actualPlayerCount", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
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
			if (method == MethodName.IsCombatActive && args.Count == 0)
			{
				bool from = IsCombatActive();
				ret = VariantUtils.CreateFrom(in from);
				return true;
			}
			if (method == MethodName.ReapplyMonsterScaling && args.Count == 1)
			{
				ReapplyMonsterScaling(VariantUtils.ConvertTo<int>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName.IsCombatActive && args.Count == 0)
			{
				bool from = IsCombatActive();
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
			if (method == MethodName.IsCombatActive)
			{
				return true;
			}
			if (method == MethodName.ReapplyMonsterScaling)
			{
				return true;
			}
			return base.HasGodotClassMethod(in method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if (name == PropertyName._wasInCombat)
			{
				_wasInCombat = VariantUtils.ConvertTo<bool>(in value);
				return true;
			}
			if (name == PropertyName._scalingChecked)
			{
				_scalingChecked = VariantUtils.ConvertTo<bool>(in value);
				return true;
			}
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
			if (name == PropertyName._wasInCombat)
			{
				value = VariantUtils.CreateFrom(in _wasInCombat);
				return true;
			}
			if (name == PropertyName._scalingChecked)
			{
				value = VariantUtils.CreateFrom(in _scalingChecked);
				return true;
			}
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
				new Godot.Bridge.PropertyInfo(Variant.Type.Bool, PropertyName._wasInCombat, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Bool, PropertyName._scalingChecked, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._wasInCombat, Variant.From(in _wasInCombat));
			info.AddProperty(PropertyName._scalingChecked, Variant.From(in _scalingChecked));
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._wasInCombat, out var value))
			{
				_wasInCombat = value.As<bool>();
			}
			if (info.TryGetProperty(PropertyName._scalingChecked, out var value2))
			{
				_scalingChecked = value2.As<bool>();
			}
			if (info.TryGetProperty(PropertyName._frameCounter, out var value3))
			{
				_frameCounter = value3.As<int>();
			}
		}
	}

	private ReflectionCache _cache;

	private FieldInfo? _runStateField;

	public string Name => "DifficultyScaling";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_cache = cache;
		_runStateField = cache.GetField(typeof(MultiplayerScalingModel), "_runState");
	}

	public Node? CreateNode()
	{
		return new DifficultyNode(this);
	}

	public void Cleanup()
	{
	}
}
