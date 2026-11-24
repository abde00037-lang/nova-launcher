using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class VersionLibrary
{
	private sealed class ForgeVersionComparer : IComparer<string>
	{
		public int Compare(string? x, string? y)
		{
			return VersionLibrary.Compare(x ?? "", y ?? "");
		}
	}

	public const string Oldest = "1.8";

	public const string Newest = "1.21.4";

	private const string PromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";

	private const string MavenMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

	private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.All,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(60.0)
	};

	public static async Task<List<MinecraftVersion>> GetMinecraftVersionsAsync(LauncherPaths paths, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		Task<VersionManifest> manifestTask = MojangMeta.GetManifestAsync(ct);
		Task<Dictionary<string, (string? Recommended, string? Latest)>> forgeTask = GetForgePromotionsAsync(ct);
		Task<HashSet<string>> fabricTask = GetFabricSupportedAsync(ct);
		Task<Dictionary<string, string>> neoTask = NeoForgeInstaller.GetLatestByMinecraftAsync(ct);
		await Task.WhenAll(manifestTask, forgeTask, fabricTask, neoTask).ConfigureAwait(continueOnCapturedContext: false);
		VersionManifest manifest = manifestTask.Result;
		Dictionary<string, (string? Recommended, string? Latest)> dictionary = forgeTask.Result;
		HashSet<string> fabricSet = fabricTask.Result;
		Dictionary<string, string> neoMap = neoTask.Result;
		List<MinecraftVersion> list = new List<MinecraftVersion>();
		foreach (ManifestVersion item in manifest.Versions ?? new List<ManifestVersion>())
		{
			if (string.Equals(item.Type, "release", StringComparison.OrdinalIgnoreCase) && InRange(item.Id))
			{
				dictionary.TryGetValue(item.Id, out var value);
				neoMap.TryGetValue(item.Id, out string? neoVer);
				bool hasFabric = (fabricSet.Count > 0) ? fabricSet.Contains(item.Id) : (Compare(item.Id, "1.14") >= 0);
				MinecraftVersion minecraftVersion = new MinecraftVersion
				{
					Id = item.Id,
					ReleaseUtc = ParseRelease(item),
					Installed = File.Exists(paths.VersionJson(item.Id)),
					ForgeRecommended = value.Recommended,
					ForgeLatest = value.Latest,
					HasFabric = hasFabric,
					NeoForgeLatest = neoVer
				};
				list.Add(minecraftVersion);
			}
		}
		log?.Report($"{list.Count} official releases from {Oldest} upward");
		return list.OrderByDescending((MinecraftVersion v) => v.ReleaseUtc).ToList();
	}

	private static async Task<HashSet<string>> GetFabricSupportedAsync(CancellationToken ct)
	{
		HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync("https://meta.fabricmc.net/v2/versions/game", ct).ConfigureAwait(continueOnCapturedContext: false));
			if (doc.RootElement.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement el in doc.RootElement.EnumerateArray())
				{
					if (el.TryGetProperty("version", out var vEl) && vEl.ValueKind == JsonValueKind.String)
					{
						string? v = vEl.GetString();
						if (!string.IsNullOrWhiteSpace(v))
						{
							set.Add(v);
						}
					}
				}
			}
		}
		catch
		{
		}
		return set;
	}

	public static async Task<List<string>> GetForgeBuildsAsync(string mcVersion, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			MatchCollection matchCollection = Regex.Matches(await Http.GetStringAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", ct).ConfigureAwait(continueOnCapturedContext: false), "<version>([^<]+)</version>");
			string text = mcVersion + "-";
			List<string> list = new List<string>();
			foreach (Match item in matchCollection)
			{
				string text2 = item.Groups[1].Value.Trim();
				if (text2.StartsWith(text, StringComparison.OrdinalIgnoreCase))
				{
					string text3 = text2;
					int length = text.Length;
					string text4 = text3.Substring(length, text3.Length - length);
					if (text4.Length > 0 && text4.All((char c) => char.IsDigit(c) || c == '.'))
					{
						list.Add(text4);
					}
				}
			}
			return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending((string v) => v, new ForgeVersionComparer()).ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	private static async Task<Dictionary<string, (string? Recommended, string? Latest)>> GetForgePromotionsAsync(CancellationToken ct)
	{
		Dictionary<string, (string? Recommended, string? Latest)> map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(await Http.GetStringAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct).ConfigureAwait(continueOnCapturedContext: false));
			if (!jsonDocument.RootElement.TryGetProperty("promos", out var value))
			{
				return map;
			}
			foreach (JsonProperty item in value.EnumerateObject())
			{
				string name = item.Name;
				int num = name.LastIndexOf('-');
				if (num > 0)
				{
					string key = name.Substring(0, num);
					string text = name;
					int num2 = num + 1;
					string text2 = text.Substring(num2, text.Length - num2);
					string text3 = item.Value.GetString() ?? "";
					map.TryGetValue(key, out (string, string) value2);
					map[key] = (text2.Equals("recommended", StringComparison.OrdinalIgnoreCase) ? (text3, value2.Item2) : (value2.Item1, text3));
				}
			}
		}
		catch
		{
		}
		return map;
	}

	private static DateTime ParseRelease(ManifestVersion entry)
	{
		if (entry.ReleaseTime != default(DateTime))
		{
			return entry.ReleaseTime.ToUniversalTime();
		}
		int num = VersionOrder(entry.Id);
		return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(num);
	}

	public static bool InRange(string id)
	{
		if (!IsReleaseLike(id))
		{
			return false;
		}
		if (Compare(id, "1.8") >= 0)
		{
			return Compare(id, "1.21.4") <= 0;
		}
		return false;
	}

	private static bool IsReleaseLike(string id)
	{
		return Regex.IsMatch(id, "^\\d+(\\.\\d+){1,2}$");
	}

	public static bool Newer(string a, string b)
	{
		return Compare(a, b) > 0;
	}

	public static int Compare(string left, string right)
	{
		int[] array = (from p in left.Split('.')
			select int.TryParse(p, out var result) ? result : 0).ToArray();
		int[] array2 = (from p in right.Split('.')
			select int.TryParse(p, out var result) ? result : 0).ToArray();
		int num = Math.Max(array.Length, array2.Length);
		for (int num2 = 0; num2 < num; num2++)
		{
			int num3 = ((num2 < array.Length) ? array[num2] : 0);
			int num4 = ((num2 < array2.Length) ? array2[num2] : 0);
			if (num3 != num4)
			{
				return num3.CompareTo(num4);
			}
		}
		return 0;
	}

	private static int VersionOrder(string id)
	{
		string[] array = id.Split('.');
		int num = ((array.Length == 0 || !int.TryParse(array[0], out var result)) ? 1 : result);
		int num2 = ((array.Length > 1 && int.TryParse(array[1], out var result2)) ? result2 : 0);
		int num3 = ((array.Length > 2 && int.TryParse(array[2], out var result3)) ? result3 : 0);
		return (num * 10000 + num2 * 100 + num3) % 9000;
	}

	public static bool IsForgeInstalled(string mcVersion, string forgeVersion, LauncherPaths paths)
	{
		return File.Exists(paths.VersionJson(mcVersion + "-forge-" + forgeVersion));
	}
}
