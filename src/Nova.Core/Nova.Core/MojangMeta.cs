using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class MojangMeta
{
	public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

	private const string ManifestMirror = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

	private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.All,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(60.0)
	};

	public static async Task<VersionManifest> GetManifestAsync(CancellationToken ct = default(CancellationToken))
	{
		return JsonSerializer.Deserialize<VersionManifest>(await FetchTextAsync(ManifestUrl, ct).ConfigureAwait(continueOnCapturedContext: false)) ?? throw new InvalidDataException("Empty version manifest");
	}

	private static async Task<string> FetchTextAsync(string url, CancellationToken ct)
	{
		List<string> list = new List<string> { url };
		if (url == ManifestUrl)
		{
			list.Add(ManifestMirror);
		}
		else if (url.StartsWith("https://piston-meta.mojang.com/", StringComparison.OrdinalIgnoreCase))
		{
			int length = "https://piston-meta.mojang.com/".Length;
			list.Add("https://launchermeta.mojang.com/" + url.Substring(length, url.Length - length));
		}
		Exception? last = null;
		foreach (string candidate in list)
		{
			for (int attempt = 1; attempt <= 3; attempt++)
			{
				try
				{
					return await Http.GetStringAsync(candidate, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex) when (attempt < 3)
				{
					last = ex;
					await Task.Delay(700 * attempt, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex2)
				{
					last = ex2;
					break;
				}
			}
		}
		throw last ?? new IOException("could not reach Mojang");
	}

	public static async Task<string> InstallAsync(string versionId, LauncherPaths paths, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		progress?.Report(new DownloadProgress("Manifest", 0, 100, 0L, "fetching Mojang manifest…"));
		ManifestVersion entry = (await GetManifestAsync(ct).ConfigureAwait(continueOnCapturedContext: false)).Versions?.FirstOrDefault((ManifestVersion v) => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("version '" + versionId + "' is not in Mojang's manifest");
		string directory = paths.VersionDir(entry.Id);
		Directory.CreateDirectory(directory);
		string jsonPath = paths.VersionJson(entry.Id);
		progress?.Report(new DownloadProgress("Metadata", 5, 100, 0L, "reading " + entry.Id + ".json…"));
		string json = await FetchTextAsync(entry.Url, ct).ConfigureAwait(continueOnCapturedContext: false);
		await File.WriteAllTextAsync(jsonPath, json, ct).ConfigureAwait(continueOnCapturedContext: false);
		log?.Report("version json: " + jsonPath);
		VersionJson? versionJson = null;
		try
		{
			versionJson = JsonSerializer.Deserialize<VersionJson>(json);
		}
		catch
		{
		}
		DownloadEntry? client = versionJson?.Downloads?.Client;
		string? text = client?.Url;
		if (text != null && text.Length > 0 && client != null)
		{
			string targetPath = Path.Combine(directory, entry.Id + ".jar");
			using Downloader downloader = new Downloader(4);
			long expectedTotal = Math.Max(1L, client.Size);
			long lastTick = 0L;
			FetchResult value = await downloader.EnsureAsync(targetPath, text, client.Sha1, ct, delegate(long doneBytes, long contentLen)
			{
				long now = Environment.TickCount64;
				if (now - Interlocked.Read(ref lastTick) >= 80 || doneBytes >= contentLen)
				{
					Interlocked.Exchange(ref lastTick, now);
					long denom = ((contentLen > 0) ? contentLen : expectedTotal);
					int pct = (int)Math.Clamp(doneBytes * 100 / Math.Max(1L, denom), 0L, 100L);
					progress?.Report(new DownloadProgress("Client JAR", pct, 100, doneBytes, $"client.jar · {(double)doneBytes / 1048576.0:F1} / {(double)denom / 1048576.0:F1} MB"));
				}
			}).ConfigureAwait(continueOnCapturedContext: false);
			if (value != FetchResult.Downloaded && value != FetchResult.AlreadyValid)
				throw new IOException("Minecraft client download failed: " + value + (string.IsNullOrWhiteSpace(downloader.LastError) ? "" : " — " + downloader.LastError));
			progress?.Report(new DownloadProgress("Client JAR", 100, 100, downloader.BytesDownloaded, $"client.jar ready ({(double)client.Size / 1048576.0:F1} MB)"));
			log?.Report($"client jar: {value} ({(double)client.Size / 1048576.0:F1} MB)");
		}
		else
		{
			log?.Report("no client jar in this version json (modded versions inherit it from the parent)");
		}
		return jsonPath;
	}
}
