using Environment = System.Environment;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Modding;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal sealed class MirrorProcess : IDisposable
{
    internal readonly MirrorWire Wire;
    internal readonly string DirectoryPath;
    internal Process Child { get; }
    internal bool Authenticated { get; set; }
    internal string LastHash = "";
    internal string LastDrawingHash = "";
    internal int LastEvents;
    internal bool LastIdle;
    internal long LastControlEpoch;
    internal bool LastControlEnabled;
    internal long Window;
    internal bool DisplayReady, NativeAttached;
    internal bool WindowVisible;
    internal bool TargetArrowVisible;
    internal string Presentation = "";
    internal long DrawFrames;
    internal string Phase = "启动中";
    internal double Fps, ProcessMs;
    internal System.Collections.Generic.List<MirrorHit> LastHits = new();
    private bool _disposed;
    internal MirrorProcess(ulong source)
    {
        string executable = OS.GetExecutablePath();
        // Godot consumes engine options before exposing GetCmdlineArgs. Never
        // assume --main-pack remains in that list, or launch without a PCK.
        string mainPack = Environment.GetEnvironmentVariable("RMP_MULTI_MAIN_PACK") ?? Path.ChangeExtension(executable, ".pck");
        mainPack = Path.GetFullPath(mainPack);
        if (!File.Exists(executable) || !File.Exists(mainPack)) throw new FileNotFoundException("Independent game executable/PCK unavailable; renderer was not started", mainPack);
        string session = Guid.NewGuid().ToString("N"), secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        string pipe = "RMP-NativeMirror-" + Environment.ProcessId + "-" + session;
        Wire = new MirrorWire(new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly), new MirrorIdentity(session, secret, source));
        string root = Path.GetFullPath(Path.Combine(OS.GetUserDataDir(), "rmp-multi-instance"));
        DirectoryPath = Path.GetFullPath(Path.Combine(root, session));
        if (!DirectoryPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid child profile path");
        Directory.CreateDirectory(DirectoryPath);
        // Copy only non-run preferences/unlocks into a newly owned profile.
        // Run saves, histories, Quick SL data and spectator auto-open settings
        // are deliberately excluded. Replica saves can never reach the source.
        string account = Path.Combine(DirectoryPath, "Roaming", "SlayTheSpire2", "default", "1");
        Directory.CreateDirectory(account);
        bool diagnosticTest = Environment.GetEnvironmentVariable("RMP_MULTI_TEST") == "1";
        var settings = new SettingsSave
        {
            SchemaVersion = SaveManager.Instance.SettingsSave.SchemaVersion, Language = SaveManager.Instance.SettingsSave.Language,
            Fullscreen = false, WindowPosition = new Vector2I(-30000,-30000), WindowSize = new Vector2I(1280,720),
            SkipIntroLogo = true, SeenEaDisclaimer = true, FpsLimit = 120, LimitFpsInBackground = false, VolumeMaster = 0,
            ModSettings = new ModSettings { PlayerAgreedToModLoading = true,
                ModList = ModManager.Mods.Select(mod => new SettingsSaveMod(mod) { IsEnabled = mod.manifest?.id == "RMPStable" || diagnosticTest && mod.manifest?.id == "MultiInstanceSmoke" }).ToList() }
        };
        File.WriteAllText(Path.Combine(account, "settings.save"), JsonSerializationUtility.ToJson(settings));
        foreach (string relative in new[] { "saves/progress.save", "saves/prefs.save" })
        {
            string original = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(relative));
            foreach (string profile in new[] { "profile1", "modded/profile1" })
            {
                string destination = Path.Combine(account, profile, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(original)) File.Copy(original, destination, overwrite: false);
            }
        }
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string key in new System.Collections.Generic.List<string>(start.Environment.Keys))
            if (key.StartsWith("RMP_SMOKE", StringComparison.Ordinal) || key.StartsWith("RMP_MULTI", StringComparison.Ordinal)) start.Environment.Remove(key);
        start.Environment["APPDATA"] = Path.Combine(DirectoryPath, "Roaming"); start.Environment["LOCALAPPDATA"] = Path.Combine(DirectoryPath, "Local");
        start.Environment["RMP_MULTI_ROLE"] = "renderer"; start.Environment["RMP_MULTI_PIPE"] = pipe;
        start.Environment["RMP_MULTI_SESSION"] = session; start.Environment["RMP_MULTI_SECRET"] = secret;
        start.Environment["RMP_MULTI_SOURCE"] = source.ToString(); start.Environment["RMP_MULTI_PARENT"] = Environment.ProcessId.ToString();
        start.Environment["RMP_MULTI_PROFILE_ROOT"] = DirectoryPath;
        if (diagnosticTest) start.Environment["RMP_MULTI_RENDERER_TEST"] = "1";
        // Original game resources stay read-only. In isolated tests the PCK is
        // outside the copied executable directory, so preserve --main-pack.
        start.ArgumentList.Add("--main-pack"); start.ArgumentList.Add(mainPack);
        string renderingMethod = DisplayServer.GetName() == "headless" ? "gl_compatibility" : RenderingServer.GetCurrentRenderingMethod();
        foreach (string value in new[] { "--force-steam", "off", "--windowed", "--position", "-30000,-30000", "--resolution", "1280x720", "--max-fps", "120", "--rendering-method", renderingMethod, "--log-file", Path.Combine(DirectoryPath, "renderer.log") }) start.ArgumentList.Add(value);
        if (DisplayServer.GetName() != "headless")
        {
            start.ArgumentList.Add("--rendering-driver"); start.ArgumentList.Add(RenderingServer.GetCurrentRenderingDriverName());
        }
        if (Environment.GetEnvironmentVariable("RMP_MULTI_HEADLESS") == "1") start.ArgumentList.Add("--headless");
        try { Child = Process.Start(start) ?? throw new IOException("Renderer process did not start"); }
        catch { Wire.Dispose(); throw; }
        // The renderer participates in interactive input. Artificially lowering
        // its scheduling priority causes avoidable latency under source load.
        try { Child.PriorityClass = ProcessPriorityClass.Normal; } catch { }
        _ = Wire.ConnectAsync();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        // Only the exact child Process object created for this session is owned.
        // Never enumerate or terminate games by executable name.
        Wire.Dispose();
        try { if (!Child.HasExited) Child.Kill(entireProcessTree: false); } catch { }
        Child.Dispose();
    }
}
