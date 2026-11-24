using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed record ModUpdateCandidate(
	string CurrentFileName,
	bool WasEnabled,
	string ProjectId,
	string LatestVersionNumber,
	string NewFileName,
	string DownloadUrl,
	string? Sha1,
	long Size);

public static class ModUpdater
{
	private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.All,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(45.0)
	};

	static ModUpdater()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/0.2 (minecraft modpack launcher)");
	}

	public static async Task<Dictionary<string, ModUpdateCandidate>> CheckUpdatesAsync(
		string modsDirectory,
		string mcVersion,
		string loader,
		IProgress<string>? log = null,
		CancellationToken ct = default(CancellationToken))
	{
		Dictionary<string, ModUpdateCandidate> results = new Dictionary<string, ModUpdateCandidate>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(modsDirectory) || string.IsNullOrWhiteSpace(mcVersion))
		{
			return results;
		}
		string effLoader = (string.IsNullOrWhiteSpace(loader) || loader == "vanilla") ? "forge" : loader.ToLowerInvariant();
		List<string> files = Directory.EnumerateFiles(modsDirectory)
			.Where((string f) => ModManager.IsModFile(Path.GetFileName(f)))
			.ToList();
		if (files.Count == 0)
		{
			return results;
		}
		log?.Report($"computing SHA-1 for {files.Count} mod file(s)…");
		Dictionary<string, (string FileName, bool Enabled)> hashToFile = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
		foreach (string fullPath in files)
		{
			ct.ThrowIfCancellationRequested();
			string fn = Path.GetFileName(fullPath);
			bool enabled = !fn.EndsWith(ModManager.DisabledSuffix, StringComparison.OrdinalIgnoreCase);
			string? sha1 = await ComputeSha1Async(fullPath, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!string.IsNullOrEmpty(sha1))
			{
				hashToFile[sha1] = (fn, enabled);
			}
		}
		if (hashToFile.Count == 0)
		{
			return results;
		}
		List<string> allHashes = hashToFile.Keys.ToList();
		for (int offset = 0; offset < allHashes.Count; offset += 150)
		{
			List<string> chunk = allHashes.Skip(offset).Take(150).ToList();
			string body = JsonSerializer.Serialize(new
			{
				hashes = chunk,
				algorithm = "sha1",
				loaders = new string[1] { effLoader },
				game_versions = new string[1] { mcVersion }
			});
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://api.modrinth.com/v2/version_files/update")
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			};
			using HttpResponseMessage resp = await Http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!resp.IsSuccessStatusCode)
			{
				continue;
			}
			using JsonDocument doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false), default, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
			{
				string oldSha = prop.Name;
				if (!hashToFile.TryGetValue(oldSha, out var localInfo))
				{
					continue;
				}
				JsonElement verObj = prop.Value;
				if (!verObj.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Array || filesEl.GetArrayLength() == 0)
				{
					continue;
				}
				List<JsonElement> fileList = filesEl.EnumerateArray().ToList();
				JsonElement primary = fileList.FirstOrDefault((JsonElement f) => f.TryGetProperty("primary", out var pEl) && pEl.ValueKind == JsonValueKind.True);
				JsonElement chosen = ((primary.ValueKind != JsonValueKind.Undefined) ? primary : fileList[0]);
				string? newSha = null;
				if (chosen.TryGetProperty("hashes", out var hEl) && hEl.TryGetProperty("sha1", out var s1El))
				{
					newSha = s1El.GetString();
				}
				if (!string.IsNullOrEmpty(newSha) && string.Equals(newSha, oldSha, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string newFn = (chosen.TryGetProperty("filename", out var fnEl) ? (fnEl.GetString() ?? "") : "");
				string url = (chosen.TryGetProperty("url", out var urlEl) ? (urlEl.GetString() ?? "") : "");
				long size = ((chosen.TryGetProperty("size", out var szEl) && szEl.ValueKind == JsonValueKind.Number) ? szEl.GetInt64() : 0);
				string verNum = (verObj.TryGetProperty("version_number", out var vnEl) ? (vnEl.GetString() ?? "latest") : "latest");
				string projId = (verObj.TryGetProperty("project_id", out var pidEl) ? (pidEl.GetString() ?? "") : "");
				if (string.IsNullOrWhiteSpace(newFn) || string.IsNullOrWhiteSpace(url))
				{
					continue;
				}
				string baseOld = ModManager.DisplayName(localInfo.FileName);
				if (string.Equals(baseOld, newFn, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(newSha))
				{
					continue;
				}
				results[localInfo.FileName] = new ModUpdateCandidate(
					localInfo.FileName,
					localInfo.Enabled,
					projId,
					verNum,
					newFn,
					url,
					newSha,
					size);
			}
		}
		log?.Report(results.Count == 0
			? "all Modrinth-tracked mods are up to date"
			: $"{results.Count} mod update(s) available for {mcVersion} ({effLoader})");
		return results;
	}

	public static async Task<string> ApplyUpdateAsync(string modsDirectory, ModUpdateCandidate candidate, CancellationToken ct = default(CancellationToken))
	{
		Directory.CreateDirectory(modsDirectory);
		string finalName = (candidate.WasEnabled ? candidate.NewFileName : (candidate.NewFileName + ModManager.DisabledSuffix));
		string tempTarget = Path.Combine(modsDirectory, finalName + ".updating");
		string finalTarget = Path.Combine(modsDirectory, finalName);
		using (Downloader downloader = new Downloader(2))
		{
			FetchResult res = await downloader.EnsureAsync(tempTarget, candidate.DownloadUrl, candidate.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (res == FetchResult.Failed)
			{
				try
				{
					if (File.Exists(tempTarget))
					{
						File.Delete(tempTarget);
					}
				}
				catch
				{
				}
				throw new IOException("failed to download " + candidate.NewFileName);
			}
		}
		string oldPath = Path.Combine(modsDirectory, candidate.CurrentFileName);
		File.Move(tempTarget, finalTarget, overwrite: true);
		if (!string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(finalTarget), StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
		{
			try
			{
				File.Delete(oldPath);
			}
			catch
			{
			}
		}
		return finalName;
	}

	private static async Task<string?> ComputeSha1Async(string path, CancellationToken ct)
	{
		try
		{
			await using FileStream fs = File.OpenRead(path);
			byte[] hash = await SHA1.HashDataAsync(fs, ct).ConfigureAwait(continueOnCapturedContext: false);
			return Convert.ToHexString(hash).ToLowerInvariant();
		}
		catch
		{
			return null;
		}
	}
}
