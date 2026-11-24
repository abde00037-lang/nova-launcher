using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class FabricInstaller
{
	private const string MetaRoot = "https://meta.fabricmc.net/v2";

	private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.All,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(45.0)
	};

	static FabricInstaller()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/0.2 (minecraft modpack launcher)");
	}

	public static async Task<string?> GetLatestLoaderVersionAsync(string mcVersion, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			string uri = $"{MetaRoot}/versions/loader/{Uri.EscapeDataString(mcVersion)}";
			using Stream stream = await Http.GetStreamAsync(uri, ct).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
			{
				return null;
			}
			string? firstAny = null;
			foreach (JsonElement item in doc.RootElement.EnumerateArray())
			{
				if (item.TryGetProperty("loader", out var loaderEl) && loaderEl.ValueKind == JsonValueKind.Object)
				{
					string? ver = (loaderEl.TryGetProperty("version", out var vEl) ? vEl.GetString() : null);
					if (string.IsNullOrWhiteSpace(ver))
					{
						continue;
					}
					firstAny ??= ver;
					if (loaderEl.TryGetProperty("stable", out var sEl) && sEl.ValueKind == JsonValueKind.True)
					{
						return ver;
					}
				}
			}
			return firstAny;
		}
		catch
		{
			return null;
		}
	}

	public static bool IsInstalled(string mcVersion, string loaderVersion, LauncherPaths paths)
	{
		string id = $"fabric-loader-{loaderVersion}-{mcVersion}";
		return File.Exists(paths.VersionJson(id));
	}

	public static async Task<string> InstallAsync(string mcVersion, string? loaderVersion, LauncherPaths paths, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		if (string.IsNullOrWhiteSpace(mcVersion))
		{
			throw new ArgumentException("minecraft version is required");
		}
		paths.EnsureCreated();
		progress?.Report(new DownloadProgress("Fabric", 5, 100, 0L, $"resolving Fabric loader for {mcVersion}…"));
		string? resolvedLoader = loaderVersion;
		if (string.IsNullOrWhiteSpace(resolvedLoader) || string.Equals(resolvedLoader, "latest", StringComparison.OrdinalIgnoreCase) || string.Equals(resolvedLoader, "recommended", StringComparison.OrdinalIgnoreCase))
		{
			resolvedLoader = await GetLatestLoaderVersionAsync(mcVersion, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (string.IsNullOrWhiteSpace(resolvedLoader))
		{
			throw new InvalidOperationException("Fabric does not publish a loader for Minecraft " + mcVersion);
		}
		string versionId = $"fabric-loader-{resolvedLoader}-{mcVersion}";
		if (IsInstalled(mcVersion, resolvedLoader, paths))
		{
			log?.Report("Fabric " + versionId + " is already installed");
			progress?.Report(new DownloadProgress("Fabric", 100, 100, 0L, versionId + " already installed"));
			return versionId;
		}
		log?.Report("vanilla " + mcVersion + ": checking");
		await MojangMeta.InstallAsync(mcVersion, paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		progress?.Report(new DownloadProgress("Fabric Profile", 70, 100, 0L, $"fetching {versionId}.json…"));
		string profileUrl = $"{MetaRoot}/versions/loader/{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(resolvedLoader)}/profile/json";
		string json = await Http.GetStringAsync(profileUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		string dir = paths.VersionDir(versionId);
		Directory.CreateDirectory(dir);
		string jsonPath = paths.VersionJson(versionId);
		await File.WriteAllTextAsync(jsonPath, json, ct).ConfigureAwait(continueOnCapturedContext: false);
		log?.Report("Fabric " + versionId + " profile installed");
		progress?.Report(new DownloadProgress("Fabric", 100, 100, 0L, versionId + " installed"));
		return versionId;
	}
}
