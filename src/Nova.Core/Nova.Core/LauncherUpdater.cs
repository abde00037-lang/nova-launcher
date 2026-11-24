using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed record LauncherUpdateInfo(
	string CurrentVersion,
	string LatestVersion,
	string TagName,
	string Title,
	string ReleaseNotes,
	string HtmlUrl,
	string? DownloadUrl,
	string? AssetName,
	long AssetSizeBytes,
	DateTimeOffset? PublishedAt,
	string? ChecksumUrl = null);

public static class LauncherUpdater
{
	public const string DefaultRepo = "abde00037-lang/nova-launcher";

	public const string ExpectedAssetPrefix = "nova-bin-win-x64";

	public static string CurrentVersion
	{
		get
		{
			Assembly asm = typeof(LauncherUpdater).Assembly;
			string? infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (!string.IsNullOrWhiteSpace(infoVer))
			{
				int plusIdx = infoVer.IndexOf('+');
				return (plusIdx > 0) ? infoVer.Substring(0, plusIdx) : infoVer;
			}
			Version? v = asm.GetName().Version;
			return (v != null) ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" : "2.0.0";
		}
	}

	public static Version NormalizeVersion(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return new Version(0, 0, 0);
		}
		string cleaned = raw.Trim();
		if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
		{
			cleaned = cleaned.Substring(1);
		}
		int dash = cleaned.IndexOfAny(new char[] { '-', '+' });
		if (dash > 0)
		{
			cleaned = cleaned.Substring(0, dash);
		}
		string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
		int major = (parts.Length > 0 && int.TryParse(parts[0], out var m0)) ? m0 : 0;
		int minor = (parts.Length > 1 && int.TryParse(parts[1], out var m1)) ? m1 : 0;
		int patch = (parts.Length > 2 && int.TryParse(parts[2], out var m2)) ? m2 : 0;
		int rev = (parts.Length > 3 && int.TryParse(parts[3], out var m3)) ? m3 : 0;
		return (parts.Length > 3) ? new Version(major, minor, patch, rev) : new Version(major, minor, patch);
	}

	public static bool IsNewer(string candidateTag, string currentVersion)
	{
		return NormalizeVersion(candidateTag) > NormalizeVersion(currentVersion);
	}

	public static async Task<LauncherUpdateInfo?> CheckForUpdateAsync(string? repoSlug = null, bool includeSameOrOlder = false, CancellationToken ct = default(CancellationToken))
	{
		string repo = (string.IsNullOrWhiteSpace(repoSlug) ? DefaultRepo : repoSlug.Trim().Trim('/'));
		string apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";

		using HttpClient http = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(18.0)
		};
		http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NovaWorkbench/" + CurrentVersion);
		http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

		using HttpResponseMessage resp = await http.GetAsync(apiUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		if ((int)resp.StatusCode == 404)
		{
			return null;
		}
		resp.EnsureSuccessStatusCode();

		using Stream stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		using JsonDocument doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(continueOnCapturedContext: false);
		JsonElement root = doc.RootElement;

		string tag = (root.TryGetProperty("tag_name", out var tagEl) ? (tagEl.GetString() ?? "") : "");
		if (string.IsNullOrWhiteSpace(tag))
		{
			return null;
		}
		string current = CurrentVersion;
		string latestClean = NormalizeVersion(tag).ToString(3);
		if (!includeSameOrOlder && !IsNewer(tag, current))
		{
			return null;
		}

		string title = (root.TryGetProperty("name", out var nameEl) ? (nameEl.GetString() ?? tag) : tag);
		string notes = (root.TryGetProperty("body", out var bodyEl) ? (bodyEl.GetString() ?? "") : "");
		string htmlUrl = (root.TryGetProperty("html_url", out var htmlEl) ? (htmlEl.GetString() ?? $"https://github.com/{repo}/releases") : $"https://github.com/{repo}/releases");
		DateTimeOffset? published = null;
		if (root.TryGetProperty("published_at", out var pubEl) && pubEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(pubEl.GetString(), out var parsedDate))
		{
			published = parsedDate;
		}

		string? downloadUrl = null;
		string? assetName = null;
		long assetSize = 0L;
		string? checksumUrl = null;

		if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement asset in assetsEl.EnumerateArray())
			{
				string aName = (asset.TryGetProperty("name", out var an) ? (an.GetString() ?? "") : "");
				string aUrl = (asset.TryGetProperty("browser_download_url", out var au) ? (au.GetString() ?? "") : "");
				long aSize = (asset.TryGetProperty("size", out var az) ? az.GetInt64() : 0L);
				if (aName.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(aUrl))
				{
					checksumUrl = aUrl;
				}
				if (aName.Equals(ExpectedAssetPrefix + ".zip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(aUrl))
				{
					downloadUrl = aUrl;
					assetName = aName;
					assetSize = aSize;
				}
			}
		}

		return new LauncherUpdateInfo(current, latestClean, tag, title, notes, htmlUrl, downloadUrl, assetName, assetSize, published, checksumUrl);
	}

	/// <summary>
	/// Downloads and stages the update into cache\launcher-update\staged\.
	/// Returns the staged directory containing Nova.dll / Nova.exe ready to swap into bin\.
	/// Never touches instances\, saves\, mods\, versions\, accounts\, or settings.json!
	/// </summary>
	public static async Task<string> StageUpdateAsync(LauncherPaths paths, LauncherUpdateInfo update, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(update.DownloadUrl))
		{
			throw new InvalidOperationException("This release does not attach a portable update .zip asset.");
		}

		if (string.IsNullOrWhiteSpace(update.ChecksumUrl) || string.IsNullOrWhiteSpace(update.AssetName))
		{
			throw new InvalidDataException("This release has no SHA256SUMS.txt or matching update asset; refusing to update.");
		}
		using HttpClient checksumClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		checksumClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NovaWorkbench/" + CurrentVersion);
		string sums = await checksumClient.GetStringAsync(update.ChecksumUrl, ct).ConfigureAwait(false);
		string expectedHash = ExpectedSha256(sums, update.AssetName);

		string workRoot = Path.Combine(paths.Cache, "launcher-update");
		string zipPath = Path.Combine(workRoot, $"nova-update-{update.LatestVersion}.zip");
		string extractDir = Path.Combine(workRoot, "extracted");
		string stagedDir = Path.Combine(workRoot, "staged");

		if (Directory.Exists(extractDir))
		{
			Directory.Delete(extractDir, recursive: true);
		}
		if (Directory.Exists(stagedDir))
		{
			Directory.Delete(stagedDir, recursive: true);
		}
		Directory.CreateDirectory(workRoot);

		if (File.Exists(zipPath))
		{
			File.Delete(zipPath);
		}
		log?.Report($"downloading {update.AssetName ?? "update package"} ({Math.Max(1.0, update.AssetSizeBytes / 1024.0):0} KB)…");
		using (Downloader downloader = new Downloader(2))
		{
			FetchResult fetched = await downloader.EnsureAsync(zipPath, update.DownloadUrl, null, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (fetched == FetchResult.Failed || fetched == FetchResult.HashMismatch || !File.Exists(zipPath))
			{
				throw new IOException("Failed to download update archive from GitHub Releases. " + (downloader.LastError ?? "Check your connection and retry."));
			}
		}

		log?.Report("verifying update SHA-256…");
		await using (FileStream file = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
		{
			string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
			if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Update SHA-256 mismatch; no files were installed.");
			}
		}
		ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

		// Support archives where files are either directly at root or inside a bin\ subfolder
		string binCandidate = Path.Combine(extractDir, "bin");
		string sourceBin = ((Directory.Exists(binCandidate) && File.Exists(Path.Combine(binCandidate, "Nova.Core.dll"))) ? binCandidate : extractDir);

		if (!File.Exists(Path.Combine(sourceBin, "Nova.Core.dll")))
		{
			string? nestedCore = Directory.GetFiles(extractDir, "Nova.Core.dll", SearchOption.AllDirectories).FirstOrDefault();
			if (nestedCore != null)
			{
				sourceBin = Path.GetDirectoryName(nestedCore)!;
			}
		}

		if (!File.Exists(Path.Combine(sourceBin, "Nova.Core.dll")))
		{
			throw new InvalidDataException("Downloaded update archive does not contain Nova.Core.dll.");
		}

		Directory.Move(sourceBin, stagedDir);
		log?.Report($"staged v{update.LatestVersion} cleanly ({Directory.GetFiles(stagedDir).Length} binary files)");
		return stagedDir;
	}

	/// <summary>
	/// For CLI use (or when Nova.exe is not locking files): applies staged files directly or spawns a detached PowerShell swapper.
	/// When <paramref name="relaunchGui"/> is true, waits for <paramref name="waitForPid"/> to exit, copies staged files into <paramref name="targetBinDir"/>, and relaunches Nova.exe.
	/// </summary>
	public static void LaunchDetachedSwapper(string stagedDir, string targetBinDir, int waitForPid, bool relaunchGui, string? logFilePath = null)
	{
		Directory.CreateDirectory(targetBinDir);
		string scriptPath = Path.Combine(Path.GetDirectoryName(stagedDir) ?? Path.GetTempPath(), "apply-nova-update.ps1");
		string novaExe = Path.Combine(targetBinDir, "Nova.exe");
		string logLine = (string.IsNullOrWhiteSpace(logFilePath) ? "" : $"Add-Content -Path '{EscapePs(logFilePath)}' -Value \"$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  updater applied staged files to {EscapePs(targetBinDir)}\" -ErrorAction SilentlyContinue");

		StringBuilder sb = new StringBuilder();
		sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
		if (waitForPid > 0)
		{
			sb.AppendLine($"for ($i = 0; $i -lt 60; $i++) {{");
			sb.AppendLine($"    $p = Get-Process -Id {waitForPid} -ErrorAction SilentlyContinue");
			sb.AppendLine($"    if ($null -eq $p) {{ break }}");
			sb.AppendLine($"    Start-Sleep -Milliseconds 200");
			sb.AppendLine($"}}");
			sb.AppendLine($"Stop-Process -Id {waitForPid} -Force -ErrorAction SilentlyContinue");
			sb.AppendLine("Start-Sleep -Milliseconds 350");
		}
		sb.AppendLine($"Copy-Item -Path '{EscapePs(stagedDir)}\\*' -Destination '{EscapePs(targetBinDir)}' -Recurse -Force");
		if (!string.IsNullOrEmpty(logLine))
		{
			sb.AppendLine(logLine);
		}
		if (relaunchGui)
		{
			sb.AppendLine($"if (Test-Path '{EscapePs(novaExe)}') {{ Start-Process -FilePath '{EscapePs(novaExe)}' -WorkingDirectory '{EscapePs(targetBinDir)}' }}");
		}
		File.WriteAllText(scriptPath, sb.ToString(), Encoding.UTF8);

		Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetTempPath()
		});
	}

	public static string ExpectedSha256(string sums, string assetName)
	{
		string? match = null;
		foreach (string raw in sums.Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length < 67 || !line.Substring(0, 64).All(Uri.IsHexDigit) || !char.IsWhiteSpace(line[64])) continue;
			string name = line.Substring(65).TrimStart().TrimStart('*');
			if (name.Equals(assetName, StringComparison.Ordinal))
			{
				if (match != null) throw new InvalidDataException("Duplicate update checksum entries.");
				match = line.Substring(0, 64);
			}
		}
		return match ?? throw new InvalidDataException("The update asset has no valid SHA-256 entry.");
	}

	private static string EscapePs(string s)
	{
		return s.Replace("'", "''");
	}
}
