using System;

namespace Nova.Core;

/// <summary>Filtering and formatting of game log lines, independent of the WPF view model.</summary>
public static class GameLogFormatter
{
	public static bool ShouldShow(string line)
	{
		if (line.Contains("Missing sound for event", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (line.Contains("Potentially Dangerous alternative prefix", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (line.StartsWith("\tat ") || line.StartsWith("Caused by"))
		{
			return false;
		}
		if (line.Contains("/ERROR]") || line.Contains("/FATAL]") || line.Contains("/WARN]"))
		{
			return true;
		}
		if (!line.Contains("Mod Loader has", StringComparison.OrdinalIgnoreCase) && !line.Contains("OpenAL initialized", StringComparison.OrdinalIgnoreCase) && !line.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase) && !line.Contains("Setting user", StringComparison.OrdinalIgnoreCase) && !line.Contains("Stopping!", StringComparison.OrdinalIgnoreCase))
		{
			return line.Contains("Forge Mod Loader version", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static string LevelOf(string line)
	{
		if (line.Contains("/FATAL]"))
		{
			return "FATAL";
		}
		if (line.Contains("/ERROR]"))
		{
			return "ERROR";
		}
		if (line.Contains("/WARN]"))
		{
			return "WARN";
		}
		return "INFO";
	}

	public static string CleanLine(string line)
	{
		string text = line.Trim();
		int num = text.IndexOf("]: ", StringComparison.Ordinal);
		if (num > 0 && num < 90)
		{
			string text2 = text;
			int num2 = num + 3;
			text = text2.Substring(num2, text2.Length - num2);
		}
		if (text.Length <= 180)
		{
			return text;
		}
		return text.Substring(0, 180) + "…";
	}

}
