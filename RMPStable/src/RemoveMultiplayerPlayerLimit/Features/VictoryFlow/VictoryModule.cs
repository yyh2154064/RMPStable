using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.VictoryFlow;

public class VictoryModule : IRMPModule
{
	private sealed class VictoryNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";

			public static readonly StringName ResetState = "ResetState";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _lastRunNodeId = "_lastRunNodeId";

			public static readonly StringName _frameCounter = "_frameCounter";

			public static readonly StringName _missingGameOverTicks = "_missingGameOverTicks";

			public static readonly StringName _summaryForcedForCurrentRun = "_summaryForcedForCurrentRun";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private ulong _lastRunNodeId;

		private int _frameCounter;

		private int _missingGameOverTicks;

		private bool _summaryForcedForCurrentRun;

		public VictoryNode()
		{
			base.Name = "VictoryNode";
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 15 != 0)
			{
				return;
			}
			NRun instance = NRun.Instance;
			if (instance == null)
			{
				ResetState();
				return;
			}
			ulong instanceId = instance.GetInstanceId();
			if (instanceId != _lastRunNodeId)
			{
				_lastRunNodeId = instanceId;
				_missingGameOverTicks = 0;
				_summaryForcedForCurrentRun = false;
			}
			if (_summaryForcedForCurrentRun)
			{
				return;
			}
			RunState runState = GameStateAccessor.GetRunState();
			if (runState == null || !(runState.CurrentRoom?.IsVictoryRoom).GetValueOrDefault())
			{
				_missingGameOverTicks = 0;
				return;
			}
			if (NOverlayStack.Instance?.Peek() is NGameOverScreen)
			{
				_missingGameOverTicks = 0;
				return;
			}
			if (runState.Players.Count <= 0 || !runState.Players.All((Player player) => player.Creature.IsDead))
			{
				_missingGameOverTicks = 0;
				return;
			}
			_missingGameOverTicks++;
			if (_missingGameOverTicks >= 12)
			{
				Log.Warn("[RMP:Victory] Victory summary did not appear in time. Forcing GameOverScreen.");
				instance.ShowGameOverScreen(RunManager.Instance.ToSave(null));
				_summaryForcedForCurrentRun = true;
				_missingGameOverTicks = 0;
			}
		}

		private void ResetState()
		{
			_lastRunNodeId = 0uL;
			_missingGameOverTicks = 0;
			_summaryForcedForCurrentRun = false;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<MethodInfo> GetGodotMethodList()
		{
			return new List<MethodInfo>(2)
			{
				new MethodInfo(MethodName._Process, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<PropertyInfo>
				{
					new PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new MethodInfo(MethodName.ResetState, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null)
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
			if (method == MethodName.ResetState && args.Count == 0)
			{
				ResetState();
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
			if (method == MethodName.ResetState)
			{
				return true;
			}
			return base.HasGodotClassMethod(in method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
		{
			if (name == PropertyName._lastRunNodeId)
			{
				_lastRunNodeId = VariantUtils.ConvertTo<ulong>(in value);
				return true;
			}
			if (name == PropertyName._frameCounter)
			{
				_frameCounter = VariantUtils.ConvertTo<int>(in value);
				return true;
			}
			if (name == PropertyName._missingGameOverTicks)
			{
				_missingGameOverTicks = VariantUtils.ConvertTo<int>(in value);
				return true;
			}
			if (name == PropertyName._summaryForcedForCurrentRun)
			{
				_summaryForcedForCurrentRun = VariantUtils.ConvertTo<bool>(in value);
				return true;
			}
			return base.SetGodotClassPropertyValue(in name, in value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
		{
			if (name == PropertyName._lastRunNodeId)
			{
				value = VariantUtils.CreateFrom(in _lastRunNodeId);
				return true;
			}
			if (name == PropertyName._frameCounter)
			{
				value = VariantUtils.CreateFrom(in _frameCounter);
				return true;
			}
			if (name == PropertyName._missingGameOverTicks)
			{
				value = VariantUtils.CreateFrom(in _missingGameOverTicks);
				return true;
			}
			if (name == PropertyName._summaryForcedForCurrentRun)
			{
				value = VariantUtils.CreateFrom(in _summaryForcedForCurrentRun);
				return true;
			}
			return base.GetGodotClassPropertyValue(in name, out value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			return new List<PropertyInfo>
			{
				new PropertyInfo(Variant.Type.Int, PropertyName._lastRunNodeId, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new PropertyInfo(Variant.Type.Int, PropertyName._missingGameOverTicks, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new PropertyInfo(Variant.Type.Bool, PropertyName._summaryForcedForCurrentRun, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._lastRunNodeId, Variant.From(in _lastRunNodeId));
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
			info.AddProperty(PropertyName._missingGameOverTicks, Variant.From(in _missingGameOverTicks));
			info.AddProperty(PropertyName._summaryForcedForCurrentRun, Variant.From(in _summaryForcedForCurrentRun));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._lastRunNodeId, out var value))
			{
				_lastRunNodeId = value.As<ulong>();
			}
			if (info.TryGetProperty(PropertyName._frameCounter, out var value2))
			{
				_frameCounter = value2.As<int>();
			}
			if (info.TryGetProperty(PropertyName._missingGameOverTicks, out var value3))
			{
				_missingGameOverTicks = value3.As<int>();
			}
			if (info.TryGetProperty(PropertyName._summaryForcedForCurrentRun, out var value4))
			{
				_summaryForcedForCurrentRun = value4.As<bool>();
			}
		}
	}

	private const int CheckIntervalFrames = 15;

	private const int GracePeriodTicks = 12;

	public string Name => "VictoryFlow";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
	}

	public Node? CreateNode()
	{
		return new VictoryNode();
	}

	public void Cleanup()
	{
	}
}
