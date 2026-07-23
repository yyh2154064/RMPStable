using System;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;

namespace GodotPlugins.Game;

internal static class Main
{
	[UnmanagedCallersOnly(EntryPoint = "godotsharp_game_main_init")]
	private static godot_bool InitializeFromGameProject(IntPtr godotDllHandle, IntPtr outManagedCallbacks, IntPtr unmanagedCallbacks, int unmanagedCallbacksSize)
	{
		try
		{
			DllImportResolver resolver = new GodotDllImportResolver(godotDllHandle).OnResolveDllImport;
			NativeLibrary.SetDllImportResolver(typeof(GodotObject).Assembly, resolver);
			NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
			ManagedCallbacks.Create(outManagedCallbacks);
			ScriptManagerBridge.LookupScriptsInAssembly(typeof(GodotPlugins.Game.Main).Assembly);
			return godot_bool.True;
		}
		catch (Exception value)
		{
			Console.Error.WriteLine(value);
			return GodotBoolExtensions.ToGodotBool(@bool: false);
		}
	}
}
