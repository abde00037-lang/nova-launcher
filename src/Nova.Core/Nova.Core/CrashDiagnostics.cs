using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nova.Core;

public static class CrashDiagnostics
{
	private static readonly string[] StopWords = new string[21]
	{
		"forge", "core", "lib", "libs", "mc", "api", "mod", "mods", "universal", "alpha",
		"beta", "client", "common", "jar", "main", "src", "final", "release", "dev", "unofficial",
		"fixed"
	};

	private static readonly string[] SafePackagePrefixes = new string[26]
	{
		"net.minecraft.", "net.minecraftforge.", "com.mojang.", "java.", "javax.", "jdk.", "sun.", "org.lwjgl.", "org.apache.", "com.google.",
		"io.netty.", "org.objectweb.asm.", "scala.", "akka.", "org.slf4j.", "org.apache.logging.", "it.unimi.", "oshi.", "com.paulscode.", "net.java.",
		"org.jline.", "joptsimple.", "com.ibm.", "trove4j", "gnu.trove.", "com.electronwill."
	};

	public static CrashDiagnosis? AnalyzeLatest(InstanceConfig instance)
	{
		string path = Path.Combine(instance.GameDirectory, "crash-reports");
		string text = null;
		string sourceKind = "crash-report";
		if (Directory.Exists(path))
		{
			text = (from f in new DirectoryInfo(path).GetFiles("*.txt")
				orderby f.LastWriteTimeUtc descending
				select f).FirstOrDefault()?.FullName;
		}
		string text2 = Path.Combine(instance.GameDirectory, "logs", "latest.log");
		if (text == null && File.Exists(text2))
		{
			text = text2;
			sourceKind = "latest.log";
		}
		if (text == null)
		{
			return null;
		}
		CrashDiagnosis crashDiagnosis = AnalyzeFile(text, Path.Combine(instance.GameDirectory, "mods"));
		crashDiagnosis.SourceKind = sourceKind;
		return crashDiagnosis;
	}

	public static string? NewestCrashFileSince(InstanceConfig instance, DateTime sinceUtc)
	{
		DateTime since = sinceUtc.AddSeconds(-90.0);
		string path = Path.Combine(instance.GameDirectory, "crash-reports");
		if (Directory.Exists(path))
		{
			try
			{
				FileInfo fileInfo = (from f in new DirectoryInfo(path).GetFiles("*.txt")
					where f.LastWriteTimeUtc >= since
					orderby f.LastWriteTimeUtc descending
					select f).FirstOrDefault();
				if (fileInfo != null)
				{
					return fileInfo.FullName;
				}
			}
			catch
			{
			}
		}
		string text = Path.Combine(instance.GameDirectory, "logs", "latest.log");
		try
		{
			if (!File.Exists(text) || File.GetLastWriteTimeUtc(text) < since)
			{
				return null;
			}
			string text2 = ReadTail(text, 20000);
			if (text2.Contains("Minecraft Crash Report", StringComparison.OrdinalIgnoreCase) || text2.Contains("Exception in server tick loop", StringComparison.OrdinalIgnoreCase) || text2.Contains("The game crashed whilst", StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}
		catch
		{
		}
		return null;
	}

	private static string ReadTail(string path, int maxChars)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		int num = (int)Math.Min(maxChars, fileStream.Length);
		fileStream.Seek(-num, SeekOrigin.End);
		byte[] array = new byte[num];
		int count = fileStream.Read(array, 0, num);
		return Encoding.UTF8.GetString(array, 0, count);
	}

	public static CrashDiagnosis AnalyzeFile(string path, string? modsDirectory)
	{
		CrashDiagnosis crashDiagnosis = new CrashDiagnosis
		{
			SourceFile = path,
			AnalyzedUtc = DateTime.UtcNow
		};
		if (!File.Exists(path))
		{
			return crashDiagnosis;
		}
		string text;
		try
		{
			text = File.ReadAllText(path);
		}
		catch
		{
			return crashDiagnosis;
		}
		Match match = Regex.Match(text, "Description:\\s*(.+)");
		if (match.Success)
		{
			crashDiagnosis.Description = match.Groups[1].Value.Trim();
		}
		else
		{
			Match match2 = Regex.Match(text, "The game crashed whilst ([^\\r\\n]+)");
			if (match2.Success)
			{
				crashDiagnosis.Description = match2.Groups[1].Value.Trim();
			}
		}
		foreach (Match item4 in Regex.Matches(text, "Caused by:\\s*([^\\r\\n]+)"))
		{
			string item = item4.Groups[1].Value.Trim();
			if (!crashDiagnosis.Causes.Contains(item))
			{
				crashDiagnosis.Causes.Add(item);
			}
		}
		if (crashDiagnosis.Causes.Count == 0)
		{
			Match match3 = Regex.Match(text, "^((?:[a-z_][\\w$]*\\.)+[A-Z][\\w$]*(?:Exception|Error)[^\\r\\n]*)", RegexOptions.Multiline);
			if (match3.Success)
			{
				crashDiagnosis.Causes.Add(match3.Groups[1].Value.Trim());
			}
			Match match4 = Regex.Match(text, "(?:ERROR\\]|FATAL\\])\\s*([^\\r\\n]+)", RegexOptions.Multiline);
			if (match4.Success && !crashDiagnosis.Causes.Contains(match4.Groups[1].Value.Trim()))
			{
				crashDiagnosis.Causes.Add(match4.Groups[1].Value.Trim());
			}
		}
		crashDiagnosis.Findings.AddRange(from f in CrashAnalyzer.AnalyzeText(text)
			where f.Severity != "INFO"
			select f);
		Dictionary<string, ModEntry> dictionary = BuildTokens((modsDirectory != null && Directory.Exists(modsDirectory)) ? ModScanner.Scan(modsDirectory) : null, modsDirectory);
		Dictionary<string, ModEntry> dictionary2 = ((modsDirectory != null && Directory.Exists(modsDirectory)) ? BuildPackageIndex(modsDirectory) : new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase));
		Dictionary<string, (int, string)> dictionary3 = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);
		string key2;
		foreach (Match item5 in Regex.Matches(text, "\\bat\\s+([A-Za-z_$][\\w$]*(?:\\.[\\w$]+)+)\\("))
		{
			string value = item5.Groups[1].Value;
			string lower = value.ToLowerInvariant();
			if (SafePackagePrefixes.Any((string p) => lower.StartsWith(p)))
			{
				continue;
			}
			string[] array = lower.Split('.');
			ModEntry modEntry = null;
			int num = Math.Min(3, array.Length - 1);
			while (num >= 2 && (object)modEntry == null)
			{
				string key = string.Join('.', array.Take(num));
				if (dictionary2.TryGetValue(key, out var value2))
				{
					modEntry = value2;
				}
				num--;
			}
			if ((object)modEntry == null)
			{
				foreach (KeyValuePair<string, ModEntry> item6 in dictionary)
				{
					item6.Deconstruct(out key2, out var value3);
					string value4 = key2;
					ModEntry modEntry2 = value3;
					if (lower.Contains(value4))
					{
						modEntry = modEntry2;
						break;
					}
				}
			}
			if ((object)modEntry != null)
			{
				if (dictionary3.TryGetValue(modEntry.FileName, out var value5))
				{
					dictionary3[modEntry.FileName] = (value5.Item1 + 1, value5.Item2);
				}
				else
				{
					dictionary3[modEntry.FileName] = (1, value);
				}
			}
		}
		foreach (KeyValuePair<string, (int, string)> item7 in dictionary3)
		{
			item7.Deconstruct(out key2, out var value6);
			(int, string) tuple = value6;
			string file = key2;
			int item2 = tuple.Item1;
			string item3 = tuple.Item2;
			ModEntry modEntry3 = dictionary.Values.FirstOrDefault((ModEntry m) => m.FileName.Equals(file, StringComparison.OrdinalIgnoreCase));
			crashDiagnosis.Suspects.Add(new SuspectMod(file, modEntry3?.ModId, item2, item3));
		}
		string? descriptionText = crashDiagnosis.Description;
		if (descriptionText != null && descriptionText.Length > 0)
		{
			foreach (SuspectMod item8 in crashDiagnosis.Suspects.ToList())
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item8.ModFile);
				if (fileNameWithoutExtension.Length > 4 && descriptionText.Contains(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
				{
					crashDiagnosis.Suspects.Remove(item8);
					crashDiagnosis.Suspects.Insert(0, new SuspectMod(item8.ModFile, item8.ModId, Math.Max(item8.Hits + 50, 99), "named in the crash description (" + item8.Sample + ")"));
				}
			}
			ModEntry? named = dictionary.Values.FirstOrDefault((ModEntry m) => !string.IsNullOrEmpty(m.ModId) && descriptionText.Contains(m.ModId, StringComparison.OrdinalIgnoreCase));
			if ((object?)named != null)
			{
				SuspectMod? existing = crashDiagnosis.Suspects.FirstOrDefault((SuspectMod s) => s.ModFile.Equals(named.FileName, StringComparison.OrdinalIgnoreCase));
				crashDiagnosis.Suspects.RemoveAll((SuspectMod s) => s.ModFile.Equals(named.FileName, StringComparison.OrdinalIgnoreCase));
				crashDiagnosis.Suspects.Insert(0, new SuspectMod(named.FileName, named.ModId, Math.Max((existing?.Hits ?? 0) + 50, 99), existing?.Sample ?? "named in the crash description"));
			}
		}
		crashDiagnosis.Suspects.Sort((SuspectMod a, SuspectMod b) => b.Hits.CompareTo(a.Hits));
		return crashDiagnosis;
	}

	public static string Write(LauncherPaths paths, InstanceConfig instance, CrashDiagnosis diagnosis)
	{
		string text = Path.Combine(paths.Root, "reports");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, $"crash-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.md");
		File.WriteAllText(text2, diagnosis.Markdown(instance));
		return text2;
	}

	private static Dictionary<string, ModEntry> BuildPackageIndex(string modsDirectory)
	{
		Dictionary<string, ModEntry> dictionary = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (ModEntry mod in ModScanner.Scan(modsDirectory).Mods)
		{
			string text = Path.Combine(modsDirectory, mod.FileName);
			if (!File.Exists(text))
			{
				continue;
			}
			try
			{
				using ZipArchive zipArchive = ZipFile.OpenRead(text);
				foreach (ZipArchiveEntry entry in zipArchive.Entries)
				{
					if (!entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					int num = entry.FullName.LastIndexOf('/');
					if (num <= 0)
					{
						continue;
					}
					string[] array = entry.FullName.Substring(0, num).Split('/');
					if (array.Length < 2)
					{
						continue;
					}
					string prefix2 = (array[0] + "." + array[1]).ToLowerInvariant();
					if (!SafePackagePrefixes.Any((string p) => (prefix2 + ".").StartsWith(p)))
					{
						dictionary.TryAdd(prefix2, mod);
						if (array.Length >= 3)
						{
							dictionary.TryAdd(prefix2 + "." + array[2].ToLowerInvariant(), mod);
						}
					}
				}
			}
			catch
			{
			}
		}
		return dictionary;
	}

	private static Dictionary<string, ModEntry> BuildTokens(ModScanResult? mods, string? modsDirectory)
	{
		Dictionary<string, ModEntry> dictionary = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
		if (mods != null)
		{
			foreach (ModEntry mod in mods.Mods)
			{
				if (!string.IsNullOrEmpty(mod.ModId))
				{
					dictionary[mod.ModId.ToLowerInvariant()] = mod;
				}
				string[] array = Path.GetFileNameWithoutExtension(mod.FileName.Replace(".disabled", "", StringComparison.OrdinalIgnoreCase)).Split(new char[5] { '-', '_', '.', ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < array.Length; i++)
				{
					string text = array[i].ToLowerInvariant();
					if (text.Length >= 4 && !int.TryParse(text, out var _) && !Enumerable.Contains(StopWords, text))
					{
						dictionary.TryAdd(text, mod);
					}
				}
			}
		}
		return dictionary;
	}
}
