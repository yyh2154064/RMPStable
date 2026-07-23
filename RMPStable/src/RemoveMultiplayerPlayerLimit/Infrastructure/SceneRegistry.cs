using System.Collections.Generic;
using System.ComponentModel;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

[ScriptPath("res://src/Infrastructure/SceneRegistry.cs")]
public class SceneRegistry : Node
{
	public new class MethodName : Node.MethodName
	{
		public new static readonly StringName _EnterTree = "_EnterTree";

		public new static readonly StringName _ExitTree = "_ExitTree";

		public static readonly StringName OnNodeAdded = "OnNodeAdded";

		public static readonly StringName IndexExistingTree = "IndexExistingTree";

		public static readonly StringName Register = "Register";
	}

	public new class PropertyName : Node.PropertyName
	{
		public static readonly StringName SettingsScreen = "SettingsScreen";

		public static readonly StringName RestSiteRoom = "RestSiteRoom";

		public static readonly StringName MerchantRoom = "MerchantRoom";

		public static readonly StringName TreasureRoomRelicCollection = "TreasureRoomRelicCollection";

		public static readonly StringName CharacterSelectScreen = "CharacterSelectScreen";

		public static readonly StringName CustomRunScreen = "CustomRunScreen";

		public static readonly StringName DailyRunScreen = "DailyRunScreen";

		public static readonly StringName MultiplayerLoadGameScreen = "MultiplayerLoadGameScreen";

		public static readonly StringName CustomRunLoadScreen = "CustomRunLoadScreen";

		public static readonly StringName DailyRunLoadScreen = "DailyRunLoadScreen";

		public static readonly StringName MultiplayerSubmenu = "MultiplayerSubmenu";

		public static readonly StringName MultiplayerHostSubmenu = "MultiplayerHostSubmenu";

		public static readonly StringName _settingsScreen = "_settingsScreen";

		public static readonly StringName _restSiteRoom = "_restSiteRoom";

		public static readonly StringName _merchantRoom = "_merchantRoom";

		public static readonly StringName _treasureRoomRelicCollection = "_treasureRoomRelicCollection";

		public static readonly StringName _characterSelectScreen = "_characterSelectScreen";

		public static readonly StringName _customRunScreen = "_customRunScreen";

		public static readonly StringName _dailyRunScreen = "_dailyRunScreen";

		public static readonly StringName _multiplayerLoadGameScreen = "_multiplayerLoadGameScreen";

		public static readonly StringName _customRunLoadScreen = "_customRunLoadScreen";

		public static readonly StringName _dailyRunLoadScreen = "_dailyRunLoadScreen";

		public static readonly StringName _multiplayerSubmenu = "_multiplayerSubmenu";

		public static readonly StringName _multiplayerHostSubmenu = "_multiplayerHostSubmenu";
	}

	public new class SignalName : Node.SignalName
	{
	}

	private NSettingsScreen? _settingsScreen;

	private NRestSiteRoom? _restSiteRoom;

	private NMerchantRoom? _merchantRoom;

	private NTreasureRoomRelicCollection? _treasureRoomRelicCollection;

	private NCharacterSelectScreen? _characterSelectScreen;

	private NCustomRunScreen? _customRunScreen;

	private NDailyRunScreen? _dailyRunScreen;

	private NMultiplayerLoadGameScreen? _multiplayerLoadGameScreen;

	private NCustomRunLoadScreen? _customRunLoadScreen;

	private NDailyRunLoadScreen? _dailyRunLoadScreen;

	private NMultiplayerSubmenu? _multiplayerSubmenu;

	private NMultiplayerHostSubmenu? _multiplayerHostSubmenu;

	public static SceneRegistry? Instance { get; private set; }

	internal NSettingsScreen? SettingsScreen => Validate(ref _settingsScreen);

	internal NRestSiteRoom? RestSiteRoom => Validate(ref _restSiteRoom);

	internal NMerchantRoom? MerchantRoom => Validate(ref _merchantRoom);

	internal NTreasureRoomRelicCollection? TreasureRoomRelicCollection => Validate(ref _treasureRoomRelicCollection);

	internal NCharacterSelectScreen? CharacterSelectScreen => Validate(ref _characterSelectScreen);

	internal NCustomRunScreen? CustomRunScreen => Validate(ref _customRunScreen);

	internal NDailyRunScreen? DailyRunScreen => Validate(ref _dailyRunScreen);

	internal NMultiplayerLoadGameScreen? MultiplayerLoadGameScreen => Validate(ref _multiplayerLoadGameScreen);

	internal NCustomRunLoadScreen? CustomRunLoadScreen => Validate(ref _customRunLoadScreen);

	internal NDailyRunLoadScreen? DailyRunLoadScreen => Validate(ref _dailyRunLoadScreen);

	internal NMultiplayerSubmenu? MultiplayerSubmenu => Validate(ref _multiplayerSubmenu);

	internal NMultiplayerHostSubmenu? MultiplayerHostSubmenu => Validate(ref _multiplayerHostSubmenu);

	public override void _EnterTree()
	{
		base.Name = "SceneRegistry";
		Instance = this;
		SceneTree tree = GetTree();
		tree.NodeAdded += OnNodeAdded;
		IndexExistingTree(tree.Root);
	}

	public override void _ExitTree()
	{
		SceneTree tree = GetTree();
		if (tree != null)
		{
			tree.NodeAdded -= OnNodeAdded;
		}
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void OnNodeAdded(Node node)
	{
		Register(node);
	}

	private void IndexExistingTree(Node node)
	{
		Register(node);
		foreach (Node child in node.GetChildren())
		{
			IndexExistingTree(child);
		}
	}

	private void Register(Node node)
	{
		if (!(node is NSettingsScreen settingsScreen))
		{
			if (!(node is NRestSiteRoom restSiteRoom))
			{
				if (!(node is NMerchantRoom merchantRoom))
				{
					if (!(node is NTreasureRoomRelicCollection treasureRoomRelicCollection))
					{
						if (!(node is NCharacterSelectScreen characterSelectScreen))
						{
							if (!(node is NCustomRunScreen customRunScreen))
							{
								if (!(node is NDailyRunScreen dailyRunScreen))
								{
									if (!(node is NMultiplayerLoadGameScreen multiplayerLoadGameScreen))
									{
										if (!(node is NCustomRunLoadScreen customRunLoadScreen))
										{
											if (!(node is NDailyRunLoadScreen dailyRunLoadScreen))
											{
												if (!(node is NMultiplayerSubmenu multiplayerSubmenu))
												{
													if (node is NMultiplayerHostSubmenu multiplayerHostSubmenu)
													{
														_multiplayerHostSubmenu = multiplayerHostSubmenu;
													}
												}
												else
												{
													_multiplayerSubmenu = multiplayerSubmenu;
												}
											}
											else
											{
												_dailyRunLoadScreen = dailyRunLoadScreen;
											}
										}
										else
										{
											_customRunLoadScreen = customRunLoadScreen;
										}
									}
									else
									{
										_multiplayerLoadGameScreen = multiplayerLoadGameScreen;
									}
								}
								else
								{
									_dailyRunScreen = dailyRunScreen;
								}
							}
							else
							{
								_customRunScreen = customRunScreen;
							}
						}
						else
						{
							_characterSelectScreen = characterSelectScreen;
						}
					}
					else
					{
						_treasureRoomRelicCollection = treasureRoomRelicCollection;
					}
				}
				else
				{
					_merchantRoom = merchantRoom;
				}
			}
			else
			{
				_restSiteRoom = restSiteRoom;
			}
		}
		else
		{
			_settingsScreen = settingsScreen;
		}
	}

	private static T? Validate<T>(ref T? node) where T : Node
	{
		if (node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
		{
			return node;
		}
		node = null;
		return null;
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	internal static List<MethodInfo> GetGodotMethodList()
	{
		return new List<MethodInfo>(5)
		{
			new MethodInfo(MethodName._EnterTree, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
			new MethodInfo(MethodName._ExitTree, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, null, null),
			new MethodInfo(MethodName.OnNodeAdded, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<PropertyInfo>
			{
				new PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
			}, null),
			new MethodInfo(MethodName.IndexExistingTree, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<PropertyInfo>
			{
				new PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
			}, null),
			new MethodInfo(MethodName.Register, new PropertyInfo(Variant.Type.Nil, "", PropertyHint.None, "", PropertyUsageFlags.Default, exported: false), MethodFlags.Normal, new List<PropertyInfo>
			{
				new PropertyInfo(Variant.Type.Object, "node", PropertyHint.None, "", PropertyUsageFlags.Default, new StringName("Node"), exported: false)
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
		if (method == MethodName.IndexExistingTree && args.Count == 1)
		{
			IndexExistingTree(VariantUtils.ConvertTo<Node>(in args[0]));
			ret = default(godot_variant);
			return true;
		}
		if (method == MethodName.Register && args.Count == 1)
		{
			Register(VariantUtils.ConvertTo<Node>(in args[0]));
			ret = default(godot_variant);
			return true;
		}
		return base.InvokeGodotClassMethod(in method, args, out ret);
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
		if (method == MethodName.IndexExistingTree)
		{
			return true;
		}
		if (method == MethodName.Register)
		{
			return true;
		}
		return base.HasGodotClassMethod(in method);
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override bool SetGodotClassPropertyValue(in godot_string_name name, in godot_variant value)
	{
		if (name == PropertyName._settingsScreen)
		{
			_settingsScreen = VariantUtils.ConvertTo<NSettingsScreen>(in value);
			return true;
		}
		if (name == PropertyName._restSiteRoom)
		{
			_restSiteRoom = VariantUtils.ConvertTo<NRestSiteRoom>(in value);
			return true;
		}
		if (name == PropertyName._merchantRoom)
		{
			_merchantRoom = VariantUtils.ConvertTo<NMerchantRoom>(in value);
			return true;
		}
		if (name == PropertyName._treasureRoomRelicCollection)
		{
			_treasureRoomRelicCollection = VariantUtils.ConvertTo<NTreasureRoomRelicCollection>(in value);
			return true;
		}
		if (name == PropertyName._characterSelectScreen)
		{
			_characterSelectScreen = VariantUtils.ConvertTo<NCharacterSelectScreen>(in value);
			return true;
		}
		if (name == PropertyName._customRunScreen)
		{
			_customRunScreen = VariantUtils.ConvertTo<NCustomRunScreen>(in value);
			return true;
		}
		if (name == PropertyName._dailyRunScreen)
		{
			_dailyRunScreen = VariantUtils.ConvertTo<NDailyRunScreen>(in value);
			return true;
		}
		if (name == PropertyName._multiplayerLoadGameScreen)
		{
			_multiplayerLoadGameScreen = VariantUtils.ConvertTo<NMultiplayerLoadGameScreen>(in value);
			return true;
		}
		if (name == PropertyName._customRunLoadScreen)
		{
			_customRunLoadScreen = VariantUtils.ConvertTo<NCustomRunLoadScreen>(in value);
			return true;
		}
		if (name == PropertyName._dailyRunLoadScreen)
		{
			_dailyRunLoadScreen = VariantUtils.ConvertTo<NDailyRunLoadScreen>(in value);
			return true;
		}
		if (name == PropertyName._multiplayerSubmenu)
		{
			_multiplayerSubmenu = VariantUtils.ConvertTo<NMultiplayerSubmenu>(in value);
			return true;
		}
		if (name == PropertyName._multiplayerHostSubmenu)
		{
			_multiplayerHostSubmenu = VariantUtils.ConvertTo<NMultiplayerHostSubmenu>(in value);
			return true;
		}
		return base.SetGodotClassPropertyValue(in name, in value);
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override bool GetGodotClassPropertyValue(in godot_string_name name, out godot_variant value)
	{
		if (name == PropertyName.SettingsScreen)
		{
			NSettingsScreen from = SettingsScreen;
			value = VariantUtils.CreateFrom(in from);
			return true;
		}
		if (name == PropertyName.RestSiteRoom)
		{
			NRestSiteRoom from2 = RestSiteRoom;
			value = VariantUtils.CreateFrom(in from2);
			return true;
		}
		if (name == PropertyName.MerchantRoom)
		{
			NMerchantRoom from3 = MerchantRoom;
			value = VariantUtils.CreateFrom(in from3);
			return true;
		}
		if (name == PropertyName.TreasureRoomRelicCollection)
		{
			NTreasureRoomRelicCollection from4 = TreasureRoomRelicCollection;
			value = VariantUtils.CreateFrom(in from4);
			return true;
		}
		if (name == PropertyName.CharacterSelectScreen)
		{
			NCharacterSelectScreen from5 = CharacterSelectScreen;
			value = VariantUtils.CreateFrom(in from5);
			return true;
		}
		if (name == PropertyName.CustomRunScreen)
		{
			NCustomRunScreen from6 = CustomRunScreen;
			value = VariantUtils.CreateFrom(in from6);
			return true;
		}
		if (name == PropertyName.DailyRunScreen)
		{
			NDailyRunScreen from7 = DailyRunScreen;
			value = VariantUtils.CreateFrom(in from7);
			return true;
		}
		if (name == PropertyName.MultiplayerLoadGameScreen)
		{
			NMultiplayerLoadGameScreen from8 = MultiplayerLoadGameScreen;
			value = VariantUtils.CreateFrom(in from8);
			return true;
		}
		if (name == PropertyName.CustomRunLoadScreen)
		{
			NCustomRunLoadScreen from9 = CustomRunLoadScreen;
			value = VariantUtils.CreateFrom(in from9);
			return true;
		}
		if (name == PropertyName.DailyRunLoadScreen)
		{
			NDailyRunLoadScreen from10 = DailyRunLoadScreen;
			value = VariantUtils.CreateFrom(in from10);
			return true;
		}
		if (name == PropertyName.MultiplayerSubmenu)
		{
			NMultiplayerSubmenu from11 = MultiplayerSubmenu;
			value = VariantUtils.CreateFrom(in from11);
			return true;
		}
		if (name == PropertyName.MultiplayerHostSubmenu)
		{
			NMultiplayerHostSubmenu from12 = MultiplayerHostSubmenu;
			value = VariantUtils.CreateFrom(in from12);
			return true;
		}
		if (name == PropertyName._settingsScreen)
		{
			value = VariantUtils.CreateFrom(in _settingsScreen);
			return true;
		}
		if (name == PropertyName._restSiteRoom)
		{
			value = VariantUtils.CreateFrom(in _restSiteRoom);
			return true;
		}
		if (name == PropertyName._merchantRoom)
		{
			value = VariantUtils.CreateFrom(in _merchantRoom);
			return true;
		}
		if (name == PropertyName._treasureRoomRelicCollection)
		{
			value = VariantUtils.CreateFrom(in _treasureRoomRelicCollection);
			return true;
		}
		if (name == PropertyName._characterSelectScreen)
		{
			value = VariantUtils.CreateFrom(in _characterSelectScreen);
			return true;
		}
		if (name == PropertyName._customRunScreen)
		{
			value = VariantUtils.CreateFrom(in _customRunScreen);
			return true;
		}
		if (name == PropertyName._dailyRunScreen)
		{
			value = VariantUtils.CreateFrom(in _dailyRunScreen);
			return true;
		}
		if (name == PropertyName._multiplayerLoadGameScreen)
		{
			value = VariantUtils.CreateFrom(in _multiplayerLoadGameScreen);
			return true;
		}
		if (name == PropertyName._customRunLoadScreen)
		{
			value = VariantUtils.CreateFrom(in _customRunLoadScreen);
			return true;
		}
		if (name == PropertyName._dailyRunLoadScreen)
		{
			value = VariantUtils.CreateFrom(in _dailyRunLoadScreen);
			return true;
		}
		if (name == PropertyName._multiplayerSubmenu)
		{
			value = VariantUtils.CreateFrom(in _multiplayerSubmenu);
			return true;
		}
		if (name == PropertyName._multiplayerHostSubmenu)
		{
			value = VariantUtils.CreateFrom(in _multiplayerHostSubmenu);
			return true;
		}
		return base.GetGodotClassPropertyValue(in name, out value);
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	internal static List<PropertyInfo> GetGodotPropertyList()
	{
		return new List<PropertyInfo>
		{
			new PropertyInfo(Variant.Type.Object, PropertyName._settingsScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._restSiteRoom, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._merchantRoom, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._treasureRoomRelicCollection, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._characterSelectScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._customRunScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._dailyRunScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._multiplayerLoadGameScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._customRunLoadScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._dailyRunLoadScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._multiplayerSubmenu, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName._multiplayerHostSubmenu, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.SettingsScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.RestSiteRoom, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.MerchantRoom, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.TreasureRoomRelicCollection, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.CharacterSelectScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.CustomRunScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.DailyRunScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.MultiplayerLoadGameScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.CustomRunLoadScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.DailyRunLoadScreen, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.MultiplayerSubmenu, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false),
			new PropertyInfo(Variant.Type.Object, PropertyName.MultiplayerHostSubmenu, PropertyHint.None, "", PropertyUsageFlags.ScriptVariable, exported: false)
		};
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override void SaveGodotObjectData(GodotSerializationInfo info)
	{
		base.SaveGodotObjectData(info);
		info.AddProperty(PropertyName._settingsScreen, Variant.From(in _settingsScreen));
		info.AddProperty(PropertyName._restSiteRoom, Variant.From(in _restSiteRoom));
		info.AddProperty(PropertyName._merchantRoom, Variant.From(in _merchantRoom));
		info.AddProperty(PropertyName._treasureRoomRelicCollection, Variant.From(in _treasureRoomRelicCollection));
		info.AddProperty(PropertyName._characterSelectScreen, Variant.From(in _characterSelectScreen));
		info.AddProperty(PropertyName._customRunScreen, Variant.From(in _customRunScreen));
		info.AddProperty(PropertyName._dailyRunScreen, Variant.From(in _dailyRunScreen));
		info.AddProperty(PropertyName._multiplayerLoadGameScreen, Variant.From(in _multiplayerLoadGameScreen));
		info.AddProperty(PropertyName._customRunLoadScreen, Variant.From(in _customRunLoadScreen));
		info.AddProperty(PropertyName._dailyRunLoadScreen, Variant.From(in _dailyRunLoadScreen));
		info.AddProperty(PropertyName._multiplayerSubmenu, Variant.From(in _multiplayerSubmenu));
		info.AddProperty(PropertyName._multiplayerHostSubmenu, Variant.From(in _multiplayerHostSubmenu));
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override void RestoreGodotObjectData(GodotSerializationInfo info)
	{
		base.RestoreGodotObjectData(info);
		if (info.TryGetProperty(PropertyName._settingsScreen, out var value))
		{
			_settingsScreen = value.As<NSettingsScreen>();
		}
		if (info.TryGetProperty(PropertyName._restSiteRoom, out var value2))
		{
			_restSiteRoom = value2.As<NRestSiteRoom>();
		}
		if (info.TryGetProperty(PropertyName._merchantRoom, out var value3))
		{
			_merchantRoom = value3.As<NMerchantRoom>();
		}
		if (info.TryGetProperty(PropertyName._treasureRoomRelicCollection, out var value4))
		{
			_treasureRoomRelicCollection = value4.As<NTreasureRoomRelicCollection>();
		}
		if (info.TryGetProperty(PropertyName._characterSelectScreen, out var value5))
		{
			_characterSelectScreen = value5.As<NCharacterSelectScreen>();
		}
		if (info.TryGetProperty(PropertyName._customRunScreen, out var value6))
		{
			_customRunScreen = value6.As<NCustomRunScreen>();
		}
		if (info.TryGetProperty(PropertyName._dailyRunScreen, out var value7))
		{
			_dailyRunScreen = value7.As<NDailyRunScreen>();
		}
		if (info.TryGetProperty(PropertyName._multiplayerLoadGameScreen, out var value8))
		{
			_multiplayerLoadGameScreen = value8.As<NMultiplayerLoadGameScreen>();
		}
		if (info.TryGetProperty(PropertyName._customRunLoadScreen, out var value9))
		{
			_customRunLoadScreen = value9.As<NCustomRunLoadScreen>();
		}
		if (info.TryGetProperty(PropertyName._dailyRunLoadScreen, out var value10))
		{
			_dailyRunLoadScreen = value10.As<NDailyRunLoadScreen>();
		}
		if (info.TryGetProperty(PropertyName._multiplayerSubmenu, out var value11))
		{
			_multiplayerSubmenu = value11.As<NMultiplayerSubmenu>();
		}
		if (info.TryGetProperty(PropertyName._multiplayerHostSubmenu, out var value12))
		{
			_multiplayerHostSubmenu = value12.As<NMultiplayerHostSubmenu>();
		}
	}
}
