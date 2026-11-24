using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class Doctor
{
	public static async Task<DoctorReport> RunAsync(InstanceConfig instance, LauncherPaths paths, bool deep = false, CancellationToken ct = default(CancellationToken))
	{
		DoctorReport report = new DoctorReport();
		if (!Directory.Exists(instance.GameDirectory))
		{
			report.Add("Instance", CheckStatus.Fail, "Game directory missing: " + instance.GameDirectory, "Fix the path in the instance settings.");
		}
		else
		{
			report.Add("Instance", CheckStatus.Ok, "Game directory: " + instance.GameDirectory);
		}
		ResolvedVersion version = null;
		try
		{
			string versionsHome = (string.IsNullOrWhiteSpace(instance.VersionHome) ? paths.Versions : instance.VersionHome);
			version = (report.Version = VersionResolver.Resolve(instance.VersionId, versionsHome));
			report.Add("Version", CheckStatus.Ok, $"{version.Id} (parent {version.ParentId ?? "none"}) - {version.Files.Count} libraries, {version.NativeFiles.Count} native jars, Java {version.RequiredJavaMajor}");
			foreach (string item in version.Warnings.Distinct().Take(5))
			{
				report.Add("Version", CheckStatus.Warn, item);
			}
		}
		catch (Exception ex)
		{
			report.Add("Version", CheckStatus.Fail, "Cannot resolve version: " + ex.Message, "Make sure the version JSON exists in the versions folder (install Forge for this version).");
		}
		int num = version?.RequiredJavaMajor ?? 8;
		JavaInstall javaInstall = null;
		if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))
		{
			javaInstall = JavaLocator.Probe(instance.JavaPath, "instance");
		}
		if ((object)javaInstall == null)
		{
			javaInstall = JavaLocator.Pick(num);
		}
		report.Java = javaInstall;
		if ((object)javaInstall == null)
		{
			report.Add("Java", CheckStatus.Fail, $"No Java {num} found on this machine", "Install a matching JDK, or set java.exe manually in the instance settings.");
		}
		else if (!JavaCompatible(javaInstall.Major, num))
		{
			report.Add("Java", CheckStatus.Warn, $"Java {javaInstall.Major} selected but the version asks for {num}", $"Point the instance to a Java {num} install (e.g. Temurin 8).", fixable: true);
		}
		else
		{
			report.Add("Java", CheckStatus.Ok, $"{javaInstall} - {javaInstall.Path}");
		}
		if (version != null)
		{
			string librariesDir = (string.IsNullOrWhiteSpace(instance.LibrariesHome) ? paths.Libraries : instance.LibrariesHome);
			string versionsHome2 = (string.IsNullOrWhiteSpace(instance.VersionHome) ? paths.Versions : instance.VersionHome);
			List<string> missing = new List<string>();
			List<string> missingOptional = new List<string>();
			List<string> badHash = new List<string>();
			using Downloader downloader = new Downloader();
			List<string> missingLocal = new List<string>();
			List<string> badHashLocal = new List<string>();
			foreach (ResolvedFile file in version.Files)
			{
				string path = Path.Combine(librariesDir, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
				bool downloadable = !string.IsNullOrEmpty(file.Url);
				if (!File.Exists(path))
				{
					if (!downloadable)
					{
						missingLocal.Add(file.RelativePath);
						continue;
					}
					Coordinates coordinates = Coordinates.Parse(file.Name);
					(version.NativeFiles.Any((ResolvedFile n) => Coordinates.Parse(n.Name).Group == coordinates.Group && Coordinates.Parse(n.Name).Artifact == coordinates.Artifact) ? missingOptional : missing).Add(file.RelativePath);
				}
				else if (deep && !string.IsNullOrEmpty(file.Sha1) && !string.Equals(await downloader.Sha1Async(path, ct).ConfigureAwait(continueOnCapturedContext: false), file.Sha1, StringComparison.OrdinalIgnoreCase))
				{
					(downloadable ? badHash : badHashLocal).Add(file.RelativePath);
				}
			}
			if (missingLocal.Count > 0)
			{
				report.Add("Libraries", CheckStatus.Fail, $"{missingLocal.Count} local artifacts missing (e.g. {string.Join(", ", missingLocal.Take(2))})", "Run the Forge installer again for this version.");
			}
			if (badHashLocal.Count > 0)
			{
				report.Add("Libraries", CheckStatus.Fail, $"{badHashLocal.Count} local artifacts are corrupt (SHA1 mismatch): {string.Join(", ", badHashLocal.Take(2))}", "Run the Forge installer again - repair cannot download these.");
			}
			if (missingOptional.Count > 0)
			{
				report.Add("Libraries", CheckStatus.Warn, $"{missingOptional.Count} artifact(s) exist only as a natives classifier (harmless): {string.Join(", ", missingOptional.Take(2))}");
			}
			if (missing.Count == 0)
			{
				report.Add("Libraries", CheckStatus.Ok, $"{version.Files.Count} libraries present");
			}
			else
			{
				report.Add("Libraries", CheckStatus.Fail, $"{missing.Count} libraries missing (e.g. {string.Join(", ", missing.Take(3))})", "Run repair to download them.", fixable: true);
			}
			if (badHash.Count > 0)
			{
				report.Add("Libraries", CheckStatus.Warn, $"{badHash.Count} libraries have a wrong SHA1 (e.g. {string.Join(", ", badHash.Take(3))})", "Run repair to re-download them.", fixable: true);
			}
			string text = Path.Combine(versionsHome2, version.ClientJarVersionId, version.ClientJarVersionId + ".jar");
			if (File.Exists(text))
			{
				report.Add("Client jar", CheckStatus.Ok, text);
			}
			else
			{
				string text2 = version.ClientDownload?.Url;
				if (text2 != null && text2.Length > 0)
				{
					report.Add("Client jar", CheckStatus.Fail, "Missing " + text + " (it can be downloaded)", "Run repair.", fixable: true);
				}
				else
				{
					report.Add("Client jar", CheckStatus.Fail, "Missing " + text, "Reinstall the base game version (no download url in the json).");
				}
			}
			List<string> list = (from n in version.NativeFiles
				where !File.Exists(Path.Combine(librariesDir, n.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
				select n.RelativePath).ToList();
			if (list.Count == 0)
			{
				report.Add("Natives", CheckStatus.Ok, $"{version.NativeFiles.Count} native jars present");
			}
			else
			{
				report.Add("Natives", CheckStatus.Fail, $"{list.Count} native jars missing", "Run repair.", fixable: true);
			}
			string assetsDir = (string.IsNullOrWhiteSpace(instance.AssetsDirectory) ? paths.Assets : instance.AssetsDirectory);
			if (!string.IsNullOrEmpty(version.AssetIndexId))
			{
				AssetReport assetReport = (report.Assets = await new AssetStore(assetsDir, downloader).VerifyAsync(version.AssetIndexId, download: false, null, ct).ConfigureAwait(continueOnCapturedContext: false));
				if (assetReport.Total == 0)
				{
					report.Add("Assets", CheckStatus.Fail, "Asset index '" + version.AssetIndexId + "' not found in " + assetsDir, "This is exactly why the game boots with no sounds. Run repair to fetch the official index.", fixable: true);
				}
				else if (assetReport.Missing == 0)
				{
					report.Add("Assets", CheckStatus.Ok, $"{assetReport.Present}/{assetReport.Total} asset objects present (index {assetReport.IndexId})");
				}
				else
				{
					report.Add("Assets", CheckStatus.Fail, $"{assetReport.Missing}/{assetReport.Total} asset objects missing (index {assetReport.IndexId})", "Sounds and language files will silently fail. Run repair.", fixable: true);
				}
			}
		}
		string text3 = Path.Combine(instance.GameDirectory, "mods");
		if (Directory.Exists(text3))
		{
			ModScanResult modScanResult = (report.Mods = ModScanner.Scan(text3));
			report.Add("Mods", CheckStatus.Ok, $"{modScanResult.Count} mod files ({modScanResult.EnabledCount} enabled, {modScanResult.DisabledCount} disabled), {(double)modScanResult.TotalBytes / 1048576.0:F0} MB");
			if (modScanResult.DuplicateIds.Count > 0)
			{
				report.Add("Mods", CheckStatus.Fail, "Duplicate mod ids: " + string.Join("; ", modScanResult.DuplicateIds.Select((KeyValuePair<string, List<string>> d) => d.Key + " (" + string.Join(", ", d.Value) + ")")), "Two versions of the same mod are installed. Disable one of them.");
			}
			if (modScanResult.BrokenJars.Count > 0)
			{
				report.Add("Mods", CheckStatus.Warn, "Unreadable jars: " + string.Join(", ", modScanResult.BrokenJars.Take(4)), "Re-download these files.");
			}
			if (modScanResult.MissingDependencies.Count > 0)
			{
				report.Add("Mods", CheckStatus.Warn, "Missing dependencies: " + string.Join("; ", modScanResult.MissingDependencies.Take(4)), "The mod may be fine (deps are often optional), but check before reporting a crash.");
			}
		}
		else
		{
			report.Add("Mods", CheckStatus.Skip, "No mods folder");
		}
		double num2 = (double)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576.0;
		int num3 = LaunchBuilder.RecommendMemoryMb(report.Mods?.EnabledCount ?? 0);
		if (instance.MemoryMb < num3)
		{
			report.Add("Memory", CheckStatus.Warn, $"{instance.MemoryMb} MB assigned, {num3} MB recommended for {report.Mods?.EnabledCount ?? 0} mods", "Raise it in the instance settings.", fixable: true);
		}
		else if ((double)instance.MemoryMb > num2 * 0.75)
		{
			report.Add("Memory", CheckStatus.Warn, $"{instance.MemoryMb} MB is more than 75% of the machine memory ({num2:F0} MB)", "Leave room for the operating system.", fixable: true);
		}
		else
		{
			report.Add("Memory", CheckStatus.Ok, $"{instance.MemoryMb} MB (recommended {num3} MB, machine {num2:F0} MB)");
		}
		string path2 = Path.Combine(instance.GameDirectory, "logs", "latest.log");
		if (File.Exists(path2))
		{
			string text4 = File.ReadAllText(path2);
			bool num4 = text4.Contains("The game crashed whilst", StringComparison.OrdinalIgnoreCase) || text4.Contains("A fatal error has been detected", StringComparison.OrdinalIgnoreCase) || text4.Contains("OutOfMemoryError", StringComparison.Ordinal);
			List<CrashFinding> list2 = (from f in CrashAnalyzer.AnalyzeText(text4)
				where f.Severity != "INFO"
				select f).ToList();
			if (num4)
			{
				if (list2.Count == 0)
				{
					report.Add("Log", CheckStatus.Fail, "latest.log ends in a crash but no known pattern matched", "Open logs/latest.log and read the last stack trace.");
				}
				foreach (CrashFinding item2 in list2.Take(4))
				{
					report.Add("Log", (!(item2.Severity == "FAIL")) ? CheckStatus.Warn : CheckStatus.Fail, item2.Title + " - " + item2.Suggestion);
				}
			}
			else if (list2.Count == 0)
			{
				report.Add("Log", CheckStatus.Ok, $"latest.log ({text4.Length / 1024} KB): last run finished without a crash signature");
			}
			else
			{
				foreach (CrashFinding item3 in list2.Take(3))
				{
					report.Add("Log", CheckStatus.Warn, "earlier in this log: " + item3.Title, item3.Suggestion);
				}
			}
		}
		string path3 = Path.Combine(instance.GameDirectory, "crash-reports");
		if (Directory.Exists(path3))
		{
			FileInfo fileInfo = (from f in new DirectoryInfo(path3).GetFiles("*.txt")
				orderby f.LastWriteTimeUtc descending
				select f).FirstOrDefault();
			if (fileInfo != null && fileInfo.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7.0))
			{
				List<CrashFinding> list3 = (from f in CrashAnalyzer.AnalyzeFile(fileInfo.FullName)
					where f.Severity == "FAIL"
					select f).Take(3).ToList();
				if (list3.Count > 0)
				{
					foreach (CrashFinding item4 in list3)
					{
						report.Add("Crash report", CheckStatus.Warn, fileInfo.Name + ": " + item4.Title, item4.Suggestion);
					}
				}
				else
				{
					report.Add("Crash report", CheckStatus.Warn, "Recent crash report: " + fileInfo.Name, "No known pattern matched; open it to read the first 'Caused by' line.");
				}
			}
		}
		return report;
	}

	public static bool JavaCompatible(int actual, int required)
	{
		if (required < 17)
		{
			return actual == required;
		}
		return actual >= required;
	}

	public static async Task<List<string>> RepairAsync(InstanceConfig instance, LauncherPaths paths, IProgress<string>? log, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		List<string> messages = new List<string>();
		string text = (string.IsNullOrWhiteSpace(instance.VersionHome) ? paths.Versions : instance.VersionHome);
		string librariesDir = (string.IsNullOrWhiteSpace(instance.LibrariesHome) ? paths.Libraries : instance.LibrariesHome);
		string assetsDir = (string.IsNullOrWhiteSpace(instance.AssetsDirectory) ? paths.Assets : instance.AssetsDirectory);
		ResolvedVersion version = VersionResolver.Resolve(instance.VersionId, text);
		using Downloader downloader = new Downloader(8);
		string text2 = Path.Combine(text, version.ClientJarVersionId, version.ClientJarVersionId + ".jar");
		if (!File.Exists(text2))
		{
			string? text3 = version.ClientDownload?.Url;
			if (text3 != null && text3.Length > 0 && version.ClientDownload != null)
			{
				long expectedClient = Math.Max(1L, version.ClientDownload.Size);
				FetchResult value = await downloader.EnsureAsync(text2, text3, version.ClientDownload.Sha1, ct, delegate(long done, long total)
				{
					long denom = ((total > 0) ? total : expectedClient);
					int pct = (int)Math.Clamp(done * 100 / Math.Max(1L, denom), 0L, 100L);
					progress?.Report(new DownloadProgress("Client JAR", pct, 100, downloader.BytesDownloaded, $"client.jar · {(double)done / 1048576.0:F1}/{(double)denom / 1048576.0:F1} MB"));
				}).ConfigureAwait(continueOnCapturedContext: false);
				string text4 = $"client jar: {value}";
				messages.Add(text4);
				log?.Report(text4);
			}
		}
		AssetStore assetStore = new AssetStore(assetsDir, downloader);
		if (assetStore.LoadIndex(version.AssetIndexId) == null)
		{
			string? text5 = version.AssetIndex?.Url;
			if (text5 != null && text5.Length > 0 && version.AssetIndex != null)
			{
				FetchResult value2 = await downloader.EnsureAsync(assetStore.IndexFile(version.AssetIndexId), text5, version.AssetIndex.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false);
				string text6 = $"asset index '{version.AssetIndexId}': {value2}";
				messages.Add(text6);
				log?.Report(text6);
			}
		}
		int skippedNativesOnly = 0;
		List<ResolvedFile> allLibs = version.Files.Concat(version.NativeFiles).ToList();
		List<ResolvedFile> downloadableLibs = new List<ResolvedFile>();
		foreach (ResolvedFile file in allLibs)
		{
			if (string.IsNullOrEmpty(file.Url))
			{
				Coordinates coordinates = Coordinates.Parse(file.Name);
				if (version.NativeFiles.Any((ResolvedFile n) => Coordinates.Parse(n.Name).Group == coordinates.Group && Coordinates.Parse(n.Name).Artifact == coordinates.Artifact))
				{
					skippedNativesOnly++;
				}
				continue;
			}
			downloadableLibs.Add(file);
		}
		int totalLibs = Math.Max(1, downloadableLibs.Count);
		int completedLibs = 0;
		progress?.Report(new DownloadProgress("Libraries", 0, totalLibs, downloader.BytesDownloaded, $"checking {downloadableLibs.Count} libraries…"));
		await Task.WhenAll(downloadableLibs.Select(async delegate(ResolvedFile file)
		{
			string targetPath = Path.Combine(librariesDir, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			FetchResult res = await downloader.EnsureAsync(targetPath, file.Url, file.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false);
			int doneNow = Interlocked.Increment(ref completedLibs);
			progress?.Report(new DownloadProgress("Libraries", doneNow, totalLibs, downloader.BytesDownloaded, $"libraries {doneNow}/{totalLibs} · {(double)downloader.BytesDownloaded / 1048576.0:F1} MB"));
			if (res == FetchResult.Failed || res == FetchResult.HashMismatch)
			{
				string failMsg = "failed: " + file.RelativePath;
				lock (messages)
				{
					messages.Add(failMsg);
				}
				log?.Report(failMsg);
			}
		})).ConfigureAwait(continueOnCapturedContext: false);
		if (skippedNativesOnly > 0)
		{
			messages.Add($"skipped {skippedNativesOnly} natives-only artifact(s)");
		}
		AssetReport assetReport = await assetStore.VerifyAsync(version.AssetIndexId, download: true, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		messages.Add($"assets: {assetReport.Present}/{assetReport.Total} present, {assetReport.Missing} were missing");
		progress?.Report(new DownloadProgress("Natives", 98, 100, downloader.BytesDownloaded, "extracting native libraries…"));
		List<string> nativeJars = version.NativeFiles.Select((ResolvedFile n) => Path.Combine(librariesDir, n.RelativePath.Replace('/', Path.DirectorySeparatorChar))).Where(File.Exists).ToList();
		string text8 = Path.Combine(paths.Natives, instance.Id);
		int value3 = NativeExtractor.ExtractAll(nativeJars, text8);
		messages.Add($"natives: {value3} files extracted to {text8}");
		progress?.Report(new DownloadProgress("Ready", 100, 100, downloader.BytesDownloaded, "installation complete"));
		return messages;
	}
}
