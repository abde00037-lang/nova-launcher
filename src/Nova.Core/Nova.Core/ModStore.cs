using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class ModStore : IDisposable
{
	private sealed record CurseWidgetFile(int Id, string Name, long Size, string Display, string[] Versions);

	private sealed record CurseWidget(int Id, string Title, string Summary, long Downloads, string Author, string? Icon, List<CurseWidgetFile> Files);

	private const string Modrinth = "https://api.modrinth.com/v2";

	private const string Curse = "https://api.curseforge.com/v1";

	private const int CurseMcGameId = 432;

	private const int CurseModsClassId = 6;

	private readonly HttpClient _http;

	private readonly string? _curseKey;

	public bool CurseForgeAvailable => _curseKey != null;

	public string? LastSearchNote { get; private set; }

	public ModStore(string? curseForgeKey = null)
	{
		_curseKey = (string.IsNullOrWhiteSpace(curseForgeKey) ? null : curseForgeKey.Trim());
		_http = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(45.0)
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/0.2 (minecraft modpack launcher)");
	}

	public static string LoaderOf(string versionId)
	{
		string text = versionId.ToLowerInvariant();
		if (text.Contains("neoforge"))
		{
			return "neoforge";
		}
		if (text.Contains("forge"))
		{
			return "forge";
		}
		if (text.Contains("fabric"))
		{
			return "fabric";
		}
		if (text.Contains("quilt"))
		{
			return "quilt";
		}
		return "vanilla";
	}

	public static string McVersionOf(string versionId)
	{
		string text = (versionId ?? "").Trim();
		if (text.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
		{
			int lastDash = text.LastIndexOf('-');
			if (lastDash > "fabric-loader-".Length)
			{
				return text.Substring(lastDash + 1);
			}
		}
		if (text.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
		{
			string neoVer = text.Substring("neoforge-".Length);
			string? mapped = NeoForgeInstaller.McVersionFromNeoForge(neoVer);
			if (!string.IsNullOrEmpty(mapped))
			{
				return mapped;
			}
		}
		int num = text.IndexOf('-');
		if (num <= 0)
		{
			return text;
		}
		return text.Substring(0, num);
	}

	public static string FormatLoaderLabel(string versionId)
	{
		string text = (versionId ?? "").Trim();
		if (text.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
		{
			string rest = text.Substring("fabric-loader-".Length);
			int idx = rest.LastIndexOf('-');
			return (idx > 0) ? ("Fabric " + rest.Substring(0, idx)) : "Fabric";
		}
		if (text.Contains("-forge-", StringComparison.OrdinalIgnoreCase))
		{
			int idx2 = text.IndexOf("-forge-", StringComparison.OrdinalIgnoreCase);
			return "Forge " + text.Substring(idx2 + 7);
		}
		if (text.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
		{
			return "NeoForge " + text.Substring("neoforge-".Length);
		}
		if (text.Contains("-neoforge-", StringComparison.OrdinalIgnoreCase))
		{
			int idx3 = text.IndexOf("-neoforge-", StringComparison.OrdinalIgnoreCase);
			return "NeoForge " + text.Substring(idx3 + 10);
		}
		return "Vanilla";
	}

	public async Task<List<StoreMod>> SearchAsync(StoreSource source, string query, string mcVersion, string loader, int limit = 20, CancellationToken ct = default(CancellationToken), int offset = 0, string sort = "downloads")
	{
		LastSearchNote = null;
		return (source != StoreSource.CurseForge) ? (await SearchModrinthAsync(query, mcVersion, loader, limit, ct, offset, sort).ConfigureAwait(continueOnCapturedContext: false)) : (await SearchCurseForgeAsync(query, mcVersion, loader, limit, ct, offset, sort).ConfigureAwait(continueOnCapturedContext: false));
	}

	private async Task<List<StoreMod>> SearchModrinthAsync(string query, string mcVersion, string loader, int limit, CancellationToken ct, int offset = 0, string sort = "downloads")
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(mcVersion))
		{
			list.Add("[\"versions:" + mcVersion + "\"]");
		}
		if (!string.IsNullOrWhiteSpace(loader) && loader != "vanilla")
		{
			list.Add("[\"categories:" + loader + "\"]");
		}
		list.Add("[\"project_type:mod\"]");
		string value = sort switch
		{
			"newest" => "newest", 
			"updated" => "updated", 
			"name" => "relevance", 
			_ => "downloads", 
		};
		string requestUri = $"{"https://api.modrinth.com/v2"}/search?query={Uri.EscapeDataString(query ?? "")}&limit={limit}&index={value}&offset={Math.Max(0, offset)}" + ((list.Count > 0) ? ("&facets=" + Uri.EscapeDataString("[" + string.Join(",", list) + "]")) : "");
		using Stream stream = await _http.GetStreamAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
		List<StoreMod> list2 = new List<StoreMod>();
		if (!jsonDocument.RootElement.TryGetProperty("hits", out var value2))
		{
			return list2;
		}
		foreach (JsonElement item in value2.EnumerateArray())
		{
			list2.Add(new StoreMod(Str(item, "project_id"), Str(item, "slug"), Str(item, "title"), Str(item, "author"), Str(item, "description"), item.TryGetProperty("downloads", out var value3) ? value3.GetInt64() : 0, Str(item, "icon_url"), StoreSource.Modrinth));
		}
		return list2;
	}

	private async Task<List<StoreMod>> SearchCurseForgeAsync(string query, string mcVersion, string loader, int limit, CancellationToken ct, int offset = 0, string sort = "downloads")
	{
		if (_curseKey == null)
		{
			throw new InvalidOperationException("CurseForge needs a free API key (Settings → mod store)");
		}
		string text = sort;
		int num;
		if (text == "newest")
		{
			num = 3;
		}
		else
		{
			num = ((!(text == "name")) ? 6 : 4);
		}
		int value = num;
		string text2 = $"{"https://api.curseforge.com/v1"}/mods/search?gameId={432}&classId={6}&sortField={value}&sortOrder=desc&pageSize={limit}&index={Math.Max(0, offset)}" + (string.IsNullOrWhiteSpace(query) ? "" : ("&searchFilter=" + Uri.EscapeDataString(query))) + (string.IsNullOrWhiteSpace(mcVersion) ? "" : ("&gameVersion=" + Uri.EscapeDataString(mcVersion)));
		int num2 = loader switch
		{
			"forge" => 1, 
			"fabric" => 4, 
			"quilt" => 5, 
			"neoforge" => 6, 
			_ => 0, 
		};
		if (num2 > 0)
		{
			text2 = text2 + "&modLoaderType=" + num2;
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, text2);
		request.Headers.Add("x-api-key", _curseKey);
		request.Headers.TryAddWithoutValidation("User-Agent", "NovaLauncher/0.1");
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			int code = (int)response.StatusCode;
			num = code;
			bool flag = ((num == 401 || num == 403) ? true : false);
			if (flag && string.IsNullOrWhiteSpace(query))
			{
				LastSearchNote = ((code == 401) ? "CurseForge key rejected (401) - paste it again in Settings" : "type a mod name, or paste a curseforge.com link, or switch to Modrinth");
				return new List<StoreMod>();
			}
			num = code;
			if ((num == 401 || num == 403) ? true : false)
			{
				StoreMod storeMod = await DirectCurseLookupAsync(query, mcVersion, ct).ConfigureAwait(continueOnCapturedContext: false);
				if ((object)storeMod != null)
				{
					LastSearchNote = $"CurseForge blocks public search for this key ({code}) - matched the project directly";
					return new List<StoreMod> { storeMod };
				}
				LastSearchNote = ((code == 401) ? "CurseForge key rejected (401) - paste it again in Settings" : ("no project named '" + query + "' - paste the curseforge.com link instead, or use Modrinth"));
				return new List<StoreMod>();
			}
			text = ((code != 401) ? $"CurseForge answered {code} - try Modrinth" : "CurseForge key rejected - paste it again in Settings");
			throw new InvalidOperationException(text);
		}
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
		List<StoreMod> list = new List<StoreMod>();
		if (!jsonDocument.RootElement.TryGetProperty("data", out var value2))
		{
			return list;
		}
		foreach (JsonElement item in value2.EnumerateArray())
		{
			string author = "CurseForge";
			if (item.TryGetProperty("authors", out var value3) && value3.GetArrayLength() > 0)
			{
				author = Str(value3[0], "name");
			}
			string iconUrl = null;
			if (item.TryGetProperty("logo", out var value4) && value4.ValueKind == JsonValueKind.Object)
			{
				iconUrl = Str(value4, "thumbnailUrl");
			}
			string id = (item.TryGetProperty("id", out var value5) ? value5.GetInt64().ToString() : "");
			string slug = Str(item, "slug");
			list.Add(new StoreMod(id, slug, Str(item, "name"), author, Str(item, "summary"), item.TryGetProperty("downloadCount", out var value6) ? ((long)value6.GetDouble()) : 0, iconUrl, StoreSource.CurseForge));
		}
		return list;
	}

	private static (string? Slug, string? Id) SplitCurseQuery(string query)
	{
		string text = (query ?? "").Trim();
		if (text.Length == 0)
		{
			return (Slug: null, Id: null);
		}
		int num = text.IndexOf("curseforge.com/", StringComparison.OrdinalIgnoreCase);
		if (num >= 0)
		{
			string text2 = text;
			int num2 = num + "curseforge.com/".Length;
			string[] array = text2.Substring(num2, text2.Length - num2).Split('/', StringSplitOptions.RemoveEmptyEntries);
			int num3 = Array.FindIndex(array, (string p) => p.Equals("mc-mods", StringComparison.OrdinalIgnoreCase) || p.Equals("mc-mods-legacy", StringComparison.OrdinalIgnoreCase) || p.Equals("mc-mods-alpha", StringComparison.OrdinalIgnoreCase));
			if (num3 >= 0 && array.Length > num3 + 1)
			{
				return (Slug: array[num3 + 1], Id: null);
			}
		}
		if (!long.TryParse(text, out var _))
		{
			return (Slug: text.Trim('/'), Id: null);
		}
		return (Slug: null, Id: text);
	}

	private async Task<CurseWidget?> CurseWidgetAsync(string slugOrId, CancellationToken ct)
	{
		string requestUri = "https://api.cfwidget.com/minecraft/mc-mods/" + Uri.EscapeDataString(slugOrId);
		try
		{
			using Stream stream = await _http.GetStreamAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("id", out var value))
			{
				return null;
			}
			long downloads = 0L;
			if (rootElement.TryGetProperty("downloads", out var value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("total", out var value3) && value3.ValueKind == JsonValueKind.Number)
			{
				downloads = value3.GetInt64();
			}
			string author = "CurseForge";
			if (rootElement.TryGetProperty("members", out var value4) && value4.ValueKind == JsonValueKind.Array && value4.GetArrayLength() > 0)
			{
				author = Str(value4[0], "username");
			}
			string icon = null;
			if (rootElement.TryGetProperty("thumbnail", out var value5) && value5.ValueKind == JsonValueKind.String)
			{
				icon = value5.GetString();
			}
			List<CurseWidgetFile> list = new List<CurseWidgetFile>();
			if (rootElement.TryGetProperty("files", out var value6) && value6.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in value6.EnumerateArray())
				{
					string[] versions = ((item.TryGetProperty("versions", out var value7) && value7.ValueKind == JsonValueKind.Array) ? (from x in value7.EnumerateArray()
						select x.GetString() ?? "").ToArray() : Array.Empty<string>());
					int id = ((item.TryGetProperty("id", out var value8) && value8.ValueKind == JsonValueKind.Number) ? value8.GetInt32() : 0);
					long size = ((item.TryGetProperty("filesize", out var value9) && value9.ValueKind == JsonValueKind.Number) ? value9.GetInt64() : 0);
					list.Add(new CurseWidgetFile(id, Str(item, "name"), size, Str(item, "display"), versions));
				}
			}
			return new CurseWidget((int)value.GetInt64(), Str(rootElement, "title"), Str(rootElement, "summary"), downloads, author, icon, list);
		}
		catch
		{
			return null;
		}
	}

	private async Task<StoreMod?> DirectCurseLookupAsync(string query, string mcVersion, CancellationToken ct)
	{
		(string, string) tuple = SplitCurseQuery(query);
		string slug = tuple.Item1;
		string item = tuple.Item2;
		CurseWidget curseWidget = await CurseWidgetAsync(item ?? slug ?? query, ct).ConfigureAwait(continueOnCapturedContext: false);
		if ((object)curseWidget == null)
		{
			return null;
		}
		bool flag = curseWidget.Files.Any((CurseWidgetFile f) => MatchesVersion(f, mcVersion));
		if (curseWidget.Files.Count > 0 && !flag)
		{
			return null;
		}
		return new StoreMod(curseWidget.Id.ToString(), slug ?? curseWidget.Title.Replace(' ', '-').ToLowerInvariant(), curseWidget.Title, curseWidget.Author, curseWidget.Summary, curseWidget.Downloads, curseWidget.Icon, StoreSource.CurseForge);
	}

	private static bool MatchesVersion(CurseWidgetFile file, string mcVersion)
	{
		if (!string.IsNullOrWhiteSpace(mcVersion))
		{
			return file.Versions.Any((string v) => v.Equals(mcVersion, StringComparison.OrdinalIgnoreCase));
		}
		return true;
	}

	private static string ForgeCdnUrl(int fileId, string fileName)
	{
		return $"https://mediafilez.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{Uri.EscapeDataString(fileName)}";
	}

	public async Task<StoreFile?> ResolveFileAsync(StoreMod mod, string mcVersion, string loader, CancellationToken ct = default(CancellationToken))
	{
		List<StoreFile> source = ((mod.Source != StoreSource.CurseForge) ? (await ModrinthFilesAsync(mod, mcVersion, loader, ct).ConfigureAwait(continueOnCapturedContext: false)) : (await CurseFilesAsync(mod, mcVersion, loader, ct).ConfigureAwait(continueOnCapturedContext: false)));
		return source.FirstOrDefault((StoreFile f) => !string.IsNullOrWhiteSpace(f.Url));
	}

	private async Task<List<StoreFile>> ModrinthFilesAsync(StoreMod mod, string mcVersion, string loader, CancellationToken ct)
	{
		string text = $"{"https://api.modrinth.com/v2"}/project/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(mod.Slug) ? mod.Id : mod.Slug)}/version?game_versions={Uri.EscapeDataString("[\"" + mcVersion + "\"]")}";
		bool flag = (((loader != null && loader.Length == 0) || loader == "vanilla") ? true : false);
		string requestUri = text + (flag ? "" : ("&loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]")));
		using Stream stream = await _http.GetStreamAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
		List<StoreFile> list = new List<StoreFile>();
		foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
		{
			if (item.TryGetProperty("files", out var value) && value.GetArrayLength() != 0)
			{
				List<JsonElement> list2 = value.EnumerateArray().ToList();
				JsonElement jsonElement = list2.FirstOrDefault((JsonElement f) => f.TryGetProperty("primary", out var value5) && value5.GetBoolean());
				JsonElement element = ((jsonElement.ValueKind != JsonValueKind.Undefined) ? jsonElement : list2[0]);
				string fileName = Str(element, "filename");
				string url = Str(element, "url");
				string sha = null;
				if (element.TryGetProperty("hashes", out var value2) && value2.TryGetProperty("sha1", out var value3))
				{
					sha = value3.GetString();
				}
				long size = (element.TryGetProperty("size", out var value4) ? value4.GetInt64() : 0);
				list.Add(new StoreFile(fileName, url, sha, size));
			}
		}
		return list;
	}

	private async Task<List<StoreFile>> CurseFilesAsync(StoreMod mod, string mcVersion, string loader, CancellationToken ct)
	{
		if (_curseKey == null)
		{
			throw new InvalidOperationException("CurseForge needs an API key");
		}
		int num = loader switch
		{
			"forge" => 1, 
			"fabric" => 4, 
			"quilt" => 5, 
			"neoforge" => 6, 
			_ => 0, 
		};
		string requestUri = $"{"https://api.curseforge.com/v1"}/mods/{mod.Id}/files?gameVersion={Uri.EscapeDataString(mcVersion)}" + ((num > 0) ? ("&modLoaderType=" + num) : "");
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
		request.Headers.Add("x-api-key", _curseKey);
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			return new List<StoreFile>();
		}
		List<StoreFile> files = new List<StoreFile>();
		using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!document.RootElement.TryGetProperty("data", out var value))
		{
			return files;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			string text = Str(item, "downloadUrl");
			if (string.IsNullOrWhiteSpace(text))
			{
				int num2 = ((item.TryGetProperty("id", out var value2) && value2.ValueKind == JsonValueKind.Number) ? value2.GetInt32() : 0);
				if (num2 > 0)
				{
					text = ForgeCdnUrl(num2, Str(item, "fileName"));
				}
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			string sha = null;
			if (item.TryGetProperty("hashes", out var value3))
			{
				foreach (JsonElement item2 in value3.EnumerateArray())
				{
					if (Str(item2, "algo").Equals("sha1", StringComparison.OrdinalIgnoreCase))
					{
						sha = Str(item2, "value");
					}
				}
			}
			long size = (item.TryGetProperty("fileLength", out var value4) ? value4.GetInt64() : 0);
			files.Add(new StoreFile(Str(item, "fileName"), text, sha, size));
		}
		if (files.Count > 0)
		{
			return files;
		}
		CurseWidget curseWidget = await CurseWidgetAsync(mod.Id, ct).ConfigureAwait(continueOnCapturedContext: false);
		if ((object)curseWidget == null)
		{
			return files;
		}
		foreach (CurseWidgetFile item3 in curseWidget.Files.Where((CurseWidgetFile f) => MatchesVersion(f, mcVersion)))
		{
			files.Add(new StoreFile(item3.Name, ForgeCdnUrl(item3.Id, item3.Name), null, item3.Size));
		}
		return files;
	}

	private async Task<List<StoreMod>> RequiredDependenciesAsync(StoreMod mod, string mcVersion, string loader, CancellationToken ct)
	{
		if (mod.Source == StoreSource.CurseForge)
		{
			return await RequiredCurseDependenciesAsync(mod, mcVersion, loader, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (mod.Source != StoreSource.Modrinth)
		{
			return new List<StoreMod>();
		}
		string text = $"{"https://api.modrinth.com/v2"}/project/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(mod.Slug) ? mod.Id : mod.Slug)}/version?game_versions={Uri.EscapeDataString("[\"" + mcVersion + "\"]")}";
		bool flag = (((loader != null && loader.Length == 0) || loader == "vanilla") ? true : false);
		string requestUri = text + (flag ? "" : ("&loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]")));
		using Stream stream = await _http.GetStreamAsync(requestUri, ct).ConfigureAwait(continueOnCapturedContext: false);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
		List<StoreMod> list = new List<StoreMod>();
		foreach (JsonElement item in document.RootElement.EnumerateArray())
		{
			if (!item.TryGetProperty("dependencies", out var value))
			{
				continue;
			}
			foreach (JsonElement item2 in value.EnumerateArray())
			{
				if (!string.Equals(Str(item2, "dependency_type"), "required", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string text2 = Str(item2, "project_id");
				if (!string.IsNullOrWhiteSpace(text2))
				{
					StoreMod storeMod = await GetModrinthProjectAsync(text2, ct).ConfigureAwait(continueOnCapturedContext: false);
					if ((object)storeMod != null)
					{
						list.Add(storeMod);
					}
				}
			}
			break;
		}
		return list;
	}

	private async Task<List<StoreMod>> RequiredCurseDependenciesAsync(StoreMod mod, string mcVersion, string loader, CancellationToken ct)
	{
		List<StoreMod> deps = new List<StoreMod>();
		if (_curseKey == null || string.IsNullOrWhiteSpace(mod.Id))
		{
			return deps;
		}
		try
		{
			int loaderType = loader switch
			{
				"forge" => 1,
				"fabric" => 4,
				"quilt" => 5,
				"neoforge" => 6,
				_ => 0,
			};
			string url = $"https://api.curseforge.com/v1/mods/{Uri.EscapeDataString(mod.Id)}/files?pageSize=10"
				+ (string.IsNullOrWhiteSpace(mcVersion) ? "" : ("&gameVersion=" + Uri.EscapeDataString(mcVersion)))
				+ ((loaderType > 0) ? ("&modLoaderType=" + loaderType) : "");
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
			req.Headers.Add("x-api-key", _curseKey);
			req.Headers.TryAddWithoutValidation("User-Agent", "NovaLauncher/0.2");
			using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!resp.IsSuccessStatusCode)
			{
				return deps;
			}
			using Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
			{
				return deps;
			}
			HashSet<int> depIds = new HashSet<int>();
			foreach (JsonElement fileEl in data.EnumerateArray())
			{
				if (fileEl.TryGetProperty("dependencies", out var depArr) && depArr.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement d in depArr.EnumerateArray())
					{
						int relType = (d.TryGetProperty("relationType", out var rt) ? rt.GetInt32() : 0);
						int modId = (d.TryGetProperty("modId", out var mid) ? mid.GetInt32() : 0);
						if (relType == 3 && modId > 0)
						{
							depIds.Add(modId);
						}
					}
				}
				break;
			}
			foreach (int depModId in depIds)
			{
				using HttpRequestMessage mReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.curseforge.com/v1/mods/{depModId}");
				mReq.Headers.Add("x-api-key", _curseKey);
				mReq.Headers.TryAddWithoutValidation("User-Agent", "NovaLauncher/0.2");
				using HttpResponseMessage mResp = await _http.SendAsync(mReq, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!mResp.IsSuccessStatusCode)
				{
					continue;
				}
				using Stream mStream = await mResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				using JsonDocument mDoc = await JsonDocument.ParseAsync(mStream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (mDoc.RootElement.TryGetProperty("data", out var mData))
				{
					string icon = (mData.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object) ? Str(logo, "thumbnailUrl") : "";
					deps.Add(new StoreMod(depModId.ToString(), Str(mData, "slug"), Str(mData, "name"), "dependency", Str(mData, "summary"), 0L, icon, StoreSource.CurseForge));
				}
			}
		}
		catch
		{
		}
		return deps;
	}

	private async Task<StoreMod?> GetModrinthProjectAsync(string projectId, CancellationToken ct)
	{
		_ = 1;
		try
		{
			using Stream stream = await _http.GetStreamAsync("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId), ct).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value;
			return new StoreMod(Str(rootElement, "id"), Str(rootElement, "slug"), Str(rootElement, "title"), "dependency", Str(rootElement, "description"), rootElement.TryGetProperty("downloads", out value) ? value.GetInt64() : 0, Str(rootElement, "icon_url"), StoreSource.Modrinth);
		}
		catch
		{
			return null;
		}
	}

	public async Task<ModInstallResult> InstallAsync(StoreMod mod, InstanceConfig instance, bool withDependencies = true, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		ModInstallResult result = new ModInstallResult();
		string mcVersion = McVersionOf(instance.VersionId);
		string loader = LoaderOf(instance.VersionId);
		string modsDir = Path.Combine(instance.GameDirectory, "mods");
		Directory.CreateDirectory(modsDir);
		using Downloader downloader = new Downloader(3);
		await InstallOneAsync(mod, modsDir, mcVersion, loader, downloader, result, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (withDependencies)
		{
			foreach (StoreMod item in await RequiredDependenciesAsync(mod, mcVersion, loader, ct).ConfigureAwait(continueOnCapturedContext: false))
			{
				log?.Report("dependency: " + item.Name);
				await InstallOneAsync(item, modsDir, mcVersion, loader, downloader, result, log, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		log?.Report(result.Summary);
		return result;
	}

	private async Task InstallOneAsync(StoreMod mod, string modsDir, string mcVersion, string loader, Downloader downloader, ModInstallResult result, IProgress<string>? log, CancellationToken ct)
	{
		_ = 1;
		try
		{
			StoreFile file = await ResolveFileAsync(mod, mcVersion, loader, ct).ConfigureAwait(continueOnCapturedContext: false);
			if ((object)file == null)
			{
				result.Failed.Add(mod.Name + " (no file for " + mcVersion + " " + loader + ")");
				log?.Report("no compatible file: " + mod.Name);
				return;
			}
			string targetPath = Path.Combine(modsDir, file.FileName);
			switch (await downloader.EnsureAsync(targetPath, file.Url, file.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false))
			{
			case FetchResult.Downloaded:
				result.Installed.Add(file.FileName);
				log?.Report("installed " + file.FileName);
				break;
			case FetchResult.AlreadyValid:
				result.Skipped.Add(file.FileName);
				log?.Report("already there: " + file.FileName);
				break;
			default:
				result.Failed.Add(file.FileName);
				log?.Report("failed: " + file.FileName);
				break;
			}
		}
		catch (Exception ex)
		{
			result.Failed.Add(mod.Name);
			log?.Report("failed: " + mod.Name + " - " + ex.Message);
		}
	}

	private static string Str(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
