using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nova.Core;

public static class VersionResolver
{
	public static ResolvedVersion Resolve(string versionId, string versionsHome)
	{
		string text = Path.Combine(versionsHome, versionId, versionId + ".json");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("Version JSON not found: " + text);
		}
		VersionJson versionJson = Json.Read<VersionJson>(text) ?? throw new InvalidDataException("Broken version JSON: " + text);
		List<VersionJson> chain = new List<VersionJson> { versionJson };
		HashSet<string> visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { versionJson.Id ?? versionId };
		string? nextParent = versionJson.InheritsFrom;
		while (!string.IsNullOrEmpty(nextParent) && visitedIds.Add(nextParent))
		{
			string parentPath = Path.Combine(versionsHome, nextParent, nextParent + ".json");
			if (!File.Exists(parentPath))
			{
				break;
			}
			VersionJson? pj = Json.Read<VersionJson>(parentPath);
			if (pj == null)
			{
				break;
			}
			chain.Add(pj);
			nextParent = pj.InheritsFrom;
		}
		VersionJson? versionJson2 = ((chain.Count > 1) ? chain[chain.Count - 1] : null);
		ResolvedVersion resolvedVersion = new ResolvedVersion
		{
			Id = (versionJson.Id ?? versionId),
			ParentId = versionJson.InheritsFrom,
			MainClass = (FirstNonEmpty(chain.Select((VersionJson c) => c.MainClass).ToArray()) ?? "net.minecraft.client.main.Main"),
			AssetIndexId = (FirstNonEmpty(chain.SelectMany((VersionJson c) => new string?[2] { c.Assets, c.AssetIndex?.Id }).ToArray()) ?? ""),
			AssetIndex = chain.Select((VersionJson c) => c.AssetIndex).FirstOrDefault((AssetIndexRef? a) => a != null),
			ClientJarVersionId = (versionJson2?.Id ?? versionJson.InheritsFrom ?? versionJson.Id ?? versionId),
			ClientDownload = chain.Select((VersionJson c) => c.Downloads?.Client).FirstOrDefault((DownloadEntry? d) => d != null)
		};
		resolvedVersion.RequiredJavaMajor = chain.Select((VersionJson c) => c.JavaVersion?.MajorVersion).FirstOrDefault((int? m) => m.HasValue) ?? GuessJavaMajor(resolvedVersion.ClientJarVersionId);
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (LibraryEntry item2 in AllLibrariesChain(chain))
		{
			if (!RuleEvaluator.IsAllowed(item2.Rules))
			{
				continue;
			}
			Coordinates coordinates = Coordinates.Parse(item2.Name);
			string item = coordinates.Group + ":" + coordinates.Artifact + ":" + coordinates.Classifier;
			if (!hashSet.Add(item))
			{
				continue;
			}
			string? classifier = coordinates.Classifier;
			if (classifier == null || classifier.Length <= 0 || !classifier.StartsWith("natives-", StringComparison.OrdinalIgnoreCase) || IsPlatformNative(classifier))
			{
				DownloadEntry? downloadEntry = item2.Downloads?.Artifact;
				string relativePath = downloadEntry?.Path ?? coordinates.ToPath(null);
				string? resolvedUrl = downloadEntry?.Url;
				if (string.IsNullOrWhiteSpace(resolvedUrl))
				{
					resolvedUrl = coordinates.ToUrl(null, item2.Url);
				}
				ResolvedFile resolvedFile = new ResolvedFile
				{
					Name = item2.Name,
					RelativePath = relativePath,
					Url = resolvedUrl,
					Sha1 = downloadEntry?.Sha1,
					Size = (downloadEntry?.Size ?? 0)
				};
				if (resolvedFile.Url == null)
				{
					resolvedVersion.Warnings.Add("No download URL for " + item2.Name);
				}
				resolvedVersion.Files.Add(resolvedFile);
				if (classifier != null && classifier.Length > 0 && classifier.StartsWith("natives-", StringComparison.OrdinalIgnoreCase))
				{
					resolvedVersion.NativeFiles.Add(new ResolvedFile
					{
						Name = item2.Name,
						RelativePath = relativePath,
						Url = resolvedFile.Url,
						Sha1 = resolvedFile.Sha1,
						Size = resolvedFile.Size,
						NativeClassifier = classifier
					});
				}
				if (item2.Natives != null && item2.Natives.TryGetValue(RuleEvaluator.OsName, out string? value) && value != null)
				{
					value = value.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
					DownloadEntry? downloadEntry2 = ((item2.Downloads?.Classifiers != null && item2.Downloads.Classifiers.TryGetValue(value, out DownloadEntry? value2)) ? value2 : null);
					resolvedVersion.NativeFiles.Add(new ResolvedFile
					{
						Name = item2.Name + ":" + value,
						RelativePath = (downloadEntry2?.Path ?? coordinates.ToPath(value)),
						Url = (downloadEntry2?.Url ?? coordinates.ToUrl(value, item2.Url)),
						Sha1 = downloadEntry2?.Sha1,
						Size = (downloadEntry2?.Size ?? 0),
						NativeClassifier = value
					});
				}
			}
		}
		foreach (VersionJson ancestor in Enumerable.Reverse(chain))
		{
			if (!string.IsNullOrWhiteSpace(ancestor.MinecraftArguments) && resolvedVersion.GameArgs.Count == 0)
			{
				resolvedVersion.GameArgs.AddRange(SplitArguments(ancestor.MinecraftArguments));
			}
		}
		if (!string.IsNullOrWhiteSpace(versionJson.MinecraftArguments))
		{
			resolvedVersion.GameArgs.Clear();
			resolvedVersion.GameArgs.AddRange(SplitArguments(versionJson.MinecraftArguments));
		}
		foreach (VersionJson ancestor2 in Enumerable.Reverse(chain))
		{
			AppendModernArguments(resolvedVersion, ancestor2.Arguments);
			if (SupportsQuickPlay(ancestor2.Arguments)) resolvedVersion.SupportsQuickPlaySingleplayer = true;
		}
		return resolvedVersion;
	}

	public static bool IsPlatformNative(string classifier)
	{
		bool flag = RuntimeInformation.OSArchitecture == Architecture.Arm64;
		if (OperatingSystem.IsWindows())
		{
			if (!classifier.StartsWith("natives-windows", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return classifier.Contains("arm64", StringComparison.OrdinalIgnoreCase) == flag;
		}
		if (OperatingSystem.IsMacOS())
		{
			if (classifier.StartsWith("natives-macos", StringComparison.OrdinalIgnoreCase))
			{
				return classifier.Contains("arm64", StringComparison.OrdinalIgnoreCase) == flag;
			}
			return false;
		}
		if (classifier.StartsWith("natives-linux", StringComparison.OrdinalIgnoreCase))
		{
			return classifier.Contains("arm64", StringComparison.OrdinalIgnoreCase) == flag;
		}
		return false;
	}

	private static IEnumerable<LibraryEntry> AllLibrariesChain(List<VersionJson> chain)
	{
		foreach (VersionJson node in chain)
		{
			if (node.Libraries == null)
			{
				continue;
			}
			foreach (LibraryEntry library in node.Libraries)
			{
				yield return library;
			}
		}
	}

	private static bool SupportsQuickPlay(ArgumentsBlock? block)
	{
		foreach (JsonElement element in block?.Game ?? new List<JsonElement>())
		{
			if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array ||
				!element.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array) continue;
			bool feature = rules.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.Object && r.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String && action.GetString() == "allow" &&
				r.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("is_quick_play_singleplayer", out var v) && v.ValueKind == JsonValueKind.True);
			var args = values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).ToArray();
			if (feature && args.Length == 2 && args[0] == "--quickPlaySingleplayer" && args[1] == "${quickPlaySingleplayer}") return true;
		}
		return false;
	}

	private static void AppendModernArguments(ResolvedVersion resolved, ArgumentsBlock? block)
	{
		if (block == null)
		{
			return;
		}
		foreach (JsonElement item in block.Game ?? new List<JsonElement>())
		{
			foreach (string arg in ReadArguments(item))
			{
				resolved.GameArgs.Add(arg);
			}
		}
		foreach (JsonElement item2 in block.Jvm ?? new List<JsonElement>())
		{
			foreach (string arg2 in ReadArguments(item2))
			{
				resolved.JvmArgs.Add(arg2);
			}
		}
	}

	private static IEnumerable<string> ReadArguments(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.String)
		{
			string? s = element.GetString();
			if (s != null)
			{
				yield return s;
			}
			yield break;
		}
		if (element.ValueKind != JsonValueKind.Object)
		{
			yield break;
		}
		if (element.TryGetProperty("rules", out var rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
		{
			List<Rule> rules = JsonSerializer.Deserialize<List<Rule>>(rulesEl.GetRawText(), Json.Options) ?? new List<Rule>();
			if (!RuleEvaluator.IsAllowed(rules))
			{
				yield break;
			}
		}
		if (!element.TryGetProperty("value", out var valEl))
		{
			yield break;
		}
		if (valEl.ValueKind == JsonValueKind.String)
		{
			string? s2 = valEl.GetString();
			if (s2 != null)
			{
				yield return s2;
			}
		}
		else if (valEl.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in valEl.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					string? s3 = item.GetString();
					if (s3 != null)
					{
						yield return s3;
					}
				}
			}
		}
	}

	public static List<string> SplitArguments(string raw)
	{
		List<string> list = new List<string>();
		string[] array = raw.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string item in array)
		{
			list.Add(item);
		}
		return list;
	}

	public static int GuessJavaMajor(string minecraftVersion)
	{
		string[] array = minecraftVersion.Split('.', '-');
		if (array.Length >= 1 && int.TryParse(array[0], out var majorNum) && majorNum >= 26)
		{
			return 25;
		}
		if (array.Length >= 1 && int.TryParse(array[0], out majorNum) && majorNum >= 25)
		{
			return 21;
		}
		if (array.Length >= 2 && array[0] == "1" && int.TryParse(array[1], out var minor))
		{
			int patch = ((array.Length >= 3 && int.TryParse(array[2], out var p)) ? p : 0);
			if (minor <= 16)
			{
				return 8;
			}
			if (minor < 20 || (minor == 20 && patch <= 4))
			{
				return 17;
			}
			return 21;
		}
		return 8;
	}

	private static string? FirstNonEmpty(params string?[] values)
	{
		return values.FirstOrDefault((string? v) => !string.IsNullOrWhiteSpace(v));
	}
}
