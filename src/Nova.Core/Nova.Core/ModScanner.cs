using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nova.Core;

public static class ModScanner
{
	public static ModScanResult Scan(string modsDirectory, CancellationToken cancellationToken = default)
	{
		ModScanResult modScanResult = new ModScanResult();
		if (!Directory.Exists(modsDirectory))
		{
			return modScanResult;
		}
		foreach (string item in Directory.GetFiles(modsDirectory).OrderBy((string p) => p, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string fileName = Path.GetFileName(item);
			string extension = Path.GetExtension(fileName);
			if (string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
			{
				bool flag = string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase);
				if (flag)
				{
					modScanResult.EnabledCount++;
				}
				else
				{
					modScanResult.DisabledCount++;
				}
				FileInfo fileInfo = new FileInfo(item);
				modScanResult.TotalBytes += fileInfo.Length;
				ModEntry modEntry = ReadMod(item, fileName, flag, fileInfo.Length, modScanResult);
				if ((object)modEntry != null)
				{
					modScanResult.Mods.Add(modEntry);
				}
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		foreach (IGrouping<string, ModEntry> item2 in modScanResult.Mods.Where((ModEntry m) => !string.IsNullOrEmpty(m.ModId)).GroupBy((ModEntry m) => m.ModId, StringComparer.OrdinalIgnoreCase))
		{
			if (item2.Count() > 1)
			{
				modScanResult.DuplicateIds[item2.Key] = item2.Select((ModEntry g) => g.FileName).ToList();
			}
		}
		HashSet<string> hashSet = new HashSet<string>(from m in modScanResult.Mods
			where m.Enabled && !string.IsNullOrEmpty(m.ModId)
			select m.ModId, StringComparer.OrdinalIgnoreCase);
		foreach (ModEntry item3 in modScanResult.Mods.Where((ModEntry m) => m.Enabled))
		{
			foreach (string item4 in item3.Required)
			{
				string text = item4.Split('@', '(', '[', ']')[0].Trim();
				if (text.Length != 0 && !text.Equals("forge", StringComparison.OrdinalIgnoreCase) && !text.Equals("minecraft", StringComparison.OrdinalIgnoreCase) && !text.Equals("fml", StringComparison.OrdinalIgnoreCase) && !hashSet.Contains(text))
				{
					modScanResult.MissingDependencies.Add(item3.FileName + " needs '" + text + "'");
				}
			}
		}
		return modScanResult;
	}

	private static ModEntry? ReadMod(string path, string fileName, bool enabled, long size, ModScanResult result)
	{
		try
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(path);
			ZipArchiveEntry? zipArchiveEntry = zipArchive.Entries.FirstOrDefault((ZipArchiveEntry e) => string.Equals(e.FullName, "mcmod.info", StringComparison.OrdinalIgnoreCase));
			if (zipArchiveEntry == null)
			{
				ZipArchiveEntry? tomlEntry = zipArchive.Entries.FirstOrDefault((ZipArchiveEntry e) => string.Equals(e.FullName, "META-INF/mods.toml", StringComparison.OrdinalIgnoreCase) || string.Equals(e.FullName, "META-INF/neoforge.mods.toml", StringComparison.OrdinalIgnoreCase));
				if (tomlEntry != null)
				{
					using StreamReader tomlReader = new StreamReader(tomlEntry.Open());
					return ParseModsToml(fileName, tomlReader.ReadToEnd(), enabled, size);
				}
				ZipArchiveEntry? fabricEntry = zipArchive.Entries.FirstOrDefault((ZipArchiveEntry e) => string.Equals(e.FullName, "fabric.mod.json", StringComparison.OrdinalIgnoreCase));
				if (fabricEntry != null)
				{
					using StreamReader fabricReader = new StreamReader(fabricEntry.Open());
					return ParseFabricJson(fileName, fabricReader.ReadToEnd(), enabled, size);
				}
				return new ModEntry(fileName, null, null, null, Array.Empty<string>(), enabled, size);
			}
			using StreamReader streamReader = new StreamReader(zipArchiveEntry.Open());
			using JsonDocument jsonDocument = ParseLegacyJson(Canonicalize(streamReader.ReadToEnd()));
			JsonElement element = jsonDocument.RootElement;
			if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("modList", out var value))
			{
				element = value;
			}
			if (element.ValueKind == JsonValueKind.Array)
			{
				element = element[0];
			}
			string modId = GetString(element, "modid") ?? GetString(element, "modId");
			string name = GetString(element, "name");
			string version = GetString(element, "version");
			List<string> list = new List<string>();
			if (element.TryGetProperty("requiredMods", out var value2) && value2.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in value2.EnumerateArray())
				{
					string text = item.GetString();
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add(text);
					}
				}
			}
			return new ModEntry(fileName, modId, name, version, list, enabled, size);
		}
		catch (Exception)
		{
			result.BrokenJars.Add(fileName);
			return new ModEntry(fileName, null, null, null, Array.Empty<string>(), enabled, size);
		}
	}

	private static string Canonicalize(string text)
	{
		StringBuilder stringBuilder = new StringBuilder(text.Length + 64);
		bool flag = false;
		bool flag2 = false;
		foreach (char c in text)
		{
			if (flag)
			{
				if (flag2)
				{
					flag2 = false;
					stringBuilder.Append(c);
					continue;
				}
				switch (c)
				{
				case '\\':
					stringBuilder.Append(c);
					flag2 = true;
					continue;
				case '"':
					flag = false;
					stringBuilder.Append(c);
					continue;
				case '\n':
					stringBuilder.Append("\\n");
					continue;
				case '\r':
					stringBuilder.Append("\\r");
					continue;
				case '\t':
					stringBuilder.Append("\\t");
					continue;
				}
				if (c < ' ')
				{
					StringBuilder stringBuilder2 = stringBuilder.Append("\\u");
					int num = c;
					stringBuilder2.Append(num.ToString("x4"));
				}
				else
				{
					stringBuilder.Append(c);
				}
			}
			else
			{
				if (c == '"')
				{
					flag = true;
				}
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static JsonDocument ParseLegacyJson(string text)
	{
		try
		{
			return JsonDocument.Parse(text, new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip
			});
		}
		catch (JsonException)
		{
			Match match = Regex.Match(text, "\"modid\"\\s*:\\s*\"([^\"\\r\\n]+)\"");
			Match match2 = Regex.Match(text, "\"name\"\\s*:\\s*\"([^\"\\r\\n]+)\"");
			Match match3 = Regex.Match(text, "\"version\"\\s*:\\s*\"([^\"\\r\\n]+)\"");
			return JsonDocument.Parse("{\"modid\":\"" + (match.Success ? match.Groups[1].Value : "") + "\",\"name\":\"" + (match2.Success ? match2.Groups[1].Value : "") + "\",\"version\":\"" + (match3.Success ? match3.Groups[1].Value : "") + "\"}");
		}
	}

	private static ModEntry ParseModsToml(string fileName, string toml, bool enabled, long size)
	{
		int modsSection = toml.IndexOf("[[mods]]", StringComparison.OrdinalIgnoreCase);
		string head = ((modsSection >= 0) ? toml.Substring(modsSection) : toml);
		int nextSection = head.IndexOf("[[dependencies", StringComparison.OrdinalIgnoreCase);
		string modBlock = ((nextSection > 0) ? head.Substring(0, nextSection) : head);
		string? modId = MatchTomlString(modBlock, "modId");
		string? name = MatchTomlString(modBlock, "displayName");
		string? version = MatchTomlString(modBlock, "version");
		if (version == "${file.jarVersion}")
		{
			version = null;
		}
		List<string> reqs = new List<string>();
		foreach (Match m in Regex.Matches(toml, "\\[\\[dependencies\\.[^\\]]+\\]\\]([\\s\\S]*?)(?=\\[\\[|$)"))
		{
			string depBlock = m.Groups[1].Value;
			string? depId = MatchTomlString(depBlock, "modId");
			bool mandatory = Regex.IsMatch(depBlock, "mandatory\\s*=\\s*true", RegexOptions.IgnoreCase) || string.Equals(MatchTomlString(depBlock, "type"), "required", StringComparison.OrdinalIgnoreCase);
			if (mandatory && !string.IsNullOrWhiteSpace(depId))
			{
				reqs.Add(depId);
			}
		}
		return new ModEntry(fileName, modId, name, version, reqs, enabled, size);
	}

	private static string? MatchTomlString(string block, string key)
	{
		Match m = Regex.Match(block, "^\\s*" + Regex.Escape(key) + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.Multiline);
		if (m.Success)
		{
			return m.Groups[1].Value.Trim();
		}
		Match m2 = Regex.Match(block, "^\\s*" + Regex.Escape(key) + "\\s*=\\s*'([^']*)'", RegexOptions.Multiline);
		return m2.Success ? m2.Groups[1].Value.Trim() : null;
	}

	private static ModEntry ParseFabricJson(string fileName, string raw, bool enabled, long size)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(raw, new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip
			});
			JsonElement root = doc.RootElement;
			string? id = GetString(root, "id");
			string? name = GetString(root, "name");
			string? version = GetString(root, "version");
			List<string> reqs = new List<string>();
			if (root.TryGetProperty("depends", out var deps) && deps.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty prop in deps.EnumerateObject())
				{
					if (!prop.Name.Equals("fabricloader", StringComparison.OrdinalIgnoreCase) && !prop.Name.Equals("java", StringComparison.OrdinalIgnoreCase))
					{
						reqs.Add(prop.Name);
					}
				}
			}
			return new ModEntry(fileName, id, name, version, reqs, enabled, size);
		}
		catch
		{
			return new ModEntry(fileName, null, null, null, Array.Empty<string>(), enabled, size);
		}
	}

	private static string? GetString(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}
}
