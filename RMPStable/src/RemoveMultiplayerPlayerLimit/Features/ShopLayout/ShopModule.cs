using System;
using System.Collections.Generic;
using System.ComponentModel;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.ShopLayout;

public class ShopModule : IRMPModule
{
	private class ShopNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";

			public static readonly StringName RepositionVisuals = "RepositionVisuals";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";

			public static readonly StringName _arranged = "_arranged";

			public static readonly StringName _lastRoom = "_lastRoom";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly ShopModule _module;

		private int _frameCounter;

		private bool _arranged;

		private NMerchantRoom? _lastRoom;

		public ShopNode(ShopModule module)
		{
			_module = module;
			base.Name = "ShopNode";
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 10 != 0)
			{
				return;
			}
			NMerchantRoom nMerchantRoom = SceneMonitor.FindMerchantRoom();
			if (nMerchantRoom == null || nMerchantRoom != _lastRoom)
			{
				_arranged = false;
				_lastRoom = nMerchantRoom;
			}
			else if (!_arranged)
			{
				try
				{
					RepositionVisuals(nMerchantRoom);
					_arranged = true;
				}
				catch (Exception value)
				{
					Log.Warn($"[RMP:Shop] Failed to reposition visuals: {value}");
					_arranged = true;
				}
			}
		}

		private void RepositionVisuals(NMerchantRoom room)
		{
			IReadOnlyList<NMerchantCharacter> playerVisuals = room.PlayerVisuals;
			if (playerVisuals.Count <= 4)
			{
				return;
			}
			int num = ((playerVisuals.Count <= 8) ? 2 : Mathf.CeilToInt((float)playerVisuals.Count / 4f));
			int num2 = Mathf.CeilToInt((float)playerVisuals.Count / (float)num);
			int num3 = 0;
			for (int i = 0; i < num; i++)
			{
				float num4 = 160f + -110f * (float)i;
				float y = 35f + -40f * (float)i;
				for (int j = 0; j < num2; j++)
				{
					if (num3 >= playerVisuals.Count)
					{
						break;
					}
					playerVisuals[num3].Position = new Vector2(num4, y);
					num4 += -230f;
					num3++;
				}
			}
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
				new MethodInfo(MethodName.RepositionVisuals, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<PropertyInfo>
				{
					new PropertyInfo(Variant.Type.Object, "room", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
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
			if (method == MethodName.RepositionVisuals && args.Count == 1)
			{
				RepositionVisuals(VariantUtils.ConvertTo<NMerchantRoom>(in args[0]));
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
			if (method == MethodName.RepositionVisuals)
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
			if (name == PropertyName._arranged)
			{
				_arranged = VariantUtils.ConvertTo<bool>(in value);
				return true;
			}
			if (name == PropertyName._lastRoom)
			{
				_lastRoom = VariantUtils.ConvertTo<NMerchantRoom>(in value);
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
			if (name == PropertyName._arranged)
			{
				value = VariantUtils.CreateFrom(in _arranged);
				return true;
			}
			if (name == PropertyName._lastRoom)
			{
				value = VariantUtils.CreateFrom(in _lastRoom);
				return true;
			}
			return base.GetGodotClassPropertyValue(in name, out value);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<PropertyInfo> GetGodotPropertyList()
		{
			return new List<PropertyInfo>
			{
				new PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new PropertyInfo(Variant.Type.Bool, PropertyName._arranged, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new PropertyInfo(Variant.Type.Object, PropertyName._lastRoom, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
			info.AddProperty(PropertyName._arranged, Variant.From(in _arranged));
			info.AddProperty(PropertyName._lastRoom, Variant.From(in _lastRoom));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
			if (info.TryGetProperty(PropertyName._arranged, out var value2))
			{
				_arranged = value2.As<bool>();
			}
			if (info.TryGetProperty(PropertyName._lastRoom, out var value3))
			{
				_lastRoom = value3.As<NMerchantRoom>();
			}
		}
	}

	private const float ForwardShiftX = 160f;

	private const float ForwardShiftY = 35f;

	private const float RowStartOffsetX = -110f;

	private const float RowStepY = -40f;

	private const float ColumnStepX = -230f;

	private ReflectionCache _cache;

	public string Name => "ShopLayout";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_cache = cache;
	}

	public Node? CreateNode()
	{
		return new ShopNode(this);
	}

	public void Cleanup()
	{
	}
}
