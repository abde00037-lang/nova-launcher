using System;
using System.IO;
using System.Linq;

namespace Nova.Core;

public static class ModManager
{
	public const string DisabledSuffix = ".disabled";

	public static int CountInstalled(string modsDirectory)
	{
		if (!Directory.Exists(modsDirectory))
		{
			return 0;
		}
		try
		{
			return Directory.EnumerateFiles(modsDirectory).Count((string f) => IsModFile(Path.GetFileName(f)));
		}
		catch
		{
			return 0;
		}
	}

	public static bool Delete(string modsDirectory, string fileName)
	{
		string path = Path.Combine(modsDirectory, fileName);
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			File.Delete(path);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static (int Added, int Replaced, int Skipped) InstallLocalJars(string modsDirectory, string[] filePaths)
	{
		Directory.CreateDirectory(modsDirectory);
		int added = 0;
		int replaced = 0;
		int skipped = 0;
		foreach (string src in filePaths)
		{
			if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
			{
				skipped++;
				continue;
			}
			string fileName = Path.GetFileName(src);
			if (!IsModFile(fileName))
			{
				skipped++;
				continue;
			}
			string dest = Path.Combine(modsDirectory, fileName);
			try
			{
				if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
				{
					skipped++;
					continue;
				}
				bool existed = File.Exists(dest);
				File.Copy(src, dest, overwrite: true);
				if (existed)
				{
					replaced++;
				}
				else
				{
					added++;
				}
			}
			catch
			{
				skipped++;
			}
		}
		return (Added: added, Replaced: replaced, Skipped: skipped);
	}

	public static bool SetEnabled(string modsDirectory, string fileName, bool enabled)
	{
		string text = Path.Combine(modsDirectory, fileName);
		if (!File.Exists(text))
		{
			return false;
		}
		bool flag = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
		if (enabled == !flag)
		{
			return true;
		}
		string text2;
		if (!enabled)
		{
			text2 = text + ".disabled";
		}
		else
		{
			string text3 = text;
			int length = ".disabled".Length;
			text2 = text3.Substring(0, text3.Length - length);
		}
		string text4 = text2;
		if (File.Exists(text4))
		{
			return false;
		}
		try
		{
			File.Move(text, text4);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static string DisplayName(string fileName)
	{
		string text = fileName;
		if (text.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			int length = ".disabled".Length;
			text = text2.Substring(0, text2.Length - length);
		}
		return text;
	}

	public static bool IsModFile(string fileName)
	{
		string text = DisplayName(fileName);
		if (!text.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
		{
			return text.EndsWith(".jar.txt", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
