using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.addons.mega_text;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.SettingsUI;

public class SettingsModule : IRMPModule
{
	private class SettingsNode : Node
	{
		public new class MethodName : Node.MethodName
		{
			public new static readonly StringName _Process = "_Process";

			public static readonly StringName InjectSettings = "InjectSettings";

			public static readonly StringName RemoveExistingInjectedControls = "RemoveExistingInjectedControls";

			public static readonly StringName RemoveInjectedControl = "RemoveInjectedControl";

			public static readonly StringName CreateSettingsRow = "CreateSettingsRow";

			public static readonly StringName CreateModPaginator = "CreateModPaginator";

			public static readonly StringName AdoptOwnership = "AdoptOwnership";

			public static readonly StringName SetupDifficultyPaginator = "SetupDifficultyPaginator";

			public static readonly StringName OnPaginatorChanged = "OnPaginatorChanged";

			public static readonly StringName RebuildFocusChain = "RebuildFocusChain";
		}

		public new class PropertyName : Node.PropertyName
		{
			public static readonly StringName _frameCounter = "_frameCounter";

			public static readonly StringName _injected = "_injected";

			public static readonly StringName _lastScreen = "_lastScreen";
		}

		public new class SignalName : Node.SignalName
		{
		}

		private readonly SettingsModule _mod;

		private int _frameCounter;

		private bool _injected;

		private NSettingsScreen? _lastScreen;

		public SettingsNode(SettingsModule mod)
		{
			_mod = mod;
			base.Name = "SettingsNode";
		}

		public override void _Process(double delta)
		{
			if (++_frameCounter % 30 != 0)
			{
				return;
			}
			NSettingsScreen nSettingsScreen = SceneMonitor.FindSettingsScreen();
			if (nSettingsScreen == null && _lastScreen != null)
			{
				_mod._config.Save();
				_injected = false;
				_lastScreen = null;
				return;
			}
			if (nSettingsScreen != _lastScreen)
			{
				_lastScreen = nSettingsScreen;
				_injected = false;
			}
			if (nSettingsScreen == null || _injected)
			{
				return;
			}
			try
			{
				InjectSettings(nSettingsScreen);
				_injected = true;
			}
			catch (Exception value)
			{
				Log.Warn($"[RMP:Settings] Injection failed: {value}");
				_injected = true;
			}
		}

		private void InjectSettings(NSettingsScreen screen)
		{
			NSettingsPanel node = screen.GetNode<NSettingsPanel>("%GeneralSettings");
			VBoxContainer content = node.Content;
			RemoveExistingInjectedControls(content);
			Control control = screen.GetNodeOrNull<Control>("%Modding") ?? screen.GetNodeOrNull<Control>("%SendFeedback");
			if (control == null)
			{
				Log.Warn("[RMP:Settings] Anchor node not found.");
				return;
			}
			int num = control.GetIndex() + 1;
			RichTextLabel richTextLabel = content.GetNodeOrNull<RichTextLabel>("Screenshake/Label") ?? control.GetNodeOrNull<RichTextLabel>("Label");
			ColorRect colorRect = new ColorRect
			{
				Name = "RmpDivider",
				CustomMinimumSize = new Vector2(0f, 2f),
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Color = DividerColor
			};
			content.AddChild(colorRect, forceReadableName: false, InternalMode.Disabled);
			content.MoveChild(colorRect, num);
			MarginContainer marginContainer = CreateSettingsRow("RmpDifficultyScaling");
			if (richTextLabel != null)
			{
				RichTextLabel richTextLabel2 = (RichTextLabel)richTextLabel.Duplicate();
				richTextLabel2.Text = Localization.Get("SETTINGS_DIFFICULTY_SCALING_LABEL", "Difficulty Scaling");
				richTextLabel2.MouseFilter = Control.MouseFilterEnum.Ignore;
				marginContainer.AddChild(richTextLabel2, forceReadableName: false, InternalMode.Disabled);
			}
			NPaginator nPaginator = CreateModPaginator("DifficultyScalingPaginator");
			if (nPaginator != null)
			{
				marginContainer.AddChild(nPaginator, forceReadableName: false, InternalMode.Disabled);
				content.AddChild(marginContainer, forceReadableName: false, InternalMode.Disabled);
				content.MoveChild(marginContainer, num + 1);
				SetupDifficultyPaginator(nPaginator);
			}
			RebuildFocusChain(node);
		}

		private static void RemoveExistingInjectedControls(VBoxContainer vbox)
		{
			RemoveInjectedControl(vbox, "RmpDivider");
			RemoveInjectedControl(vbox, "RmpDifficultyScaling");
			RemoveInjectedControl(vbox, "RmpPlayerLimit");
		}

		private static void RemoveInjectedControl(VBoxContainer vbox, string controlName)
		{
			Control nodeOrNull = vbox.GetNodeOrNull<Control>(controlName);
			if (nodeOrNull != null)
			{
				vbox.RemoveChild(nodeOrNull);
				nodeOrNull.QueueFree();
			}
		}

		private static MarginContainer CreateSettingsRow(string name)
		{
			MarginContainer marginContainer = new MarginContainer();
			marginContainer.Name = name;
			marginContainer.CustomMinimumSize = new Vector2(0f, 64f);
			marginContainer.AddThemeConstantOverride("margin_left", 12);
			marginContainer.AddThemeConstantOverride("margin_top", 0);
			marginContainer.AddThemeConstantOverride("margin_right", 12);
			marginContainer.AddThemeConstantOverride("margin_bottom", 0);
			return marginContainer;
		}

		private NPaginator? CreateModPaginator(string name)
		{
			PackedScene packedScene = ResourceLoader.Load<PackedScene>(SceneHelper.GetScenePath("screens/paginator"), null, ResourceLoader.CacheMode.Reuse);
			if (packedScene == null)
			{
				return null;
			}
			Node node = packedScene.Instantiate(PackedScene.GenEditState.Disabled);
			RmpPaginator rmpPaginator = new RmpPaginator(delegate(NPaginator p, int idx)
			{
				OnPaginatorChanged(p, idx);
			})
			{
				Name = name,
				CustomMinimumSize = new Vector2(324f, 64f),
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
				FocusMode = Control.FocusModeEnum.All,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			foreach (Node item in new List<Node>(node.GetChildren()))
			{
				node.RemoveChild(item);
				rmpPaginator.AddChild(item, forceReadableName: false, InternalMode.Disabled);
				AdoptOwnership(item, node, rmpPaginator);
			}
			node.Free();
			return rmpPaginator;
		}

		private static void AdoptOwnership(Node node, Node oldOwner, Node newOwner)
		{
			if (node.Owner == oldOwner)
			{
				node.Owner = newOwner;
			}
			foreach (Node child in node.GetChildren())
			{
				AdoptOwnership(child, oldOwner, newOwner);
			}
		}

		private void SetupDifficultyPaginator(NPaginator paginator)
		{
			NPaginator paginator2 = paginator;
			if (_mod._paginatorOptionsField?.GetValue(paginator2) is List<string> list)
			{
				list.Clear();
				list.Add("OFF");
				list.Add("ON");
				int num = (ProtocolConfig.DifficultyScalingEnabled ? 1 : 0);
				_mod._paginatorCurrentIndexField?.SetValue(paginator2, num);
				if (_mod._paginatorLabelField?.GetValue(paginator2) is MegaLabel megaLabel)
				{
					megaLabel.SetTextAutoSize(list[num]);
				}
				_mod._difficultyPaginators.Add(paginator2);
				paginator2.TreeExiting += delegate
				{
					_mod._difficultyPaginators.Remove(paginator2);
				};
			}
		}

		private void OnPaginatorChanged(NPaginator paginator, int index)
		{
			if (_mod._difficultyPaginators.Contains(paginator) && _mod._paginatorOptionsField?.GetValue(paginator) is List<string> list && index >= 0 && index < list.Count)
			{
				if (_mod._paginatorLabelField?.GetValue(paginator) is MegaLabel megaLabel)
				{
					megaLabel.SetTextAutoSize(list[index]);
				}
				ProtocolConfig.SetDifficultyScalingEnabled(list[index] == "ON");
				_mod._config.Save();
			}
		}

		private void RebuildFocusChain(NSettingsPanel panel)
		{
			if (!(_mod._getSettingsOptionsMethod == null) && !(_mod._panelFirstControlField == null))
			{
				List<Control> list = new List<Control>();
				_mod._getSettingsOptionsMethod.Invoke(panel, new object[2] { panel.Content, list });
				for (int i = 0; i < list.Count; i++)
				{
					list[i].FocusNeighborLeft = list[i].GetPath();
					list[i].FocusNeighborRight = list[i].GetPath();
					list[i].FocusNeighborTop = ((i > 0) ? list[i - 1].GetPath() : list[i].GetPath());
					list[i].FocusNeighborBottom = ((i < list.Count - 1) ? list[i + 1].GetPath() : list[i].GetPath());
				}
				if (list.Count > 0)
				{
					_mod._panelFirstControlField.SetValue(panel, list[0]);
				}
			}
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(10)
			{
				new Godot.Bridge.MethodInfo(MethodName._Process, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Float, "delta", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.InjectSettings, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "screen", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.RemoveExistingInjectedControls, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "vbox", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("VBoxContainer"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.RemoveInjectedControl, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "vbox", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("VBoxContainer"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.String, "controlName", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.CreateSettingsRow, new Godot.Bridge.PropertyInfo(Variant.Type.Object, "", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("MarginContainer"), exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.String, "name", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.CreateModPaginator, new Godot.Bridge.PropertyInfo(Variant.Type.Object, "", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.String, "name", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.AdoptOwnership, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal | MethodFlags.Static, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "oldOwner", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "newOwner", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.SetupDifficultyPaginator, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "paginator", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.OnPaginatorChanged, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "paginator", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false),
					new Godot.Bridge.PropertyInfo(Variant.Type.Int, "index", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null),
				new Godot.Bridge.MethodInfo(MethodName.RebuildFocusChain, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Object, "panel", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Control"), exported: false)
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
			if (method == MethodName.InjectSettings && args.Count == 1)
			{
				InjectSettings(VariantUtils.ConvertTo<NSettingsScreen>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.RemoveExistingInjectedControls && args.Count == 1)
			{
				RemoveExistingInjectedControls(VariantUtils.ConvertTo<VBoxContainer>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.RemoveInjectedControl && args.Count == 2)
			{
				RemoveInjectedControl(VariantUtils.ConvertTo<VBoxContainer>(in args[0]), VariantUtils.ConvertTo<string>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.CreateSettingsRow && args.Count == 1)
			{
				MarginContainer from = CreateSettingsRow(VariantUtils.ConvertTo<string>(in args[0]));
				ret = VariantUtils.CreateFrom(in from);
				return true;
			}
			if (method == MethodName.CreateModPaginator && args.Count == 1)
			{
				NPaginator from2 = CreateModPaginator(VariantUtils.ConvertTo<string>(in args[0]));
				ret = VariantUtils.CreateFrom(in from2);
				return true;
			}
			if (method == MethodName.AdoptOwnership && args.Count == 3)
			{
				AdoptOwnership(VariantUtils.ConvertTo<Node>(in args[0]), VariantUtils.ConvertTo<Node>(in args[1]), VariantUtils.ConvertTo<Node>(in args[2]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.SetupDifficultyPaginator && args.Count == 1)
			{
				SetupDifficultyPaginator(VariantUtils.ConvertTo<NPaginator>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnPaginatorChanged && args.Count == 2)
			{
				OnPaginatorChanged(VariantUtils.ConvertTo<NPaginator>(in args[0]), VariantUtils.ConvertTo<int>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.RebuildFocusChain && args.Count == 1)
			{
				RebuildFocusChain(VariantUtils.ConvertTo<NSettingsPanel>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static bool InvokeGodotClassStaticMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName.RemoveExistingInjectedControls && args.Count == 1)
			{
				RemoveExistingInjectedControls(VariantUtils.ConvertTo<VBoxContainer>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.RemoveInjectedControl && args.Count == 2)
			{
				RemoveInjectedControl(VariantUtils.ConvertTo<VBoxContainer>(in args[0]), VariantUtils.ConvertTo<string>(in args[1]));
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.CreateSettingsRow && args.Count == 1)
			{
				MarginContainer from = CreateSettingsRow(VariantUtils.ConvertTo<string>(in args[0]));
				ret = VariantUtils.CreateFrom(in from);
				return true;
			}
			if (method == MethodName.AdoptOwnership && args.Count == 3)
			{
				AdoptOwnership(VariantUtils.ConvertTo<Node>(in args[0]), VariantUtils.ConvertTo<Node>(in args[1]), VariantUtils.ConvertTo<Node>(in args[2]));
				ret = default(godot_variant);
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
			if (method == MethodName.InjectSettings)
			{
				return true;
			}
			if (method == MethodName.RemoveExistingInjectedControls)
			{
				return true;
			}
			if (method == MethodName.RemoveInjectedControl)
			{
				return true;
			}
			if (method == MethodName.CreateSettingsRow)
			{
				return true;
			}
			if (method == MethodName.CreateModPaginator)
			{
				return true;
			}
			if (method == MethodName.AdoptOwnership)
			{
				return true;
			}
			if (method == MethodName.SetupDifficultyPaginator)
			{
				return true;
			}
			if (method == MethodName.OnPaginatorChanged)
			{
				return true;
			}
			if (method == MethodName.RebuildFocusChain)
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
			if (name == PropertyName._injected)
			{
				_injected = VariantUtils.ConvertTo<bool>(in value);
				return true;
			}
			if (name == PropertyName._lastScreen)
			{
				_lastScreen = VariantUtils.ConvertTo<NSettingsScreen>(in value);
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
			if (name == PropertyName._injected)
			{
				value = VariantUtils.CreateFrom(in _injected);
				return true;
			}
			if (name == PropertyName._lastScreen)
			{
				value = VariantUtils.CreateFrom(in _lastScreen);
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
				new Godot.Bridge.PropertyInfo(Variant.Type.Bool, PropertyName._injected, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
				new Godot.Bridge.PropertyInfo(Variant.Type.Object, PropertyName._lastScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
			info.AddProperty(PropertyName._frameCounter, Variant.From(in _frameCounter));
			info.AddProperty(PropertyName._injected, Variant.From(in _injected));
			info.AddProperty(PropertyName._lastScreen, Variant.From(in _lastScreen));
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
			if (info.TryGetProperty(PropertyName._frameCounter, out var value))
			{
				_frameCounter = value.As<int>();
			}
			if (info.TryGetProperty(PropertyName._injected, out var value2))
			{
				_injected = value2.As<bool>();
			}
			if (info.TryGetProperty(PropertyName._lastScreen, out var value3))
			{
				_lastScreen = value3.As<NSettingsScreen>();
			}
		}
	}

	private class RmpPaginator : NPaginator
	{
		public new class MethodName : NPaginator.MethodName
		{
			public new static readonly StringName _Ready = "_Ready";

			public new static readonly StringName OnIndexChanged = "OnIndexChanged";
		}

		public new class PropertyName : NPaginator.PropertyName
		{
		}

		public new class SignalName : NPaginator.SignalName
		{
		}

		private readonly Action<NPaginator, int> _callback;

		public RmpPaginator(Action<NPaginator, int> callback)
		{
			_callback = callback;
		}

		public override void _Ready()
		{
			ConnectSignals();
		}

		protected override void OnIndexChanged(int index)
		{
			_callback(this, index);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		internal static List<Godot.Bridge.MethodInfo> GetGodotMethodList()
		{
			return new List<Godot.Bridge.MethodInfo>(2)
			{
				new Godot.Bridge.MethodInfo(MethodName._Ready, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
				new Godot.Bridge.MethodInfo(MethodName.OnIndexChanged, new Godot.Bridge.PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<Godot.Bridge.PropertyInfo>
				{
					new Godot.Bridge.PropertyInfo(Variant.Type.Int, "index", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false)
				}, null)
			};
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool InvokeGodotClassMethod(in godot_string_name method, NativeVariantPtrArgs args, out godot_variant ret)
		{
			if (method == MethodName._Ready && args.Count == 0)
			{
				_Ready();
				ret = default(godot_variant);
				return true;
			}
			if (method == MethodName.OnIndexChanged && args.Count == 1)
			{
				OnIndexChanged(VariantUtils.ConvertTo<int>(in args[0]));
				ret = default(godot_variant);
				return true;
			}
			return base.InvokeGodotClassMethod(in method, args, out ret);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override bool HasGodotClassMethod(in godot_string_name method)
		{
			if (method == MethodName._Ready)
			{
				return true;
			}
			if (method == MethodName.OnIndexChanged)
			{
				return true;
			}
			return base.HasGodotClassMethod(in method);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void SaveGodotObjectData(GodotSerializationInfo info)
		{
			base.SaveGodotObjectData(info);
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		protected override void RestoreGodotObjectData(GodotSerializationInfo info)
		{
			base.RestoreGodotObjectData(info);
		}
	}

	private static readonly Color DividerColor = new Color(0.91f, 0.86f, 0.75f, 0.25f);

	private const string DividerName = "RmpDivider";

	private const string DifficultyScalingRowName = "RmpDifficultyScaling";

	private ReflectionCache _cache;

	private ConfigManager _config;

	private FieldInfo? _paginatorOptionsField;

	private FieldInfo? _paginatorCurrentIndexField;

	private FieldInfo? _paginatorLabelField;

	private System.Reflection.MethodInfo? _getSettingsOptionsMethod;

	private FieldInfo? _panelFirstControlField;

	private readonly HashSet<NPaginator> _difficultyPaginators = new HashSet<NPaginator>();

	public string Name => "SettingsUI";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		_cache = cache;
		_config = config;
		_paginatorOptionsField = cache.GetField(typeof(NPaginator), "_options");
		_paginatorCurrentIndexField = cache.GetField(typeof(NPaginator), "_currentIndex");
		_paginatorLabelField = cache.GetField(typeof(NPaginator), "_label");
		_getSettingsOptionsMethod = cache.GetMethod(typeof(NSettingsPanel), "GetSettingsOptionsRecursive");
		_panelFirstControlField = cache.GetField(typeof(NSettingsPanel), "_firstControl");
	}

	public Node? CreateNode()
	{
		return new SettingsNode(this);
	}

	public void Cleanup()
	{
		_difficultyPaginators.Clear();
	}
}
