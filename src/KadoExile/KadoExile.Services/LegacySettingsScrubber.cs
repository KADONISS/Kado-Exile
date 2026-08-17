using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KadoExile.Services;

public static class LegacySettingsScrubber
{
	public static bool ScrubPoeSessionId(string settingsPath)
	{
		if (!File.Exists(settingsPath))
		{
			return false;
		}
		JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
		JsonNode jsonNode = jsonObject?["PoeSessionId"]?["Value"];
		if (jsonNode == null || string.IsNullOrEmpty(jsonNode.GetValue<string>()))
		{
			return false;
		}
		jsonObject["PoeSessionId"]["Value"] = string.Empty;
		string text = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			File.Move(text, settingsPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
		return true;
	}
}
