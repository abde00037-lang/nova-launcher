using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class ForgeInstaller
{
	private const string MavenRoot = "https://maven.minecraftforge.net/net/minecraftforge/forge";

	public static string InstallerUrl(string mcVersion, string forgeVersion)
	{
		return $"{MavenRoot}/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
	}

	public static bool IsInstalled(string mcVersion, string forgeVersion, LauncherPaths paths)
	{
		string text = mcVersion + "-forge-" + forgeVersion;
		return File.Exists(Path.Combine(paths.Versions, text, text + ".json"));
	}

	public static async Task<string> InstallAsync(string mcVersion, string forgeVersion, LauncherPaths paths, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		if (string.IsNullOrWhiteSpace(mcVersion) || string.IsNullOrWhiteSpace(forgeVersion))
		{
			throw new ArgumentException("both the minecraft version and the forge version are required");
		}
		string coreId = mcVersion + "-forge-" + forgeVersion;
		if (IsInstalled(mcVersion, forgeVersion, paths))
		{
			log?.Report("Forge " + coreId + " is already installed");
			progress?.Report(new DownloadProgress("Forge", 100, 100, 0L, "Forge " + coreId + " already installed"));
			return coreId;
		}
		paths.EnsureCreated();
		log?.Report("vanilla " + mcVersion + ": checking");
		await MojangMeta.InstallAsync(mcVersion, paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		int requiredJava = VersionResolver.GuessJavaMajor(mcVersion);
		JavaInstall? java = JavaLocator.Pick(requiredJava);
		if ((object?)java == null)
		{
			progress?.Report(new DownloadProgress("Java", 15, 100, 0L, $"provisioning Java {requiredJava}…"));
			java = await JavaProvisioner.EnsureAsync(paths, requiredJava, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if ((object?)java == null)
		{
			throw new InvalidOperationException($"Forge {coreId} needs Java {requiredJava} - none was found (see: nova-cli java)");
		}
		Directory.CreateDirectory(paths.Cache);
		string installerPath = Path.Combine(paths.Cache, "forge-" + coreId + "-installer.jar");
		string[] candidateUrls = new string[2]
		{
			InstallerUrl(mcVersion, forgeVersion),
			$"{MavenRoot}/{mcVersion}-{forgeVersion}-{mcVersion}/forge-{mcVersion}-{forgeVersion}-{mcVersion}-installer.jar"
		};
		using (Downloader downloader = new Downloader(2))
		{
			log?.Report("downloading the forge installer");
			progress?.Report(new DownloadProgress("Forge Installer", 20, 100, 0L, "downloading Forge installer…"));
			bool fetched = false;
			foreach (string url in candidateUrls)
			{
				FetchResult res = await downloader.EnsureAsync(installerPath, url, null, ct, delegate(long done, long total)
				{
					int pct = ((total > 0) ? ((int)Math.Clamp(20 + done * 30 / total, 20L, 50L)) : 35);
					progress?.Report(new DownloadProgress("Forge Installer", pct, 100, done, $"installer.jar · {(double)done / 1048576.0:F1} MB"));
				}).ConfigureAwait(continueOnCapturedContext: false);
				if (res != FetchResult.Failed)
				{
					fetched = true;
					break;
				}
			}
			if (!fetched)
			{
				throw new InvalidOperationException("could not download the Forge installer for " + coreId);
			}
		}
		string path = Path.Combine(paths.Root, "launcher_profiles.json");
		if (!File.Exists(path))
		{
			File.WriteAllText(path, JsonSerializer.Serialize(new
			{
				profiles = new { },
				settings = new { },
				version = 3
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		log?.Report("running the forge installer (this takes a minute)");
		progress?.Report(new DownloadProgress("Forge Patching", 55, 100, 0L, "running Forge client processor…"));
		ProcessStartInfo processStartInfo = new ProcessStartInfo(java.Path)
		{
			WorkingDirectory = paths.Root,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		processStartInfo.ArgumentList.Add("-jar");
		processStartInfo.ArgumentList.Add(installerPath);
		processStartInfo.ArgumentList.Add("--installClient");
		processStartInfo.ArgumentList.Add(paths.Root);
		using Process process = new Process
		{
			StartInfo = processStartInfo
		};
		StringBuilder output = new StringBuilder();
		int linesSeen = 0;
		process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (output) output.AppendLine(e.Data);
				log?.Report(e.Data);
				int n = Interlocked.Increment(ref linesSeen);
				int pct = Math.Min(96, 55 + n / 6);
				progress?.Report(new DownloadProgress("Forge Patching", pct, 100, 0L, e.Data.Length > 72 ? (e.Data.Substring(0, 72) + "…") : e.Data));
			}
		};
		process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				lock (output) output.AppendLine(e.Data);
				log?.Report(e.Data);
			}
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromMinutes(6.0));
		try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
		catch (OperationCanceledException)
		{
			try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
			throw;
		}
		process.WaitForExit(); // Wait for redirected output callbacks to finish before inspecting them.
		string text;
		lock (output) text = output.ToString().Trim();
        CheckInstallerOutcome(process.ExitCode, text, IsInstalled(mcVersion, forgeVersion, paths));
		progress?.Report(new DownloadProgress("Forge", 100, 100, 0L, "Forge " + coreId + " installed"));
		log?.Report("Forge " + coreId + " installed");
		return coreId;
	}

	internal static void CheckInstallerOutcome(int exitCode, string text, bool profileExists)
	{
		if (exitCode != 0 || text.Contains("There was an error during installation", StringComparison.OrdinalIgnoreCase) || !profileExists)
		{
			if (text.Length > 600) text = text.Substring(text.Length - 600, 600);
			throw new InvalidOperationException($"Forge installation failed (exit {exitCode}); verify the network and retry. Output tail: " + text);
		}
	}

	public static (string? McVersion, string? ForgeVersion) Split(string versionId)
	{
		int num = versionId.IndexOf("-forge-", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return (McVersion: null, ForgeVersion: null);
		}
		string item = versionId.Substring(0, num);
		int num2 = num + 7;
		return (McVersion: item, ForgeVersion: versionId.Substring(num2, versionId.Length - num2));
	}
}
