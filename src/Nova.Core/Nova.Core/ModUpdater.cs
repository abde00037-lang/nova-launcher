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
	long Size,
	string? CurrentSha1 = null,
	string? Changelog = null);

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
			if (ModPinStore.IsPinned(modsDirectory, fn)) continue;
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
			resp.EnsureSuccessStatusCode();
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
				string? changelog = verObj.TryGetProperty("changelog", out var clEl) && clEl.ValueKind == JsonValueKind.String ? clEl.GetString() : null;
				if (changelog?.Length > 20000) changelog = changelog.Substring(0, 20000) + "\n[Changelog truncated]";
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
				size,
					oldSha,
					changelog);
			}
		}
		log?.Report(results.Count == 0
			? "all Modrinth-tracked mods are up to date"
			: $"{results.Count} mod update(s) available for {mcVersion} ({effLoader})");
		return results;
	}

	// Backups are kept outside the active mod list and are never deleted automatically.
	public static async Task<string> ApplyUpdateAsync(string modsDirectory, ModUpdateCandidate candidate, CancellationToken ct = default(CancellationToken))
	{
		Directory.CreateDirectory(modsDirectory);
		using FileStream operationLock = AcquireModLock(modsDirectory);
		string currentName = candidate.CurrentFileName;
		if (ModPinStore.IsPinned(modsDirectory, currentName)) throw new IOException("This mod is pinned. Unpin it before updating.");
		string finalName = candidate.WasEnabled ? candidate.NewFileName : candidate.NewFileName + ModManager.DisabledSuffix;
		if (!ModManager.IsModFile(currentName) || !ModManager.IsModFile(finalName) ||
			Path.GetFileName(currentName) != currentName || Path.GetFileName(finalName) != finalName ||
			currentName.Contains('\\') || finalName.Contains('\\'))
			throw new InvalidDataException("Invalid mod filename.");
		if (candidate.Sha1 == null || candidate.Sha1.Length != 40 || !candidate.Sha1.All(Uri.IsHexDigit) ||
			candidate.CurrentSha1 == null || candidate.CurrentSha1.Length != 40 || !candidate.CurrentSha1.All(Uri.IsHexDigit))
			throw new InvalidDataException("Mod update is missing a valid checksum; check updates again.");
		string oldPath = SafePath.ResolveWithin(modsDirectory, currentName);
		string finalTarget = SafePath.ResolveWithin(modsDirectory, finalName);
		bool sameFile = string.Equals(oldPath, finalTarget, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!File.Exists(oldPath) || !string.Equals(await ComputeSha1Async(oldPath, ct), candidate.CurrentSha1, StringComparison.OrdinalIgnoreCase))
			throw new IOException("The original mod changed or disappeared; check updates again.");
		if (!sameFile && File.Exists(finalTarget)) throw new IOException("The new mod filename already exists; refusing to overwrite it.");
		string tempTarget = SafePath.ResolveWithin(modsDirectory, ".nova-update-" + Guid.NewGuid().ToString("N") + ".tmp");
		try
		{
			using (Downloader downloader = new Downloader(2))
			{
				FetchResult res = await downloader.EnsureAsync(tempTarget, candidate.DownloadUrl, candidate.Sha1, ct).ConfigureAwait(false);
				if (res != FetchResult.Downloaded && res != FetchResult.AlreadyValid) throw new IOException("Failed to download or verify " + candidate.NewFileName);
			}
			// Copy and verify the original before touching it. Failure to back up aborts the update.
			if (!string.Equals(await ComputeSha1Async(oldPath, ct), candidate.CurrentSha1, StringComparison.OrdinalIgnoreCase))
				throw new IOException("The original mod changed during download; check updates again.");
			string backupDir = SafePath.ResolveWithin(modsDirectory, Path.Combine(".nova-update-backups", Guid.NewGuid().ToString("N")));
			Directory.CreateDirectory(backupDir);
			string backupPath = SafePath.ResolveWithin(backupDir, currentName);
			File.Copy(oldPath, backupPath, overwrite: false);
			if (!string.Equals(await ComputeSha1Async(backupPath, ct), candidate.CurrentSha1, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(await ComputeSha1Async(oldPath, ct), candidate.CurrentSha1, StringComparison.OrdinalIgnoreCase))
				throw new IOException("Could not verify the mod backup; update aborted.");
			File.WriteAllText(Path.Combine(backupDir, "backup.json"), JsonSerializer.Serialize(new {
				originalFile = currentName, replacementFile = finalName, originalSha1 = candidate.CurrentSha1,
				replacementSha1 = candidate.Sha1, createdUtc = DateTimeOffset.UtcNow
			}));
			SafePath.RejectLinks(modsDirectory, oldPath);
			SafePath.RejectLinks(modsDirectory, finalTarget);
			File.Move(tempTarget, finalTarget, overwrite: sameFile);
			if (!sameFile)
			{
				try { File.Delete(oldPath); }
				catch (Exception ex) { throw new IOException("Updated mod was downloaded but the old mod could not be removed. Check the mods folder; backup: " + backupDir, ex); }
			}
			return finalName;
		}
		finally { try { if (File.Exists(tempTarget)) File.Delete(tempTarget); } catch { } }
	}

	private sealed record ModBackupMetadata(string originalFile, string replacementFile, string originalSha1, string replacementSha1, DateTimeOffset createdUtc);

	// Restores only when the active replacement still matches the recorded update.
	// The replacement is retained in the backup folder before any destructive change.
	public static async Task RestoreBackupAsync(string modsDirectory, string manifestPath, CancellationToken ct = default(CancellationToken))
	{
		string root = Path.GetFullPath(modsDirectory);
		using FileStream operationLock = AcquireModLock(root);
		string fullManifest = Path.GetFullPath(manifestPath);
		string backupRoot = SafePath.ResolveWithin(root, ".nova-update-backups");
		string relative = Path.GetRelativePath(backupRoot, fullManifest);
		string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _) || !string.Equals(parts[1], "backup.json", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Choose a backup.json inside this instance's mod backups.");
		string checkedManifest = SafePath.ResolveWithin(backupRoot, relative);
		ModBackupMetadata meta = JsonSerializer.Deserialize<ModBackupMetadata>(await File.ReadAllTextAsync(checkedManifest, ct).ConfigureAwait(false))
			?? throw new InvalidDataException("Invalid mod backup manifest.");
		if (string.IsNullOrWhiteSpace(meta.originalFile) || string.IsNullOrWhiteSpace(meta.replacementFile) ||
			Path.GetFileName(meta.originalFile) != meta.originalFile || Path.GetFileName(meta.replacementFile) != meta.replacementFile ||
			meta.originalFile.Contains('\\') || meta.replacementFile.Contains('\\') ||
			!ModManager.IsModFile(meta.originalFile) || !ModManager.IsModFile(meta.replacementFile) ||
			meta.originalSha1 == null || meta.originalSha1.Length != 40 || !meta.originalSha1.All(Uri.IsHexDigit) ||
			meta.replacementSha1 == null || meta.replacementSha1.Length != 40 || !meta.replacementSha1.All(Uri.IsHexDigit))
			throw new InvalidDataException("Invalid mod backup metadata.");
		string backupDir = Path.GetDirectoryName(checkedManifest)!;
		string original = SafePath.ResolveWithin(root, meta.originalFile);
		string replacement = SafePath.ResolveWithin(root, meta.replacementFile);
		string saved = SafePath.ResolveWithin(backupDir, meta.originalFile);
		bool same = string.Equals(original, replacement, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		if (!File.Exists(saved) || !string.Equals(await ComputeSha1Async(saved, ct), meta.originalSha1, StringComparison.OrdinalIgnoreCase))
			throw new IOException("The original mod backup is missing or damaged.");
		if (!File.Exists(replacement) || !string.Equals(await ComputeSha1Async(replacement, ct), meta.replacementSha1, StringComparison.OrdinalIgnoreCase))
			throw new IOException("The updated mod changed or is missing; refusing automatic restore.");
		if (!same && File.Exists(original)) throw new IOException("The original filename already exists; refusing to overwrite it.");
		string temp = SafePath.ResolveWithin(root, ".nova-restore-" + Guid.NewGuid().ToString("N") + ".tmp");
		string keptReplacement = SafePath.ResolveWithin(backupDir, "replacement-" + Guid.NewGuid().ToString("N") + ".data");
		bool moved = false;
		try
		{
			File.Copy(saved, temp, false);
			if (!string.Equals(await ComputeSha1Async(temp, ct), meta.originalSha1, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(await ComputeSha1Async(replacement, ct), meta.replacementSha1, StringComparison.OrdinalIgnoreCase))
				throw new IOException("The mod or backup changed; restore aborted.");
			SafePath.RejectLinks(root, original); SafePath.RejectLinks(root, replacement); SafePath.RejectLinks(root, keptReplacement);
			File.Move(replacement, keptReplacement, false);
			moved = true;
			File.Move(temp, original, false);
		}
		catch
		{
			if (moved && !File.Exists(replacement))
			{
				try { File.Move(keptReplacement, replacement, false); } catch { /* Both versions remain in the backup. */ }
			}
			throw;
		}
		finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
	}

	internal static FileStream AcquireModLock(string modsDirectory)
	{
		string path = SafePath.ResolveWithin(modsDirectory, ".nova-mod-update.lock");
		try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
		catch (IOException ex) { throw new IOException("Another mod update or restore is in progress for this instance.", ex); }
	}

	private static async Task<string?> ComputeSha1Async(string path, CancellationToken ct)
	{
		try
		{
			await using FileStream fs = File.OpenRead(path);
			byte[] hash = await SHA1.HashDataAsync(fs, ct).ConfigureAwait(continueOnCapturedContext: false);
			return Convert.ToHexString(hash).ToLowerInvariant();
		}
		catch (OperationCanceledException) { throw; }
		catch
		{
			return null;
		}
	}
}
