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
using System.Xml.Linq;

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
		string repo = string.IsNullOrWhiteSpace(repoSlug) ? DefaultRepo : repoSlug.Trim().Trim('/');
		try
		{
			// GitHub's anonymous REST API is rate-limited per public IP. The release Atom feed
			// is a lightweight public endpoint and avoids making startup checks consume that quota.
			return await CheckLatestFromAtomAsync(repo, includeSameOrOlder, ct).ConfigureAwait(false);
		}
		catch (HttpRequestException) when (!ct.IsCancellationRequested)
		{
			// Retain the original JSON API as a fallback if GitHub's release feed is unavailable.
			return await CheckLatestFromApiAsync(repo, includeSameOrOlder, ct).ConfigureAwait(false);
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			return await CheckLatestFromApiAsync(repo, includeSameOrOlder, ct).ConfigureAwait(false);
		}
		catch (System.Xml.XmlException) when (!ct.IsCancellationRequested)
		{
			return await CheckLatestFromApiAsync(repo, includeSameOrOlder, ct).ConfigureAwait(false);
		}
	}

	private static async Task<LauncherUpdateInfo?> CheckLatestFromAtomAsync(string repo, bool includeSameOrOlder, CancellationToken ct)
	{
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(18.0) };
		http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NovaWorkbench/" + CurrentVersion);
		http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/atom+xml, application/xml, text/xml");
		using HttpResponseMessage response = await http.GetAsync($"https://github.com/{repo}/releases.atom", ct).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		XDocument feed = await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);
		XNamespace atom = "http://www.w3.org/2005/Atom";
		XElement? entry = feed.Root?.Element(atom + "entry");
		if (entry == null) return null;
		string? releaseUrl = entry.Elements(atom + "link")
			.FirstOrDefault(link => string.Equals((string?)link.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))?
			.Attribute("href")?.Value;
		if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out Uri? parsedUrl)) return null;
		string tag = Uri.UnescapeDataString(parsedUrl.Segments.LastOrDefault()?.Trim('/') ?? string.Empty);
		if (string.IsNullOrWhiteSpace(tag)) return null;
		string current = CurrentVersion;
		if (!includeSameOrOlder && !IsNewer(tag, current)) return null;
		string latestClean = NormalizeVersion(tag).ToString(3);
		string title = entry.Element(atom + "title")?.Value ?? tag;
		DateTimeOffset? published = null;
		if (DateTimeOffset.TryParse(entry.Element(atom + "updated")?.Value, out DateTimeOffset parsedDate)) published = parsedDate;

		// Asset names are part of Nova's release contract. Size may be unknown on the feed
		// path; the updater still caps downloads and verifies the SHA-256 manifest before use.
		string escapedTag = Uri.EscapeDataString(tag);
		string releaseBase = $"https://github.com/{repo}/releases/download/{escapedTag}";
		string assetName = ExpectedAssetPrefix + ".zip";
		string downloadUrl = releaseBase + "/" + assetName;
		long assetSize = 0L;
		try
		{
			using HttpRequestMessage head = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
			using HttpResponseMessage assetResponse = await http.SendAsync(head, ct).ConfigureAwait(false);
			long? contentLength = assetResponse.Content.Headers.ContentLength;
			if (assetResponse.IsSuccessStatusCode && contentLength.HasValue && contentLength.Value > 0 && contentLength.Value <= 2L * 1024 * 1024 * 1024)
				assetSize = contentLength.Value;
		}
		catch (HttpRequestException) when (!ct.IsCancellationRequested) { }
		catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
		return new LauncherUpdateInfo(
			current, latestClean, tag, title, string.Empty, releaseUrl,
			downloadUrl, assetName, assetSize, published,
			releaseBase + "/SHA256SUMS.txt");
	}

	private static async Task<LauncherUpdateInfo?> CheckLatestFromApiAsync(string repo, bool includeSameOrOlder, CancellationToken ct)
	{
		string apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
		using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(18.0) };
		http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "NovaWorkbench/" + CurrentVersion);
		http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
		using HttpResponseMessage response = await http.GetAsync(apiUrl, ct).ConfigureAwait(false);
		if ((int)response.StatusCode == 404) return null;
		response.EnsureSuccessStatusCode();
		using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
		using JsonDocument doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
		JsonElement root = doc.RootElement;
		string tag = root.TryGetProperty("tag_name", out JsonElement tagNode) ? tagNode.GetString() ?? string.Empty : string.Empty;
		if (string.IsNullOrWhiteSpace(tag)) return null;
		string current = CurrentVersion;
		if (!includeSameOrOlder && !IsNewer(tag, current)) return null;
		string latestClean = NormalizeVersion(tag).ToString(3);
		string title = root.TryGetProperty("name", out JsonElement nameNode) ? nameNode.GetString() ?? tag : tag;
		string notes = root.TryGetProperty("body", out JsonElement bodyNode) ? bodyNode.GetString() ?? string.Empty : string.Empty;
		string htmlUrl = root.TryGetProperty("html_url", out JsonElement htmlNode) ? htmlNode.GetString() ?? $"https://github.com/{repo}/releases" : $"https://github.com/{repo}/releases";
		DateTimeOffset? published = null;
		if (root.TryGetProperty("published_at", out JsonElement publishedNode) && publishedNode.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(publishedNode.GetString(), out DateTimeOffset parsedDate)) published = parsedDate;
		string? downloadUrl = null;
		string? assetName = null;
		long assetSize = 0L;
		string? checksumUrl = null;
		if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement asset in assets.EnumerateArray())
			{
				string aName = asset.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty;
				string aUrl = asset.TryGetProperty("browser_download_url", out JsonElement url) ? url.GetString() ?? string.Empty : string.Empty;
				long aSize = asset.TryGetProperty("size", out JsonElement size) ? size.GetInt64() : 0L;
				if (aName.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(aUrl)) checksumUrl = aUrl;
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
		string sums = await DownloadChecksumsAsync(checksumClient, update.ChecksumUrl, ct).ConfigureAwait(false);
		string expectedHash = ExpectedSha256(sums, update.AssetName);

		string workRoot = Path.Combine(paths.Cache, "launcher-update");
		SafePath.RejectLinks(paths.Root, workRoot);
		Directory.CreateDirectory(workRoot);
		using FileStream updateLock = await AcquireUpdateLockAsync(workRoot, ct).ConfigureAwait(false);
		string partial = Path.Combine(workRoot, "download-" + expectedHash.ToLowerInvariant() + ".part");
		SafePath.RejectLinks(workRoot, partial);
		string unique = Guid.NewGuid().ToString("N");
		string extractDir = Path.Combine(workRoot, "extract-" + unique);
		string stagedDir = Path.Combine(workRoot, "stage-" + unique);
		string zipPath = await UpdateDownloader.FetchAsync(update.DownloadUrl, partial, expectedHash, update.AssetSizeBytes, log, ct).ConfigureAwait(false);

		try
		{
			using ZipArchive archive = ZipFile.OpenRead(zipPath);
			var limits = ArchiveLimits.Default;
			SafeArchive.Extract(archive, extractDir, "", limits, new ArchiveBudget(limits), ct: ct);
		}
		catch
		{
			if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
			throw;
		}

		try
		{
			// Support both OTA archives and portable bundles while requiring a complete runtime.
			string binCandidate = Path.Combine(extractDir, "bin");
			string sourceBin = (Directory.Exists(binCandidate) && File.Exists(Path.Combine(binCandidate, "Nova.Core.dll"))) ? binCandidate : extractDir;
			if (!File.Exists(Path.Combine(sourceBin, "Nova.Core.dll")))
			{
				string? nestedCore = Directory.GetFiles(extractDir, "Nova.Core.dll", SearchOption.AllDirectories).FirstOrDefault();
				if (nestedCore != null) sourceBin = Path.GetDirectoryName(nestedCore)!;
			}
			foreach (string required in new[] { "Nova.exe", "Nova.dll", "Nova.Core.dll", "nova-cli.exe", "Nova.runtimeconfig.json", "nova-cli.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll" })
				if (!File.Exists(Path.Combine(sourceBin, required))) throw new InvalidDataException("Update archive is incomplete: " + required);
			foreach (string config in new[] { "Nova.runtimeconfig.json", "nova-cli.runtimeconfig.json" })
			{
				using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sourceBin, config), ct).ConfigureAwait(false));
				if (!doc.RootElement.TryGetProperty("runtimeOptions", out var opts) || !opts.TryGetProperty("includedFrameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array || frameworks.GetArrayLength() == 0 || opts.TryGetProperty("frameworks", out _))
					throw new InvalidDataException("Update has an incompatible runtime configuration: " + config);
			}
			Directory.Move(sourceBin, stagedDir);
			if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
			log?.Report($"staged v{update.LatestVersion} cleanly ({Directory.GetFiles(stagedDir).Length} binary files)");
			return stagedDir;
		}
		catch
		{
			if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
			if (Directory.Exists(stagedDir)) Directory.Delete(stagedDir, true);
			throw;
		}
	}

	private static async Task<string> DownloadChecksumsAsync(HttpClient client, string url, CancellationToken ct)
	{
		Exception? last = null;
		for (int i = 0; i < 3; i++)
		{
			try
			{
				using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
				response.EnsureSuccessStatusCode();
				if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Checksum file is too large.");
				await using Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
				using var memory = new MemoryStream();
				byte[] buffer = new byte[8192]; int n;
				while ((n = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
				{
					if (memory.Length + n > 1024 * 1024) throw new InvalidDataException("Checksum file is too large.");
					memory.Write(buffer, 0, n);
				}
				return Encoding.UTF8.GetString(memory.ToArray());
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
			{ last = ex; if (i < 2) await Task.Delay(500 * (i + 1), ct).ConfigureAwait(false); }
		}
		throw new IOException("Could not download update checksums. Check your connection and retry.", last);
	}

	private static async Task<FileStream> AcquireUpdateLockAsync(string workRoot, CancellationToken ct)
	{
		string file = Path.Combine(workRoot, "update.lock");
		SafePath.RejectLinks(workRoot, file);
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
			catch (IOException ex) when (OperatingSystem.IsWindows() ? ((ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33) : (ex.HResult & 0xffff) == 11)
			{ await Task.Delay(150, ct).ConfigureAwait(false); }
		}
	}

	/// <summary>
	/// For CLI use (or when Nova.exe is not locking files): applies staged files directly or spawns a detached PowerShell swapper.
	/// When <paramref name="relaunchGui"/> is true, waits for <paramref name="waitForPid"/> to exit, copies staged files into <paramref name="targetBinDir"/>, and relaunches Nova.exe.
	/// </summary>
	public static void LaunchDetachedSwapper(string stagedDir, string targetBinDir, int waitForPid, bool relaunchGui, string? logFilePath = null)
	{
		stagedDir = Path.GetFullPath(stagedDir);
		targetBinDir = Path.GetFullPath(targetBinDir);
		if (!Directory.Exists(stagedDir) || !Directory.Exists(targetBinDir)) throw new DirectoryNotFoundException("Update stage or target bin is missing.");
		SafePath.RejectLinks(stagedDir, stagedDir);
		SafePath.RejectLinks(targetBinDir, targetBinDir);
		string workRoot = Path.GetDirectoryName(stagedDir) ?? throw new InvalidOperationException("Update stage has no parent.");
		string unique = Guid.NewGuid().ToString("N");
		string scriptPath = Path.Combine(workRoot, "apply-" + unique + ".ps1");
		string backupDir = Path.Combine(workRoot, "backup-" + unique);
		string statusFile = Path.Combine(workRoot, "last-update-result.txt");
		var sb = new StringBuilder();
		sb.AppendLine("$stage = '" + EscapePs(stagedDir) + "'");
		sb.AppendLine("$target = '" + EscapePs(targetBinDir) + "'");
		sb.AppendLine("$backup = '" + EscapePs(backupDir) + "'");
		sb.AppendLine("$status = '" + EscapePs(statusFile) + "'");
		sb.AppendLine("$log = '" + EscapePs(logFilePath ?? "") + "'");
		sb.AppendLine("$waitPid = " + Math.Max(0, waitForPid));
		sb.AppendLine("$relaunch = " + (relaunchGui ? "$true" : "$false"));
		sb.AppendLine("""
$ErrorActionPreference = 'Stop'
function Log([string]$message) {
    try { [IO.File]::WriteAllText($status, $message) } catch { }
    if ($log) { try { Add-Content -LiteralPath $log -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') updater: $message" } catch { } }
}
function CheckLinks([string]$base, [string]$path) {
    $root = [IO.Path]::GetFullPath($base).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path outside target' }
    $current = $base
    if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Update root is a link' }
    foreach ($part in $full.Substring($root.Length).Split('\')) {
        $current = Join-Path $current $part
        if (Test-Path -LiteralPath $current) {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Update target contains a link' }
        }
    }
}
function FileSha([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)) }
    finally { $hasher.Dispose(); $stream.Dispose() }
}
function CopyRetry([string]$from, [string]$to) {
    for ($i=0; $i -lt 4; $i++) {
        try { Copy-Item -LiteralPath $from -Destination $to -Force -ErrorAction Stop; return }
        catch { if ($i -eq 3) { throw }; Start-Sleep -Milliseconds 400 }
    }
}
$applied = New-Object 'System.Collections.Generic.List[object]'
$records = New-Object 'System.Collections.Generic.List[object]'
$installLock = $null
try {
    $lockFile = Join-Path $target '.nova-update.lock'
    CheckLinks $target $lockFile
    for ($i=0; $i -lt 300; $i++) {
        try { $installLock = [IO.File]::Open($lockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); break }
        catch [IO.IOException] { Start-Sleep -Milliseconds 200 }
    }
    if ($null -eq $installLock) { throw 'Another update is still being installed' }
    if ($waitPid -gt 0) {
        for ($i=0; $i -lt 300; $i++) {
            if ($null -eq (Get-Process -Id $waitPid -ErrorAction SilentlyContinue)) { break }
            Start-Sleep -Milliseconds 200
        }
        if ($null -ne (Get-Process -Id $waitPid -ErrorAction SilentlyContinue)) { throw 'Launcher did not exit; update was not applied' }
    }
    New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
    $root = [IO.Path]::GetFullPath($stage).TrimEnd('\') + '\'
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse -Force)
    if ($files.Count -eq 0) { throw 'Update stage is empty' }
    $i = 0
    foreach ($f in $files) {
        CheckLinks $stage $f.FullName
        $rel = $f.FullName.Substring($root.Length)
        $top = $rel.Split('\')[0]
        if ($top -in @('data','instances','saves','mods','accounts','cache','settings.json','.nova-update.lock')) { throw 'Update contains a reserved data path' }
        $dest = [IO.Path]::GetFullPath((Join-Path $target $rel))
        CheckLinks $target $dest
        if ((Test-Path -LiteralPath $dest) -and -not (Test-Path -LiteralPath $dest -PathType Leaf)) { throw 'Update file conflicts with a directory' }
        $old = Test-Path -LiteralPath $dest -PathType Leaf
        $saved = Join-Path $backup ([string]$i)
        if ($old) {
            CopyRetry $dest $saved
            if ((FileSha $dest) -ne (FileSha $saved)) { throw 'Could not verify update backup' }
        }
        $records.Add([pscustomobject]@{ Source=$f.FullName; Target=$dest; Saved=$saved; Existed=$old })
        $i++
    }
    foreach ($record in $records) {
        CheckLinks $target $record.Target
        $parent = [IO.Path]::GetDirectoryName($record.Target)
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        $applied.Add($record)
        CopyRetry $record.Source $record.Target
        if ((FileSha $record.Source) -ne (FileSha $record.Target)) { throw 'Installed file verification failed' }
    }
    Log 'SUCCESS: update installed'
    try { Remove-Item -LiteralPath $backup -Recurse -Force } catch { }
    if ($relaunch) { try { Start-Process -FilePath (Join-Path $target 'Nova.exe') -WorkingDirectory $target } catch { Log ('SUCCESS: updated, but restart failed: ' + $_.Exception.Message) } }
}
catch {
    $reason = $_.Exception.Message
    $rollbackFailed = $false
    for ($i=$applied.Count-1; $i -ge 0; $i--) {
        $record=$applied[$i]
        try {
            CheckLinks $target $record.Target
            if ($record.Existed) { CopyRetry $record.Saved $record.Target }
            elseif (Test-Path -LiteralPath $record.Target) { Remove-Item -LiteralPath $record.Target -Force -ErrorAction Stop }
        } catch { $rollbackFailed = $true }
    }
    if ($rollbackFailed) { Log ('ROLLBACK_FAILED: ' + $reason + '; backup retained at ' + $backup) }
    else { Log ('ROLLED_BACK: ' + $reason); try { if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force } } catch { } }
    if ($relaunch) {
        try { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show(('Nova update failed: ' + $reason + $(if ($rollbackFailed) { "`nBackup retained at: " + $backup } else { "`nChanges were rolled back." })), 'Nova updater') | Out-Null } catch { }
        if (-not $rollbackFailed) { try { Start-Process -FilePath (Join-Path $target 'Nova.exe') -WorkingDirectory $target } catch { } }
    }
    exit 1
}
finally { if ($null -ne $installLock) { $installLock.Dispose() } }
""");
		File.WriteAllText(scriptPath, sb.ToString(), Encoding.UTF8);
		Process? process = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetTempPath()
		});
		if (process == null) throw new IOException("Could not launch the update installer.");
		process.Dispose();
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
