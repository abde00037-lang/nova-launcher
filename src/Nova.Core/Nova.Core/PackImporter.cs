using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class PackImporter
{
	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(45.0)
	};

	static PackImporter()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/0.2 (minecraft modpack launcher)");
	}

	public static async Task<PackImportResult> ImportAsync(string zipPath, LauncherPaths paths, string? instanceId = null, string? displayName = null, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), string? curseForgeKey = null)
	{
		PackInfo info = PackInspector.Inspect(zipPath);
		if (info.Format == PackFormat.Unknown)
		{
			throw new InvalidOperationException("cannot import " + Path.GetFileName(zipPath) + ": " + string.Join("; ", info.Notes));
		}
		string? effectiveKey = curseForgeKey;
		if (string.IsNullOrWhiteSpace(effectiveKey))
		{
			effectiveKey = LauncherSettings.Load(paths).CurseForgeKey;
		}
		string id = MakeId(instanceId ?? info.Name);
		InstanceStore store = new InstanceStore(paths);
		int suffix = 2;
		string baseId = id;
		while (store.Find(id) != null)
		{
			id = baseId + "-" + suffix++;
		}
		string name = (string.IsNullOrWhiteSpace(displayName) ? (string.IsNullOrWhiteSpace(info.Name) ? id : info.Name) : displayName);
		string text = Path.Combine(paths.Instances, id);
		string gameDir = Path.Combine(text, "game");
		Directory.CreateDirectory(gameDir);
		PackImportResult result = new PackImportResult
		{
			InstanceId = id,
			Name = name,
			GameDirectory = gameDir
		};
		log?.Report("importing " + info.FormatName + ": " + name);
		using (ZipArchive zip = ZipFile.OpenRead(zipPath))
		{
			switch (info.Format)
			{
			case PackFormat.Modrinth:
				await ImportModrinthAsync(zip, gameDir, result, log, ct).ConfigureAwait(continueOnCapturedContext: false);
				break;
			case PackFormat.CurseForge:
				await ImportCurseForgeAsync(zip, text, gameDir, effectiveKey, result, log, ct).ConfigureAwait(continueOnCapturedContext: false);
				break;
			default:
				ExtractFolder(zip, gameDir, "", result, new string[1] { "nova-pack.json" });
				break;
			}
		}
		string resolvedVersionId = await EnsurePackLoaderAsync(info, paths, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		string modsDir = Path.Combine(gameDir, "mods");
		int num = ModManager.CountInstalled(modsDir);
		string mcForJava = (string.IsNullOrWhiteSpace(info.McVersion) ? ModStore.McVersionOf(resolvedVersionId) : info.McVersion);
		JavaInstall? java = JavaLocator.Pick(VersionResolver.GuessJavaMajor(string.IsNullOrWhiteSpace(mcForJava) ? "1.12.2" : mcForJava));
		if ((object?)java == null && !string.IsNullOrWhiteSpace(mcForJava))
		{
			try
			{
				java = await JavaProvisioner.EnsureAsync(paths, VersionResolver.GuessJavaMajor(mcForJava), log, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		}
		AccountStore accountStore = new AccountStore(paths);
		LauncherSettings settings = LauncherSettings.Load(paths);
		string activePlayer = accountStore.Active(settings).Name;
		InstanceConfig instanceConfig = new InstanceConfig
		{
			Id = id,
			Name = name,
			GameDirectory = gameDir,
			VersionId = resolvedVersionId,
			JavaPath = java?.Path,
			MemoryMb = LaunchBuilder.RecommendMemoryMb(num),
			PlayerName = (string.IsNullOrWhiteSpace(activePlayer) ? "Player" : activePlayer),
			CreatedUtc = DateTime.UtcNow
		};
		store.Save(instanceConfig);
		result.VersionId = instanceConfig.VersionId;
		result.Notes.Add($"instance '{id}' created with {num} mod(s), {instanceConfig.MemoryMb} MB heap");
		log?.Report(result.Summary);
		return result;
	}

	private static async Task<string> EnsurePackLoaderAsync(PackInfo info, LauncherPaths paths, IProgress<string>? log, CancellationToken ct)
	{
		string built = BuildVersionId(info);
		if (string.IsNullOrWhiteSpace(info.McVersion))
		{
			return built;
		}
		try
		{
			string loader = (info.Loader ?? "").ToLowerInvariant();
			if (loader == "forge" && !string.IsNullOrWhiteSpace(info.LoaderVersion))
			{
				return await ForgeInstaller.InstallAsync(info.McVersion, info.LoaderVersion, paths, log, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (loader == "fabric")
			{
				return await FabricInstaller.InstallAsync(info.McVersion, info.LoaderVersion, paths, log, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (loader == "neoforge")
			{
				return await NeoForgeInstaller.InstallAsync(info.McVersion, info.LoaderVersion, paths, log, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			await MojangMeta.InstallAsync(info.McVersion, paths, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			log?.Report("loader setup note: " + ex.Message);
		}
		return built;
	}

	private static async Task ImportModrinthAsync(ZipArchive zip, string gameDir, PackImportResult result, IProgress<string>? log, CancellationToken ct)
	{
		ModrinthIndex modrinthIndex = PackInspector.ReadJson<ModrinthIndex>(zip.GetEntry("modrinth.index.json")!) ?? new ModrinthIndex();
		List<ModrinthFile> files = (modrinthIndex.Files ?? new List<ModrinthFile>()).Where((ModrinthFile f) => f.IsClientFile).ToList();
		log?.Report($"{files.Count} file(s) listed by the pack");
		using Downloader downloader = new Downloader(8);
		int done = 0;
		object sync = new object();
		IEnumerable<Task> tasks = files.Select(async delegate(ModrinthFile file)
		{
			ct.ThrowIfCancellationRequested();
			string rel = file.Path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			string target = Path.Combine(gameDir, rel);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			FetchResult fetchResult = await downloader.EnsureAsync(target, file.Url, file.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false);
			int current = Interlocked.Increment(ref done);
			lock (sync)
			{
				switch (fetchResult)
				{
				case FetchResult.AlreadyValid:
					result.FilesSkipped++;
					break;
				case FetchResult.Downloaded:
					result.FilesWritten++;
					break;
				default:
					result.FilesFailed++;
					result.Missing.Add(file.Path);
					break;
				}
			}
			if (current % 10 == 0 || current == files.Count)
			{
				log?.Report($"downloaded {current}/{files.Count} ({downloader.BytesDownloaded / 1024 / 1024} MB)");
			}
		});
		await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false);
		ExtractFolder(zip, gameDir, "overrides/", result);
		ExtractFolder(zip, gameDir, "client-overrides/", result);
		if (result.Missing.Count > 0)
		{
			log?.Report($"{result.Missing.Count} file(s) could not be downloaded: " + string.Join(", ", result.Missing.Take(5)));
		}
	}

	private static async Task ImportCurseForgeAsync(ZipArchive zip, string instanceFolder, string gameDir, string? curseKey, PackImportResult result, IProgress<string>? log, CancellationToken ct)
	{
		CurseManifest? manifest = PackInspector.ReadJson<CurseManifest>(zip.GetEntry("manifest.json")!);
		ExtractFolder(zip, gameDir, "overrides/", result);
		List<CurseFile> required = manifest?.Files?.Where((CurseFile f) => f.Required && f.ProjectId > 0 && f.FileId > 0).ToList() ?? new List<CurseFile>();
		if (required.Count == 0)
		{
			return;
		}
		log?.Report($"{required.Count} CurseForge mod file(s) listed in manifest.json");
		string modsDir = Path.Combine(gameDir, "mods");
		Directory.CreateDirectory(modsDir);
		Dictionary<int, (string FileName, string Url, string? Sha1)> resolved = new Dictionary<int, (string, string, string?)>();
		if (!string.IsNullOrWhiteSpace(curseKey))
		{
			try
			{
				string payload = JsonSerializer.Serialize(new
				{
					fileIds = required.Select((CurseFile f) => f.FileId).Distinct().ToArray()
				});
				using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://api.curseforge.com/v1/mods/files")
				{
					Content = new StringContent(payload, Encoding.UTF8, "application/json")
				};
				req.Headers.Add("x-api-key", curseKey.Trim());
				using HttpResponseMessage resp = await Http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (resp.IsSuccessStatusCode)
				{
					using JsonDocument doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement item in dataEl.EnumerateArray())
						{
							int fid = (item.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0);
							string fn = ((item.TryGetProperty("fileName", out var fnEl) && fnEl.ValueKind == JsonValueKind.String) ? (fnEl.GetString() ?? "") : "");
							string dl = ((item.TryGetProperty("downloadUrl", out var dlEl) && dlEl.ValueKind == JsonValueKind.String) ? (dlEl.GetString() ?? "") : "");
							if (string.IsNullOrWhiteSpace(dl) && fid > 0 && !string.IsNullOrWhiteSpace(fn))
							{
								dl = $"https://mediafilez.forgecdn.net/files/{fid / 1000}/{fid % 1000}/{Uri.EscapeDataString(fn)}";
							}
							string? sha1 = null;
							if (item.TryGetProperty("hashes", out var hashesEl) && hashesEl.ValueKind == JsonValueKind.Array)
							{
								foreach (JsonElement h in hashesEl.EnumerateArray())
								{
									if (h.TryGetProperty("algo", out var aEl) && aEl.ValueKind == JsonValueKind.Number && aEl.GetInt32() == 1 && h.TryGetProperty("value", out var vEl))
									{
										sha1 = vEl.GetString();
									}
								}
							}
							if (fid > 0 && !string.IsNullOrWhiteSpace(fn) && !string.IsNullOrWhiteSpace(dl))
							{
								resolved[fid] = (fn, dl, sha1);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				log?.Report("CurseForge batch lookup note: " + ex.Message);
			}
		}
		List<CurseFile> unresolved = required.Where((CurseFile f) => !resolved.ContainsKey(f.FileId)).ToList();
		if (unresolved.Count > 0)
		{
			log?.Report($"resolving {unresolved.Count} file(s) via CFWidget fallback…");
			using SemaphoreSlim gate = new SemaphoreSlim(6);
			object mapLock = new object();
			await Task.WhenAll(unresolved.Select(async delegate(CurseFile cf)
			{
				await gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					using Stream s = await Http.GetStreamAsync($"https://api.cfwidget.com/{cf.ProjectId}", ct).ConfigureAwait(continueOnCapturedContext: false);
					using JsonDocument doc2 = await JsonDocument.ParseAsync(s, default, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (doc2.RootElement.TryGetProperty("files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement fEl in filesEl.EnumerateArray())
						{
							int fid2 = (fEl.TryGetProperty("id", out var idEl2) ? idEl2.GetInt32() : 0);
							if (fid2 == cf.FileId)
							{
								string fn2 = ((fEl.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String) ? (nEl.GetString() ?? "") : "");
								if (!string.IsNullOrWhiteSpace(fn2))
								{
									string dl2 = $"https://mediafilez.forgecdn.net/files/{fid2 / 1000}/{fid2 % 1000}/{Uri.EscapeDataString(fn2)}";
									lock (mapLock)
									{
										resolved[fid2] = (fn2, dl2, null);
									}
								}
								break;
							}
						}
					}
				}
				catch
				{
				}
				finally
				{
					gate.Release();
				}
			})).ConfigureAwait(continueOnCapturedContext: false);
		}
		using Downloader downloader = new Downloader(8);
		int done = 0;
		object resLock = new object();
		List<string> missingIds = new List<string>();
		await Task.WhenAll(required.Select(async delegate(CurseFile cf)
		{
			ct.ThrowIfCancellationRequested();
			if (!resolved.TryGetValue(cf.FileId, out var info2))
			{
				lock (resLock)
				{
					result.FilesFailed++;
					missingIds.Add($"{cf.ProjectId}:{cf.FileId}");
					result.Missing.Add($"{cf.ProjectId}:{cf.FileId}");
				}
				return;
			}
			string dest = Path.Combine(modsDir, info2.FileName);
			FetchResult fr = await downloader.EnsureAsync(dest, info2.Url, info2.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false);
			int curr = Interlocked.Increment(ref done);
			lock (resLock)
			{
				switch (fr)
				{
				case FetchResult.AlreadyValid:
					result.FilesSkipped++;
					break;
				case FetchResult.Downloaded:
					result.FilesWritten++;
					break;
				default:
					result.FilesFailed++;
					missingIds.Add($"{cf.ProjectId}:{cf.FileId} ({info2.FileName})");
					result.Missing.Add(info2.FileName);
					break;
				}
			}
			if (curr % 10 == 0 || curr == required.Count)
			{
				log?.Report($"downloaded {curr}/{required.Count} CurseForge mods ({downloader.BytesDownloaded / 1024 / 1024} MB)");
			}
		})).ConfigureAwait(continueOnCapturedContext: false);
		if (missingIds.Count > 0)
		{
			List<string> lines = new List<string>
			{
				"# CurseForge files that blocked third-party distribution or could not be fetched:",
				"# Add a CurseForge API key in Settings or download manually:"
			};
			lines.AddRange(missingIds);
			File.WriteAllLines(Path.Combine(instanceFolder, "curseforge-missing.txt"), lines);
			result.Notes.Add($"{missingIds.Count} restricted file(s) listed in curseforge-missing.txt");
		}
	}

	private static void ExtractFolder(ZipArchive zip, string gameDir, string prefix, PackImportResult result, string[]? skip = null)
	{
		int num = 0;
		foreach (ZipArchiveEntry entry in zip.Entries)
		{
			if (entry.FullName.EndsWith('/') || !entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || (skip != null && skip.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase)))
			{
				continue;
			}
			string fullName = entry.FullName;
			int length = prefix.Length;
			string text = fullName.Substring(length, fullName.Length - length).Replace('/', Path.DirectorySeparatorChar);
			if (!string.IsNullOrWhiteSpace(text) && !text.Contains(".."))
			{
				string text2 = Path.Combine(gameDir, text);
				Directory.CreateDirectory(Path.GetDirectoryName(text2)!);
				try
				{
					entry.ExtractToFile(text2, overwrite: true);
					num++;
				}
				catch (Exception)
				{
					result.FilesFailed++;
				}
			}
		}
		if (num > 0)
		{
			result.FilesWritten += num;
		}
	}

	private static string BuildVersionId(PackInfo info)
	{
		string text = (string.IsNullOrWhiteSpace(info.McVersion) ? "" : info.McVersion);
		if (string.IsNullOrWhiteSpace(info.LoaderVersion))
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			return "";
		}
		string text2 = (info.Loader ?? "").ToLowerInvariant();
		if (text2 == "forge" && !string.IsNullOrWhiteSpace(text))
		{
			return text + "-forge-" + info.LoaderVersion;
		}
		if (text2 == "fabric")
		{
			return ("fabric-loader-" + info.LoaderVersion + "-" + text).Trim('-');
		}
		if (text2 == "neoforge")
		{
			return "neoforge-" + info.LoaderVersion;
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return $"{text}-{text2}-{info.LoaderVersion}";
		}
		return info.LoaderVersion;
	}

	public static string MakeId(string raw)
	{
		string text = new string((from c in (raw ?? "").Trim().ToLowerInvariant()
			select (!char.IsLetterOrDigit(c)) ? '-' : c).ToArray());
		while (text.Contains("--"))
		{
			text = text.Replace("--", "-");
		}
		text = text.Trim('-');
		if (text.Length != 0)
		{
			return text;
		}
		return "pack-" + DateTime.UtcNow.ToString("HHmmss");
	}
}
