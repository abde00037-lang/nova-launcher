using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class ModMetaStore
{
	private sealed record CurseWidget(string? Icon);

	private const string Modrinth = "https://api.modrinth.com/v2";

	private const string Curse = "https://api.curseforge.com/v1";

	private const int CurseMcGameId = 432;

	private const int CurseModsClassId = 6;

	private readonly string _cacheFile;

	private readonly string _iconDir;

	private readonly HttpClient _http;

	private readonly string? _curseKey;

	private readonly Dictionary<string, ModMeta> _meta = new Dictionary<string, ModMeta>(StringComparer.OrdinalIgnoreCase);

	private ModMetaStore(string root, string? curseKey)
	{
		_cacheFile = Path.Combine(root, "cache", "mod-meta.json");
		_iconDir = Path.Combine(root, "cache", "mod-icons");
		_curseKey = (string.IsNullOrWhiteSpace(curseKey) ? null : curseKey.Trim());
		_http = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30.0)
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/0.2 (minecraft modpack launcher)");
		Load();
	}

	public static ModMetaStore Load(LauncherPaths paths, string? curseKey)
	{
		try
		{
			return new ModMetaStore(paths.Root, curseKey);
		}
		catch
		{
			return new ModMetaStore(Path.GetTempPath(), curseKey);
		}
	}

	public ModMeta? Get(string fileName)
	{
		if (!_meta.TryGetValue(Key(fileName), out ModMeta value))
		{
			return null;
		}
		if (value.IconFile == null)
		{
			return value;
		}
		if (File.Exists(IconPath(value)))
		{
			return value;
		}
		value.IconFile = null;
		return value;
	}

	public string? IconPath(ModMeta meta)
	{
		if (!string.IsNullOrWhiteSpace(meta.IconFile))
		{
			return Path.Combine(_iconDir, meta.IconFile);
		}
		return null;
	}

	private static string Key(string fileName)
	{
		return ModManager.DisplayName(fileName).ToLowerInvariant();
	}

	private void Load()
	{
		try
		{
			if (!File.Exists(_cacheFile))
			{
				return;
			}
			Dictionary<string, ModMeta> dictionary = JsonSerializer.Deserialize<Dictionary<string, ModMeta>>(File.ReadAllText(_cacheFile));
			if (dictionary == null)
			{
				return;
			}
			foreach (KeyValuePair<string, ModMeta> item in dictionary)
			{
				_meta[item.Key] = item.Value;
			}
		}
		catch
		{
		}
	}

	private void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile));
			File.WriteAllText(_cacheFile, JsonSerializer.Serialize(_meta, new JsonSerializerOptions
			{
				WriteIndented = false
			}));
		}
		catch
		{
		}
	}

	public async Task<int> EnrichAsync(IReadOnlyList<ModEntry> mods, string modsDirectory, string mcVersion, IProgress<string>? progress = null, Action? refresh = null, CancellationToken ct = default(CancellationToken))
	{
		List<ModEntry> pending = mods.Where((ModEntry m) => !_meta.ContainsKey(Key(m.FileName))).ToList();
		if (pending.Count == 0)
		{
			return 0;
		}
		int resolved = 0;
		progress?.Report($"looking up {pending.Count} mods…");
		Dictionary<string, ModEntry> byHash = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (ModEntry mod in pending)
		{
			ct.ThrowIfCancellationRequested();
			string text = await TryHashAsync(Path.Combine(modsDirectory, mod.FileName), ct).ConfigureAwait(continueOnCapturedContext: false);
			if (text != null)
			{
				byHash[text] = mod;
			}
		}
		if (byHash.Count > 0)
		{
			try
			{
				int num = resolved;
				resolved = num + await EnrichFromModrinthAsync(byHash, ct).ConfigureAwait(continueOnCapturedContext: false);
				progress?.Report($"matched {resolved} mods on Modrinth");
			}
			catch
			{
			}
		}
		List<ModEntry> remaining = pending.Where((ModEntry m) => !_meta.ContainsKey(Key(m.FileName))).ToList();
		if (remaining.Count > 0 && _curseKey != null)
		{
			int num = 0;
			foreach (ModEntry item in remaining)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					if (await EnrichFromCurseAsync(item, mcVersion, ct).ConfigureAwait(continueOnCapturedContext: false))
					{
						resolved++;
					}
				}
				catch
				{
				}
				num++;
				if (num % 5 == 0)
				{
					progress?.Report($"curseforge {num}/{remaining.Count}…");
				}
				if (num % 12 == 0)
				{
					refresh?.Invoke();
				}
			}
		}
		List<ModEntry> leftovers = pending.Where((ModEntry m) => !_meta.ContainsKey(Key(m.FileName))).ToList();
		if (leftovers.Count > 0)
		{
			int num = 0;
			foreach (ModEntry item2 in leftovers)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					if (await EnrichFromModrinthSearchAsync(item2, mcVersion, ct).ConfigureAwait(continueOnCapturedContext: false))
					{
						resolved++;
					}
				}
				catch
				{
				}
				num++;
				if (num % 10 == 0)
				{
					progress?.Report($"modrinth search {num}/{leftovers.Count}…");
					refresh?.Invoke();
				}
			}
		}
		List<ModEntry> iconless = mods.Where((ModEntry m) => _meta.TryGetValue(Key(m.FileName), out ModMeta value) && string.IsNullOrWhiteSpace(value.IconFile)).ToList();
		if (iconless.Count > 0)
		{
			int num = 0;
			foreach (ModEntry item3 in iconless)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					if (await RefreshIconAsync(item3, ct).ConfigureAwait(continueOnCapturedContext: false))
					{
						resolved++;
					}
				}
				catch
				{
				}
				num++;
				if (num % 8 == 0)
				{
					progress?.Report($"covers {num}/{iconless.Count}…");
					refresh?.Invoke();
				}
			}
		}
		ct.ThrowIfCancellationRequested();
		Save();
		progress?.Report($"{resolved} of {pending.Count} mods got a cover");
		return resolved;
	}

	private static async Task<string?> TryHashAsync(string path, CancellationToken ct)
	{
		_ = 1;
		try
		{
			string result;
			await using (FileStream stream = File.OpenRead(path))
			{
				result = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private async Task<int> EnrichFromModrinthAsync(Dictionary<string, ModEntry> byHash, CancellationToken ct)
	{
		string content = JsonSerializer.Serialize(new
		{
			hashes = byHash.Keys.ToArray(),
			algorithm = "sha1"
		});
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.modrinth.com/v2/version_files")
		{
			Content = new StringContent(content, Encoding.UTF8, "application/json")
		};
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			return 0;
		}
		Stream utf8Json = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		CancellationToken cancellationToken = ct;
		using JsonDocument document = await JsonDocument.ParseAsync(utf8Json, default, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (document.RootElement.ValueKind != JsonValueKind.Object)
		{
			return 0;
		}
		Dictionary<string, (ModEntry Mod, string ProjectId)> projects = new Dictionary<string, (ModEntry, string)>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty item in document.RootElement.EnumerateObject())
		{
			if (byHash.TryGetValue(item.Name, out ModEntry value) && item.Value.TryGetProperty("project_id", out var value2))
			{
				projects[value.FileName] = (value, value2.GetString() ?? "");
			}
		}
		if (projects.Count == 0)
		{
			return 0;
		}
		string[] value3 = (from p in projects.Values
			select p.ProjectId into id
			where id.Length > 0
			select id).Distinct().ToArray();
		string requestUri = "https://api.modrinth.com/v2/projects?ids=" + Uri.EscapeDataString(JsonSerializer.Serialize(value3));
		using HttpResponseMessage projectResponse = await _http.GetAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!projectResponse.IsSuccessStatusCode)
		{
			return 0;
		}
		Stream utf8Json2 = await projectResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		cancellationToken = ct;
		using JsonDocument projectDocument = await JsonDocument.ParseAsync(utf8Json2, default, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (projectDocument.RootElement.ValueKind != JsonValueKind.Array)
		{
			return 0;
		}
		Dictionary<string, JsonElement> byProject = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonElement item2 in projectDocument.RootElement.EnumerateArray())
		{
			if (item2.TryGetProperty("id", out var value4))
			{
				byProject[value4.GetString() ?? ""] = item2.Clone();
			}
		}
		int resolved = 0;
		foreach (KeyValuePair<string, (ModEntry, string)> item3 in projects)
		{
			ct.ThrowIfCancellationRequested();
			item3.Deconstruct(out var key, out var value5);
			string fileName = key;
			(ModEntry, string) tuple = value5;
			if (byProject.TryGetValue(tuple.Item2, out var value6))
			{
				string text = Str(value6, "title");
				string description = Str(value6, "description");
				string slug = Str(value6, "slug");
				string text2 = Str(value6, "icon_url");
				ModMeta meta = new ModMeta
				{
					Title = (string.IsNullOrWhiteSpace(text) ? (tuple.Item1.Name ?? fileName) : text),
					Description = description,
					Slug = slug,
					Source = "Modrinth"
				};
				if (!string.IsNullOrWhiteSpace(text2))
				{
					ModMeta modMeta = meta;
					key = (modMeta.IconFile = await TryDownloadIconAsync(text2, fileName, ct).ConfigureAwait(continueOnCapturedContext: false));
				}
				_meta[Key(fileName)] = meta;
				resolved++;
			}
		}
		return resolved;
	}

	private async Task<bool> RefreshIconAsync(ModEntry mod, CancellationToken ct)
	{
		if (!_meta.TryGetValue(Key(mod.FileName), out ModMeta meta))
		{
			return false;
		}
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(meta.Title))
		{
			list.Add(meta.Title);
		}
		if (!string.IsNullOrWhiteSpace(meta.Slug))
		{
			list.Add(meta.Slug);
		}
		string text = SearchTerm(mod);
		if (text.Length > 0 && !list.Contains(text))
		{
			list.Add(text);
		}
		foreach (string query in list)
		{
			ct.ThrowIfCancellationRequested();
			if (_curseKey != null)
			{
				string text2 = await CurseLogoAsync(query, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (text2 != null)
				{
					meta.IconFile = text2;
					return true;
				}
			}
			CurseWidget curseWidget = await CurseWidgetAsync(Slugify(query), ct).ConfigureAwait(continueOnCapturedContext: false);
			if (curseWidget?.Icon != null)
			{
				string text3 = await TryDownloadIconAsync(curseWidget.Icon, mod.FileName, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (text3 != null)
				{
					meta.IconFile = text3;
					return true;
				}
			}
		}
		return false;
	}

	private async Task<string?> CurseLogoAsync(string query, CancellationToken ct)
	{
		string requestUri = $"{"https://api.curseforge.com/v1"}/mods/search?gameId={432}&classId={6}&sortField=6&sortOrder=desc&pageSize=1" + "&searchFilter=" + Uri.EscapeDataString(query);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
		request.Headers.Add("x-api-key", _curseKey);
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}
		using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!document.RootElement.TryGetProperty("data", out var value) || value.GetArrayLength() == 0)
		{
			return null;
		}
		JsonElement element = value[0];
		if (!Similar(query, Str(element, "name")) && !Similar(query, Str(element, "slug")))
		{
			return null;
		}
		if (!element.TryGetProperty("logo", out var value2) || value2.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		string text = Str(value2, "thumbnailUrl");
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return await TryDownloadIconAsync(text, query, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static string Slugify(string value)
	{
		string text = new string((from c in value.ToLowerInvariant()
			select (!char.IsLetterOrDigit(c)) ? '-' : c).ToArray());
		while (text.Contains("--", StringComparison.Ordinal))
		{
			text = text.Replace("--", "-");
		}
		return text.Trim('-');
	}

	private async Task<CurseWidget?> CurseWidgetAsync(string slugOrId, CancellationToken ct)
	{
		_ = 1;
		try
		{
			string requestUri = "https://api.cfwidget.com/minecraft/mc-mods/" + Uri.EscapeDataString(slugOrId);
			using Stream stream = await _http.GetStreamAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("id", out var _))
			{
				return null;
			}
			JsonElement value2;
			return new CurseWidget((rootElement.TryGetProperty("thumbnail", out value2) && value2.ValueKind == JsonValueKind.String) ? value2.GetString() : null);
		}
		catch
		{
			return null;
		}
	}

	private async Task<bool> EnrichFromModrinthSearchAsync(ModEntry mod, string mcVersion, CancellationToken ct)
	{
		string query = SearchTerm(mod);
		if (query.Length == 0)
		{
			return false;
		}
		string stringToEscape = "[[" + (string.IsNullOrWhiteSpace(mcVersion) ? "[\"project_type:mod\"]" : ("\"versions:" + mcVersion + "\",[\"project_type:mod\"]")) + "]]";
		string requestUri = $"{"https://api.modrinth.com/v2"}/search?query={Uri.EscapeDataString(query)}&limit=1&index=relevance&facets={Uri.EscapeDataString(stringToEscape)}";
		using HttpResponseMessage response = await _http.GetAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			return false;
		}
		using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!document.RootElement.TryGetProperty("hits", out var value) || value.GetArrayLength() == 0)
		{
			return false;
		}
		JsonElement element = value[0];
		string text = Str(element, "title");
		if (!Similar(query, text) && !Similar(query, Str(element, "slug")))
		{
			return false;
		}
		ModMeta meta = new ModMeta
		{
			Title = (string.IsNullOrWhiteSpace(text) ? query : text),
			Description = Str(element, "description"),
			Slug = Str(element, "slug"),
			Source = "Modrinth"
		};
		string text2 = Str(element, "icon_url");
		if (!string.IsNullOrWhiteSpace(text2))
		{
			ModMeta modMeta = meta;
			modMeta.IconFile = await TryDownloadIconAsync(text2, mod.FileName, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		_meta[Key(mod.FileName)] = meta;
		return true;
	}

	private async Task<bool> EnrichFromCurseAsync(ModEntry mod, string mcVersion, CancellationToken ct)
	{
		string query = SearchTerm(mod);
		if (query.Length == 0)
		{
			return false;
		}
		string requestUri = $"{"https://api.curseforge.com/v1"}/mods/search?gameId={432}&classId={6}&sortField=6&sortOrder=desc&pageSize=1" + "&searchFilter=" + Uri.EscapeDataString(query) + (string.IsNullOrWhiteSpace(mcVersion) ? "" : ("&gameVersion=" + Uri.EscapeDataString(mcVersion))) + "&modLoaderType=1";
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
		request.Headers.Add("x-api-key", _curseKey);
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			return false;
		}
		using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!document.RootElement.TryGetProperty("data", out var value) || value.GetArrayLength() == 0)
		{
			return false;
		}
		JsonElement element = value[0];
		string text = Str(element, "name");
		string description = Str(element, "summary");
		if (!Similar(query, text) && !Similar(query, Str(element, "slug")))
		{
			return false;
		}
		ModMeta meta = new ModMeta
		{
			Title = (string.IsNullOrWhiteSpace(text) ? query : text),
			Description = description,
			Slug = Str(element, "slug"),
			Source = "CurseForge"
		};
		if (element.TryGetProperty("logo", out var value2) && value2.ValueKind == JsonValueKind.Object)
		{
			string text2 = Str(value2, "thumbnailUrl");
			if (!string.IsNullOrWhiteSpace(text2))
			{
				ModMeta modMeta = meta;
				modMeta.IconFile = await TryDownloadIconAsync(text2, mod.FileName, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		_meta[Key(mod.FileName)] = meta;
		return true;
	}

	private static string SearchTerm(ModEntry mod)
	{
		if (!string.IsNullOrWhiteSpace(mod.Name) && mod.Name.Length >= 3)
		{
			return Clean(mod.Name);
		}
		return Clean(ModManager.DisplayName(mod.FileName));
	}

	private static string Clean(string raw)
	{
		string text = raw;
		if (text.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			text = text2.Substring(0, text2.Length - 4);
		}
		int num = text.IndexOfAny(new char[3] { '-', '_', '+' });
		if (num > 2)
		{
			text = text.Substring(0, num);
		}
		return text.Trim();
	}

	private static bool Similar(string left, string right)
	{
		string text = Norm(left);
		string text2 = Norm(right);
		if (text.Length == 0 || text2.Length == 0)
		{
			return false;
		}
		if (!text.Contains(text2, StringComparison.Ordinal))
		{
			return text2.Contains(text, StringComparison.Ordinal);
		}
		return true;
		static string Norm(string value)
		{
			return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
		}
	}

	internal static string? SniffExtension(byte[] bytes)
	{
		if (bytes.Length < 12)
		{
			return null;
		}
		if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71)
		{
			return ".png";
		}
		if (bytes[0] == byte.MaxValue && bytes[1] == 216 && bytes[2] == byte.MaxValue)
		{
			return ".jpg";
		}
		if (bytes[0] == 71 && bytes[1] == 73 && bytes[2] == 70)
		{
			return ".gif";
		}
		if (bytes[0] == 82 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 70 && bytes[8] == 87 && bytes[9] == 69 && bytes[10] == 66)
		{
			_ = bytes[11];
			_ = 80;
			return null;
		}
		return null;
	}

	private async Task<string?> TryDownloadIconAsync(string url, string fileName, CancellationToken ct)
	{
		_ = 1;
		try
		{
			byte[] array = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (array.Length == 0 || array.Length > 4000000)
			{
				return null;
			}
			Directory.CreateDirectory(_iconDir);
			string text = SniffExtension(array);
			if (text == null)
			{
				return null;
			}
			string text2 = new string((from c in Path.GetFileNameWithoutExtension(ModManager.DisplayName(fileName))
				select (!char.IsLetterOrDigit(c)) ? '-' : char.ToLowerInvariant(c)).ToArray());
			if (text2.Length > 60)
			{
				text2 = text2.Substring(0, 60);
			}
			string stored = text2 + text;
			await File.WriteAllBytesAsync(Path.Combine(_iconDir, stored), array, ct).ConfigureAwait(continueOnCapturedContext: false);
			return stored;
		}
		catch
		{
			return null;
		}
	}

	private static string Str(JsonElement element, string property)
	{
		if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}
}
