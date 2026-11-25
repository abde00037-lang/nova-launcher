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

	public static async Task<PackImportResult> ImportAsync(string zipPath, LauncherPaths paths, string? instanceId = null, string? displayName = null, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), string? curseForgeKey = null, ArchiveLimits? limits = null, IProgress<PackImportProgress>? progress = null)
	{
		limits ??= ArchiveLimits.Default;
		PackInfo info = PackInspector.Inspect(zipPath, limits);
		if (info.Format == PackFormat.Unknown)
		{
			throw new InvalidOperationException("cannot import " + Path.GetFileName(zipPath) + ": " + string.Join("; ", info.Notes));
		}
		ValidateMetadataToken(info.McVersion, "Minecraft version");
		ValidateMetadataToken(info.LoaderVersion, "loader version");
		ValidateMetadataToken(info.VersionId, "version ID");
		string? effectiveKey = curseForgeKey;
		if (string.IsNullOrWhiteSpace(effectiveKey))
		{
			effectiveKey = LauncherSettings.Load(paths).CurseForgeKey;
		}
		string id = MakeId(instanceId ?? info.Name);
		InstanceStore store = new InstanceStore(paths);
		int suffix = 2;
		string baseId = id;
		while (store.Find(id) != null || Directory.Exists(store.FolderFor(id)) || File.Exists(store.FolderFor(id)))
		{
			id = baseId + "-" + suffix++;
		}
		string name = (string.IsNullOrWhiteSpace(displayName) ? (string.IsNullOrWhiteSpace(info.Name) ? id : info.Name) : displayName);
		string text = store.FolderFor(id);
		string staging = Path.Combine(paths.Instances, ".nova-import-" + Guid.NewGuid().ToString("N"));
		string gameDir = Path.Combine(staging, "game");
		string finalGameDir = Path.Combine(text, "game");
		bool published = false;
		SafePath.RejectLinks(paths.Instances, text);
		SafePath.RejectLinks(paths.Instances, staging);
		try
		{
		PackImportResult result = new PackImportResult
		{
			InstanceId = id,
			Name = name,
			GameDirectory = finalGameDir
		};
		log?.Report("importing " + info.FormatName + ": " + name);
		using (ZipArchive zip = ZipFile.OpenRead(zipPath))
		{
			limits.ValidateArchive(zip);
			var budget = new ArchiveBudget(limits);
			switch (info.Format)
			{
			case PackFormat.Modrinth:
				await ImportModrinthAsync(zip, staging, gameDir, result, log, ct, limits, budget, progress).ConfigureAwait(continueOnCapturedContext: false);
				break;
			case PackFormat.CurseForge:
				await ImportCurseForgeAsync(zip, staging, gameDir, effectiveKey, result, log, ct, limits, budget, progress).ConfigureAwait(continueOnCapturedContext: false);
				break;
			default:
				ExtractFolder(zip, gameDir, "", result, limits, budget, ct, new string[1] { "nova-pack.json" });
				break;
			}
		}
		Directory.CreateDirectory(gameDir);
		string resolvedVersionId = await EnsurePackLoaderAsync(info, paths, result, log, ct).ConfigureAwait(continueOnCapturedContext: false);
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
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				result.Notes.Add("Java setup failed: " + ex.Message);
				result.SetupFailed = true;
			}
		}
		AccountStore accountStore = new AccountStore(paths);
		LauncherSettings settings = LauncherSettings.Load(paths);
		string activePlayer = accountStore.Active(settings).Name;
		InstanceConfig instanceConfig = new InstanceConfig
		{
			Id = id,
			Name = name,
			GameDirectory = finalGameDir,
			VersionId = resolvedVersionId,
			JavaPath = java?.Path,
			MemoryMb = LaunchBuilder.RecommendMemoryMb(num),
			PlayerName = (string.IsNullOrWhiteSpace(activePlayer) ? "Player" : activePlayer),
			CreatedUtc = DateTime.UtcNow
		};
		ct.ThrowIfCancellationRequested();
		SafePath.RejectLinks(paths.Instances, text);
		Directory.Move(staging, text);
		published = true;
		store.Save(instanceConfig);
		result.VersionId = instanceConfig.VersionId;
		result.Notes.Add($"instance '{id}' created with {num} mod(s), {instanceConfig.MemoryMb} MB heap");
		log?.Report(result.Summary);
		return result;
		}
		catch
		{
			// Only remove the unique directory created by this import, never an existing instance.
			if (Directory.Exists(staging)) Directory.Delete(staging, true);
			if (published && Directory.Exists(text)) Directory.Delete(text, true);
			throw;
		}
	}

	private static async Task<string> EnsurePackLoaderAsync(PackInfo info, LauncherPaths paths, PackImportResult result, IProgress<string>? log, CancellationToken ct)
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
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
		catch (Exception ex)
		{
			result.SetupFailed = true;
			result.Notes.Add("Loader setup failed: " + ex.Message);
			log?.Report("loader setup failed: " + ex.Message);
		}
		return built;
	}

	private static async Task ImportModrinthAsync(ZipArchive zip, string instanceFolder, string gameDir, PackImportResult result, IProgress<string>? log, CancellationToken ct, ArchiveLimits limits, ArchiveBudget budget, IProgress<PackImportProgress>? progress)
	{
		ModrinthIndex modrinthIndex = PackInspector.ReadJson<ModrinthIndex>(zip.GetEntry("modrinth.index.json")!, limits.MaxManifestBytes) ?? new ModrinthIndex();
		List<ModrinthFile> files = (modrinthIndex.Files ?? new List<ModrinthFile>()).Where((ModrinthFile f) => f.IsClientFile).ToList();
		// Validate every manifest and override path and all declared sizes before downloading.
		SafeArchive.Validate(zip, gameDir, "overrides/", limits);
		SafeArchive.Validate(zip, gameDir, "client-overrides/", limits);
		if ((long)files.Count + zip.Entries.Count > limits.MaxEntries) throw new InvalidDataException("Pack has too many files.");
		long declared = zip.Entries.Sum(e => e.Length);
		var destinations = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		foreach (ModrinthFile file in files)
		{
			string destination = SafePath.ResolveWithin(gameDir, file.Path);
			if (!destinations.Add(destination)) throw new InvalidDataException("Duplicate Modrinth destination.");
			if (file.FileSize < 0 || file.FileSize > limits.MaxFileBytes || file.FileSize > limits.MaxTotalBytes - declared)
				throw new InvalidDataException("Modrinth pack exceeds size limits.");
			declared += file.FileSize;
			budget.BeginFile();
			budget.AddBytes(file.FileSize, file.FileSize);
		}
		log?.Report($"{files.Count} file(s) listed by the pack");
		using Downloader downloader = new Downloader(8);
		int done = 0;
		object sync = new object();
		long lastProgressTick = 0;
		downloader.OnBytesRead = total =>
		{
			long now = Environment.TickCount64;
			long previous = Interlocked.Read(ref lastProgressTick);
			if (now - previous < 500 || Interlocked.CompareExchange(ref lastProgressTick, now, previous) != previous) return;
			lock (sync) progress?.Report(new PackImportProgress(done, files.Count, result.FilesFailed, total));
		};
		var missingDetails = new Dictionary<string, string>();
		IEnumerable<Task> tasks = files.Select(async delegate(ModrinthFile file)
		{
			ct.ThrowIfCancellationRequested();
			string target = SafePath.ResolveWithin(gameDir, file.Path);
			FetchResult fetchResult = FetchResult.Failed;
			string error = "no download source";
			foreach (string url in (file.Downloads ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
			{
				fetchResult = await downloader.EnsureAsync(target, url, file.Sha1, ct, maxBytes: file.FileSize, onFailure: message => error = message).ConfigureAwait(false);
				if (fetchResult == FetchResult.Downloaded || fetchResult == FetchResult.AlreadyValid) break;
			}
			lock (sync)
			{
				int current = ++done;
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
					missingDetails[file.Path] = error;
					log?.Report("download failed: " + file.Path + " (" + error + ")");
					break;
				}
				progress?.Report(new PackImportProgress(current, files.Count, result.FilesFailed, Interlocked.Read(ref downloader.BytesDownloaded)));
				if (current % 10 == 0 || current == files.Count)
					log?.Report($"processed {current}/{files.Count} ({result.FilesFailed} failed; {downloader.BytesDownloaded / 1024 / 1024} MB transferred)");
			}
		});
		await Task.WhenAll(tasks).ConfigureAwait(continueOnCapturedContext: false);
		// Retry failures once more without competing transfers: transient TLS and rate limits
		// can affect only the final large files while the pack is downloading in parallel.
		var failedFiles = files.Where(f => missingDetails.ContainsKey(f.Path) && f.Downloads?.Any(u => !string.IsNullOrWhiteSpace(u)) == true).ToList();
		if (failedFiles.Count > 0) log?.Report($"retrying {failedFiles.Count} failed file(s) one at a time");
		foreach (ModrinthFile file in failedFiles)
		{
			ct.ThrowIfCancellationRequested();
			string target = SafePath.ResolveWithin(gameDir, file.Path);
			string error = missingDetails[file.Path];
			FetchResult retried = FetchResult.Failed;
			foreach (string url in file.Downloads!.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct())
			{
				retried = await downloader.EnsureAsync(target, url, file.Sha1, ct, maxBytes: file.FileSize, onFailure: message => error = message).ConfigureAwait(false);
				if (retried == FetchResult.Downloaded || retried == FetchResult.AlreadyValid) break;
			}
			if (retried == FetchResult.Downloaded || retried == FetchResult.AlreadyValid)
			{
				result.FilesFailed--;
				if (retried == FetchResult.Downloaded) result.FilesWritten++; else result.FilesSkipped++;
				result.Missing.Remove(file.Path);
				missingDetails.Remove(file.Path);
				log?.Report("retry succeeded: " + file.Path);
			}
			else { missingDetails[file.Path] = error; log?.Report("retry failed: " + file.Path + " (" + error + ")"); }
			progress?.Report(new PackImportProgress(done, files.Count, result.FilesFailed, Interlocked.Read(ref downloader.BytesDownloaded)));
		}
		ExtractFolder(zip, gameDir, "overrides/", result, limits, budget, ct);
		ExtractFolder(zip, gameDir, "client-overrides/", result, limits, budget, ct);
		if (result.Missing.Count > 0)
		{
			Directory.CreateDirectory(instanceFolder);
			File.WriteAllLines(Path.Combine(instanceFolder, "modrinth-missing.txt"), new[] { "# Files missing from this Modrinth pack. Retry import or download these files manually:" }.Concat(missingDetails.Select(kv => kv.Key + " — " + kv.Value)));
			result.Notes.Add($"{result.Missing.Count} missing file(s) listed in modrinth-missing.txt");
			log?.Report($"{result.Missing.Count} file(s) could not be downloaded: " + string.Join(", ", result.Missing.Take(5)));
		}
	}

	private static async Task ImportCurseForgeAsync(ZipArchive zip, string instanceFolder, string gameDir, string? curseKey, PackImportResult result, IProgress<string>? log, CancellationToken ct, ArchiveLimits limits, ArchiveBudget budget, IProgress<PackImportProgress>? progress)
	{
		CurseManifest? manifest = PackInspector.ReadJson<CurseManifest>(zip.GetEntry("manifest.json")!, limits.MaxManifestBytes);
		ExtractFolder(zip, gameDir, "overrides/", result, limits, budget, ct);
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
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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
				catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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
		long lastProgressTick = 0;
		downloader.OnBytesRead = total =>
		{
			long now = Environment.TickCount64;
			long previous = Interlocked.Read(ref lastProgressTick);
			if (now - previous < 500 || Interlocked.CompareExchange(ref lastProgressTick, now, previous) != previous) return;
			lock (resLock) progress?.Report(new PackImportProgress(done, required.Count, result.FilesFailed, total));
		};
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
					progress?.Report(new PackImportProgress(++done, required.Count, result.FilesFailed, Interlocked.Read(ref downloader.BytesDownloaded)));
				}
				return;
			}
			if (info2.FileName.Contains('/') || info2.FileName.Contains('\\')) throw new InvalidDataException("Invalid CurseForge filename.");
			string dest = SafePath.ResolveWithin(modsDir, info2.FileName);
			FetchResult fr = await downloader.EnsureAsync(dest, info2.Url, info2.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false);
			lock (resLock)
			{
				int curr = ++done;
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
				progress?.Report(new PackImportProgress(curr, required.Count, result.FilesFailed, Interlocked.Read(ref downloader.BytesDownloaded)));
				if (curr % 10 == 0 || curr == required.Count)
					log?.Report($"processed {curr}/{required.Count} CurseForge mods ({result.FilesFailed} failed; {downloader.BytesDownloaded / 1024 / 1024} MB transferred)");
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

	private static void ExtractFolder(ZipArchive zip, string gameDir, string prefix, PackImportResult result, ArchiveLimits limits, ArchiveBudget budget, CancellationToken ct, string[]? skip = null)
	{
		result.FilesWritten += SafeArchive.Extract(zip, gameDir, prefix, limits, budget, skip, ct);
	}

	private static void ValidateMetadataToken(string? value, string label)
	{
		if (string.IsNullOrEmpty(value)) return;
		if (value.Length > 128 || value == "." || value == ".." ||
		    !value.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_' || c == '+'))
			throw new InvalidDataException("Unsafe " + label + " in pack metadata.");
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
