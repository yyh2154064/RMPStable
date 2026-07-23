using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.CampfireLayout;

public class CampfireModule : IRMPModule
{
	private class CampfireNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _EnterTree = "_EnterTree";

			public new static readonly StringName _ExitTree = "_ExitTree";

			public static readonly StringName OnNodeAdded = "OnNodeAdded";

			public static readonly StringName PreInjectContainers = "PreInjectContainers";

			public new static readonly StringName _Process = "_Process";

			public static readonly StringName ArrangeVisuals = "ArrangeVisuals";

			public static readonly StringName EnsureExtraLogs = "EnsureExtraLogs";

			public static readonly StringName DuplicateShiftedNode = "DuplicateShiftedNode";

			public static readonly StringName RemoveAllChildren = "RemoveAllChildren";
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

		private readonly CampfireModule _module;

		private int _frameCounter;

		private bool _arranged;

		private NRestSiteRoom? _lastRoom;

		public CampfireNode(CampfireModule module)
		{
			_module = module;
			base.Name = "CampfireNode";
		}

		public override void _EnterTree()
		{
			GetTree().NodeAdded += OnNodeAdded;
		}

		public override void _ExitTree()
		{
			SceneTree tree = GetTree();
			if (tree != null)
			{
				tree.NodeAdded -= OnNodeAdded;
			}
		}

		private void OnNodeAdded(Node node)
		{
			if (!(node is NRestSiteRoom restSite))
			{
				return;
			}
			int playerCount = GameStateAccessor.GetPlayerCount();
			if (playerCount <= 4)
			{
				return;
			}
			if (_module._containersField == null)
			{
				Log.Warn("[RMP:Campfire] _characterContainers field not found — cannot prevent crash.");
				return;
			}
			try
			{
				PreInjectContainers(restSite, playerCount);
			}
			catch (Exception ex)
			{
				Log.Warn("[RMP:Campfire] Pre-injection failed: " + ex.Message);
			}
		}

		private void PreInjectContainers(NRestSiteRoom restSite, int playerCount)
		{
			Control nodeOrNull = restSite.GetNodeOrNull<Control>("BgContainer");
			if (nodeOrNull == null)
			{
				return;
			}
			List<Control> list = new List<Control>();
			for (int i = 1; i <= 4; i++)
			{
				Control nodeOrNull2 = nodeOrNull.GetNodeOrNull<Control>($"Character_{i}");
				if (nodeOrNull2 != null)
				{
					list.Add(nodeOrNull2);
				}
			}
			if (list.Count >= 4)
			{
				for (int j = list.Count; j < playerCount; j++)
				{
					Control control = new Control();
					control.Name = $"Character_{j + 1}";
					control.Position = GetExtraContainerPosition(list, j);
					nodeOrNull.AddChild(control, forceReadableName: false, InternalMode.Disabled);
					list.Add(control);
				}
				if (_module._containersField.GetValue(restSite) is List<Control> list2)
				{
					list2.AddRange(list);
				}
				Log.Info($"[RMP:Campfire] Pre-injected {playerCount - 4} extra containers for {playerCount} players");
			}
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 10 != 0)
			{
				return;
			}
			NRestSiteRoom nRestSiteRoom = SceneMonitor.FindRestSiteRoom();
			if (nRestSiteRoom == null || nRestSiteRoom != _lastRoom)
			{
				_arranged = false;
				_lastRoom = nRestSiteRoom;
			}
			else
			{
				if (_arranged)
				{
					return;
				}
				int playerCount = GameStateAccessor.GetPlayerCount();
				if (playerCount > 4)
				{
					try
					{
						ArrangeVisuals(nRestSiteRoom, playerCount);
						_arranged = true;
					}
					catch (Exception value)
					{
						Log.Warn($"[RMP:Campfire] Failed to arrange visuals: {value}");
						_arranged = true;
					}
				}
			}
		}

		private void ArrangeVisuals(NRestSiteRoom room, int playerCount)
		{
			if (!(_module._containersField == null) && _module._containersField.GetValue(room) is List<Control> { Count: not 0 } list)
			{
				if (list.Count < playerCount)
				{
					EnsureContainers(list, playerCount);
				}
				Control parent = list[0].GetParent<Control>();
				if (parent != null)
				{
					EnsureExtraLogs(parent);
				}
			}
		}

		private static void EnsureContainers(List<Control> containers, int requiredCount)
		{
			if (requiredCount <= containers.Count)
			{
				return;
			}
			Control parent = containers[0].GetParent<Control>();
			if (parent == null)
			{
				return;
			}
			int num = Math.Min(containers.Count, 4);
			if (num != 0)
			{
				while (containers.Count < requiredCount)
				{
					int count = containers.Count;
					Control control = (containers[count % num].Duplicate() as Control) ?? new Control();
					RemoveAllChildren(control);
					control.Name = $"Character_Auto_{count + 1}";
					control.Position = GetExtraContainerPosition(containers, count);
					parent.AddChild(control, forceReadableName: false, InternalMode.Disabled);
					containers.Add(control);
				}
			}
		}

		private static Vector2 GetExtraContainerPosition(List<Control> containers, int index)
		{
			if (containers.Count < 4)
			{
				return containers[containers.Count - 1].Position;
			}
			if (index < 4)
			{
				return containers[index].Position;
			}
			int num = index - 4;
			bool flag = num % 2 == 0;
			int num2 = num / 2;
			Vector2 result = (flag ? (containers[0].Position + LeftExtraFrontOffset) : (containers[1].Position + RightExtraFrontOffset));
			Vector2 vector = (flag ? (containers[2].Position + LeftExtraBackOffset) : (containers[3].Position + RightExtraBackOffset));
			switch (num2)
			{
			case 0:
				return result;
			case 1:
				return vector;
			default:
			{
				int num3 = num2 - 1;
				Vector2 vector2 = new Vector2((flag ? (-1f) : 1f) * ExtraSeatStep.X * (float)num3, ExtraSeatStep.Y * (float)num3);
				return vector + vector2;
			}
			}
		}

		private static void EnsureExtraLogs(Control parent)
		{
			Node node = ((parent.GetChildCount() > 0) ? parent.GetChild(0) : null);
			if (node != null && node.GetNodeOrNull<Node>("AutoExtraLogsMarker") == null)
			{
				Node node2 = new Node
				{
					Name = "AutoExtraLogsMarker"
				};
				node.AddChild(node2, forceReadableName: false, InternalMode.Disabled);
				DuplicateShiftedNode(node, "RestSiteLLog", LogXOffsetLeft, "AutoL");
				DuplicateShiftedNode(node, "RestSiteRLog", LogXOffsetRight, "AutoR");
				DuplicateShiftedNode(node, "RestSiteLighting/RestSiteLLog2", LogXOffsetLeft, "AutoL");
				DuplicateShiftedNode(node, "RestSiteLighting/RestSiteRLog2", LogXOffsetRight, "AutoR");
			}
		}

		private static void DuplicateShiftedNode(Node root, string nodePath, Vector2 offset, string suffix)
		{
			Node nodeOrNull = root.GetNodeOrNull<Node>(nodePath);
			if (nodeOrNull == null)
			{
				return;
			}
			Node parent = nodeOrNull.GetParent();
			if (parent != null)
			{
				Node node = nodeOrNull.Duplicate();
				node.Name = $"{nodeOrNull.Name}_{suffix}";
				parent.AddChild(node, forceReadableName: false, InternalMode.Disabled);
				if (nodeOrNull is Control control && node is Control control2)
				{
					control2.Position = control.Position + offset;
				}
				else if (nodeOrNull is Node2D node2D && node is Node2D node2D2)
				{
					node2D2.Position = node2D.Position + offset;
				}
			}
		}

		private static void RemoveAllChildren(Node node)
		{
			for (int num = node.GetChildCount() - 1; num >= 0; num--)
			{
				Node child = node.GetChild(num);
				node.RemoveChild(child);
				child.QueueFree();
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(9)
			{
				new Godot.Bridge.MethodInfo(MethodName._EnterTree, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName._ExitTree, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName.OnNodeAdded, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.PreInjectContainers, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "restSite", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Int, "playerCount", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.ArrangeVisuals, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "room", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Int, "playerCount", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.EnsureExtraLogs, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "parent", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.DuplicateShiftedNode, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "root", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.String, "nodePath", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Vector2, "offset", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.String, "suffix", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.RemoveAllChildren, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
				}, null)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName._EnterTree && args.Count == 0)
			{
				_EnterTree();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName._ExitTree && args.Count == 0)
			{
				_ExitTree();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnNodeAdded && args.Count == 1)
			{
				OnNodeAdded(VariantUtils.ConvertTo<Node>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.PreInjectContainers && args.Count == 2)
			{
				PreInjectContainers(VariantUtils.ConvertTo<NRestSiteRoom>(in args[0]), VariantUtils.ConvertTo<int>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName._Process && args.Count == 1)
			{
				_Process(VariantUtils.ConvertTo<double>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.ArrangeVisuals && args.Count == 2)
			{
				ArrangeVisuals(VariantUtils.ConvertTo<NRestSiteRoom>(in args[0]), VariantUtils.ConvertTo<int>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.EnsureExtraLogs && args.Count == 1)
			{
				EnsureExtraLogs(VariantUtils.ConvertTo<Control>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.DuplicateShiftedNode && args.Count == 4)
			{
				DuplicateShiftedNode(VariantUtils.ConvertTo<Node>(in args[0]), VariantUtils.ConvertTo<string>(in args[1]), VariantUtils.ConvertTo<Vector2>(in args[2]), VariantUtils.ConvertTo<string>(in args[3]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.RemoveAllChildren && args.Count == 1)
			{
				RemoveAllChildren(VariantUtils.ConvertTo<Node>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName.EnsureExtraLogs && args.Count == 1)
			{
				EnsureExtraLogs(VariantUtils.ConvertTo<Control>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.DuplicateShiftedNode && args.Count == 4)
			{
				DuplicateShiftedNode(VariantUtils.ConvertTo<Node>(in args[0]), VariantUtils.ConvertTo<string>(in args[1]), VariantUtils.ConvertTo<Vector2>(in args[2]), VariantUtils.ConvertTo<string>(in args[3]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.RemoveAllChildren && args.Count == 1)
			{
				RemoveAllChildren(VariantUtils.ConvertTo<Node>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			ret = default(godot_variant);
			return false;
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if (method == MethodName._EnterTree)
			{
				return true;
			}
			if (method == MethodName._ExitTree)
			{
				return true;
			}
			if (method == MethodName.OnNodeAdded)
			{
				return true;
			}
			if (method == MethodName.PreInjectContainers)
			{
				return true;
			}
			if (method == MethodName._Process)
			{
				return true;
			}
			if (method == MethodName.ArrangeVisuals)
			{
				return true;
			}
			if (method == MethodName.EnsureExtraLogs)
			{
				return true;
			}
			if (method == MethodName.DuplicateShiftedNode)
			{
				return true;
			}
			if (method == MethodName.RemoveAllChildren)
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
				_lastRoom = VariantUtils.ConvertTo<NRestSiteRoom>(in value);
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
		internal static List<Godot.Bridge.PropertyInfo> GetGodotPropertyList()
		{
			return new List<Godot.Bridge.PropertyInfo>
			{
				new Godot.Bridge.PropertyInfo(Variant.Type.Int, PropertyName._frameCounter, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Bool, PropertyName._arranged, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Object, PropertyName._lastRoom, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
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
				_lastRoom = value3.As<NRestSiteRoom>();
			}
		}
	}

	private static readonly Vector2 LeftExtraFrontOffset = new Vector2(-250f, 35f);

	private static readonly Vector2 LeftExtraBackOffset = new Vector2(-240f, -20f);

	private static readonly Vector2 RightExtraFrontOffset = new Vector2(250f, 35f);

	private static readonly Vector2 RightExtraBackOffset = new Vector2(240f, -20f);

	private static readonly Vector2 LogXOffsetLeft = new Vector2(-250f, 0f);

	private static readonly Vector2 LogXOffsetRight = new Vector2(250f, 0f);

	private static readonly Vector2 ExtraSeatStep = new Vector2(70f, -45f);

	private FieldInfo? _containersField;

	private ReflectionCache _cache;

	public string Name => "CampfireLayout";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_cache = cache;
		_containersField = cache.GetField(typeof(NRestSiteRoom), "_characterContainers");
	}

	public Node? CreateNode()
	{
		return new CampfireNode(this);
	}

	public void Cleanup()
	{
	}
}
