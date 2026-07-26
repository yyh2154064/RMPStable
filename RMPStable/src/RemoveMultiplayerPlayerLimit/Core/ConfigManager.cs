using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace RemoveMultiplayerPlayerLimit.Core;

public class ConfigManager
{
	private const string ModFolderName = "RMPStable";

	private const string ConfigFileName = "config.ini";

	private const string LegacyConfigFileName = "config.json";

	private const bool DefaultMacOsTlsWorkaround = true;

	private string? _configPath;

	public static ConfigManager? Instance { get; private set; }

	public bool MacOsTlsWorkaround { get; set; } = true;


	public ConfigManager()
	{
		Instance = this;
		LoadOrCreateConfig();
	}

	public void Save()
	{
		if (string.IsNullOrEmpty(_configPath))
		{
			return;
		}
		try
		{
			using StreamWriter streamWriter = new StreamWriter(_configPath, append: false);
			streamWriter.WriteLine("[macos]");
			streamWriter.WriteLine("tls_workaround=" + MacOsTlsWorkaround.ToString().ToLowerInvariant());
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP] Failed to save config: " + ex.Message);
		}
	}

	private void LoadOrCreateConfig()
	{
		string text = ResolveModDirectory();
		Directory.CreateDirectory(text);
		_configPath = Path.Combine(text, "config.ini");
		string text2 = Path.Combine(text, "config.json");
		if (File.Exists(text2) && !File.Exists(_configPath))
		{
			MigrateLegacyJsonConfig(text2);
		}
		if (File.Exists(_configPath))
		{
			try
			{
				ParseIniConfig(_configPath);
				return;
			}
			catch (Exception ex)
			{
				Log.Warn("[RMP] Failed to parse config: " + ex.Message);
				BackupCorruptedConfig(_configPath);
			}
		}
		Save();
	}

	private void ParseIniConfig(string path)
	{
		string text = "";
		string[] array = File.ReadAllLines(path);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length == 0 || text2[0] == ';' || text2[0] == '#')
			{
				continue;
			}
			string text3;
			if (text2[0] == '[' && text2[text2.Length - 1] == ']')
			{
				text3 = text2;
				text = text3.Substring(1, text3.Length - 1 - 1).Trim();
				continue;
			}
			int num = text2.IndexOf('=');
			if (num < 0)
			{
				continue;
			}
			string text4 = text2.Substring(0, num).Trim();
			text3 = text2;
			int num2 = num + 1;
			string a = text3.Substring(num2, text3.Length - num2).Trim();
			text3 = text;
			if (text3 == "macos" && text4 == "tls_workaround")
			{
				MacOsTlsWorkaround = string.Equals(a, "true", StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	private void MigrateLegacyJsonConfig(string jsonPath)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(jsonPath));
			if (jsonDocument.RootElement.TryGetProperty("macos_tls_workaround", out var value))
			{
				MacOsTlsWorkaround = value.ValueKind == JsonValueKind.True;
			}
			Save();
			File.Delete(jsonPath);
			Log.Info("[RMP] Migrated config.json to config.ini");
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP] Failed to migrate legacy config: " + ex.Message);
		}
	}

	private static string ResolveModDirectory()
	{
		string location = Assembly.GetExecutingAssembly().Location;
		string text = (string.IsNullOrWhiteSpace(location) ? null : Path.GetDirectoryName(location));
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			return text;
		}
		string text2 = Path.Combine(AppContext.BaseDirectory, "mods", "RMPStable");
		if (Directory.Exists(text2))
		{
			return text2;
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StS2Mods", "RMPStable");
	}

	private static void BackupCorruptedConfig(string configPath)
	{
		if (File.Exists(configPath))
		{
			string text = configPath + ".bak";
			if (File.Exists(text))
			{
				text = $"{configPath}.{DateTime.Now:yyyyMMddHHmmss}.bak";
			}
			File.Move(configPath, text);
		}
	}
}
