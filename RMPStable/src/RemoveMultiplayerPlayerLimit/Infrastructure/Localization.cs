using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;

namespace RemoveMultiplayerPlayerLimit.Infrastructure;

public static class Localization
{
	private static readonly Dictionary<string, Dictionary<string, string>> Cache = new Dictionary<string, Dictionary<string, string>>();

	public static string Get(string key, string fallback)
	{
		string languageCode = GetLanguageCode();
		if (TryGet(languageCode, key, out string value))
		{
			return value;
		}
		if (languageCode != "en_us" && TryGet("en_us", key, out value))
		{
			return value;
		}
		return fallback;
	}

	private static string GetLanguageCode()
	{
		if (!string.Equals(LocManager.Instance?.Language ?? "eng", "zhs", StringComparison.OrdinalIgnoreCase))
		{
			return "en_us";
		}
		return "zh_cn";
	}

	private static bool TryGet(string langCode, string key, out string value)
	{
		if (GetTable(langCode).TryGetValue(key, out string value2) && value2 != null)
		{
			value = value2;
			return true;
		}
		value = string.Empty;
		return false;
	}

	private static Dictionary<string, string> GetTable(string langCode)
	{
		if (Cache.TryGetValue(langCode, out Dictionary<string, string> value))
		{
			return value;
		}
		string text = "res://RMPStable/localization/" + langCode + ".json";
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		try
		{
			using FileAccess fileAccess = FileAccess.Open(text, FileAccess.ModeFlags.Read);
			if (fileAccess != null)
			{
				Dictionary<string, string> dictionary2 = JsonSerializer.Deserialize<Dictionary<string, string>>(fileAccess.GetAsText());
				if (dictionary2 != null)
				{
					dictionary = dictionary2;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("[RMP] Failed to load localization: " + text + ". " + ex.Message);
		}
		Cache[langCode] = dictionary;
		return dictionary;
	}
}
