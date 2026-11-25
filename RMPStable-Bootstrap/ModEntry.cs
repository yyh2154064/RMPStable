using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Godot.Bridge;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace RMPStable.Bootstrap;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private const string Version = "0.4.9";

    public static void Initialize()
    {
        Log.Warn($"[RMP Stable] Bootstrap {Version}: reading game version.");
        string gameVersion = ReadGameVersion();
        string resourceName = GetPayloadResourceName(gameVersion);

        using Stream resource = typeof(ModEntry).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded compatibility DLL: {resourceName}");
        using MemoryStream buffer = new MemoryStream();
        resource.CopyTo(buffer);
        Log.Warn($"[RMP Stable] Bootstrap {Version}: loading {resourceName} ({buffer.Length} bytes).");
        buffer.Position = 0;
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(typeof(ModEntry).Assembly)
            ?? throw new InvalidOperationException("Could not resolve the mod assembly load context.");
        Assembly payload = context.LoadFromStream(buffer);
        Log.Warn($"[RMP Stable] Bootstrap {Version}: payload assembly loaded.");
        ScriptManagerBridge.LookupScriptsInAssembly(payload);
        Log.Warn($"[RMP Stable] Bootstrap {Version}: Godot scripts registered.");
        Log.Warn($"[RMP Stable] Bootstrap {Version}: locating payload initializer.");
        Type entry = payload.GetType("RemoveMultiplayerPlayerLimit.Core.ModEntry", throwOnError: true)!;
        System.Reflection.MethodInfo initialize = entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(entry.FullName, "Initialize");
        Log.Warn($"[RMP Stable] Bootstrap {Version}: invoking payload initializer.");
        try
        {
            initialize.Invoke(null, null);
        }
        catch (Exception error)
        {
            Log.Error($"[RMP Stable] Payload initializer failed: {error}");
            throw;
        }
        Log.Info($"[RMP Stable] Loaded v{Version} compatibility payload for {gameVersion}.");
    }

    private static string GetPayloadResourceName(string gameVersion) => gameVersion switch
    {
        "v0.107.1" => "RMPStable.Payload.v0107.dll",
        "v0.111.0" => "RMPStable.Payload.v0111.dll",
        "v0.113.0" => "RMPStable.Payload.v0113.dll",
        "v0.113.1" => "RMPStable.Payload.v01131.dll",
        "v0.114.0" => "RMPStable.Payload.v0114.dll",
        _ => throw new NotSupportedException($"RMP Stable {Version} does not support STS2 {gameVersion}.")
    };

    private static string ReadGameVersion()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "release_info.json")),
            Path.Combine(AppContext.BaseDirectory, "release_info.json"),
            Path.Combine(Environment.CurrentDirectory, "release_info.json")
        };
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("version").GetString()
                ?? throw new InvalidDataException($"Missing version in {path}");
        }
        throw new FileNotFoundException("Could not locate the game's release_info.json.");
    }
}
