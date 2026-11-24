using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class NeoForgeInstaller
{
	private const string MavenRoot = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

	private const string MetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

	private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.All,
		PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(60.0)
	};

	public static async Task<Dictionary<string, string>> GetLatestByMinecraftAsync(CancellationToken ct = default(CancellationToken))
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string xml = await Http.GetStringAsync(MetadataUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			MatchCollection matches = Regex.Matches(xml, "<version>([^<]+)</version>");
			foreach (Match m in matches)
			{
				string ver = m.Groups[1].Value.Trim();
				if (ver.Contains("alpha", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string? mc = McVersionFromNeoForge(ver);
				if (mc == null)
				{
					continue;
				}
				if (!map.TryGetValue(mc, out string? existing) || CompareNeo(ver, existing) > 0)
				{
					map[mc] = ver;
				}
			}
		}
		catch
		{
		}
		return map;
	}

	public static string? McVersionFromNeoForge(string neoVersion)
	{
		string clean = neoVersion.Split('-')[0];
		string[] parts = clean.Split('.');
		if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
		{
			return null;
		}
		if (major < 20)
		{
			return null;
		}
		return (minor == 0) ? $"1.{major}" : $"1.{major}.{minor}";
	}

	private static int CompareNeo(string a, string b)
	{
		bool aBeta = a.Contains('-');
		bool bBeta = b.Contains('-');
		string aCore = a.Split('-')[0];
		string bCore = b.Split('-')[0];
		int cmp = VersionLibrary.Compare(aCore, bCore);
		if (cmp != 0)
		{
			return cmp;
		}
		if (aBeta != bBeta)
		{
			return aBeta ? (-1) : 1;
		}
		return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
	}

	public static string? FindInstalledId(string mcVersion, string neoVersion, LauncherPaths paths)
	{
		string[] candidates = new string[2]
		{
			"neoforge-" + neoVersion,
			mcVersion + "-neoforge-" + neoVersion
		};
		foreach (string c in candidates)
		{
			if (File.Exists(paths.VersionJson(c)))
			{
				return c;
			}
		}
		return null;
	}

	public static async Task<string> InstallAsync(string mcVersion, string? neoVersion, LauncherPaths paths, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		if (string.IsNullOrWhiteSpace(mcVersion))
		{
			throw new ArgumentException("minecraft version is required");
		}
		paths.EnsureCreated();
		string? resolvedNeo = neoVersion;
		if (string.IsNullOrWhiteSpace(resolvedNeo) || string.Equals(resolvedNeo, "latest", StringComparison.OrdinalIgnoreCase) || string.Equals(resolvedNeo, "recommended", StringComparison.OrdinalIgnoreCase))
		{
			Dictionary<string, string> map = await GetLatestByMinecraftAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			map.TryGetValue(mcVersion, out resolvedNeo);
		}
		if (string.IsNullOrWhiteSpace(resolvedNeo))
		{
			throw new InvalidOperationException("no NeoForge build is published for Minecraft " + mcVersion);
		}
		string? existing = FindInstalledId(mcVersion, resolvedNeo, paths);
		if (existing != null)
		{
			log?.Report("NeoForge " + existing + " is already installed");
			progress?.Report(new DownloadProgress("NeoForge", 100, 100, 0L, existing + " already installed"));
			return existing;
		}
		log?.Report("vanilla " + mcVersion + ": checking");
		await MojangMeta.InstallAsync(mcVersion, paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		int requiredJava = Math.Max(17, VersionResolver.GuessJavaMajor(mcVersion));
		JavaInstall? java = JavaLocator.Pick(requiredJava);
		if ((object?)java == null)
		{
			progress?.Report(new DownloadProgress("Java", 15, 100, 0L, $"provisioning Java {requiredJava}…"));
			java = await JavaProvisioner.EnsureAsync(paths, requiredJava, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if ((object?)java == null)
		{
			throw new InvalidOperationException($"NeoForge {resolvedNeo} needs Java {requiredJava}");
		}
		Directory.CreateDirectory(paths.Cache);
		string installerPath = Path.Combine(paths.Cache, $"neoforge-{resolvedNeo}-installer.jar");
		string url = $"{MavenRoot}/{resolvedNeo}/neoforge-{resolvedNeo}-installer.jar";
		using (Downloader downloader = new Downloader(2))
		{
			log?.Report("downloading NeoForge installer " + resolvedNeo);
			progress?.Report(new DownloadProgress("NeoForge Installer", 20, 100, 0L, "downloading NeoForge installer…"));
			FetchResult res = await downloader.EnsureAsync(installerPath, url, null, ct, delegate(long done, long total)
			{
				int pct = ((total > 0) ? ((int)Math.Clamp(20 + done * 30 / total, 20L, 50L)) : 35);
				progress?.Report(new DownloadProgress("NeoForge Installer", pct, 100, done, $"installer.jar · {(double)done / 1048576.0:F1} MB"));
			}).ConfigureAwait(continueOnCapturedContext: false);
			if (res == FetchResult.Failed)
			{
				throw new InvalidOperationException("could not download NeoForge installer " + resolvedNeo);
			}
		}
		string profilesPath = Path.Combine(paths.Root, "launcher_profiles.json");
		if (!File.Exists(profilesPath))
		{
			File.WriteAllText(profilesPath, JsonSerializer.Serialize(new
			{
				profiles = new { },
				settings = new { },
				version = 3
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		log?.Report("running NeoForge client processor…");
		progress?.Report(new DownloadProgress("NeoForge Patching", 55, 100, 0L, "running NeoForge client processor…"));
		ProcessStartInfo psi = new ProcessStartInfo(java.Path)
		{
			WorkingDirectory = paths.Root,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		psi.ArgumentList.Add("-jar");
		psi.ArgumentList.Add(installerPath);
		psi.ArgumentList.Add("--installClient");
		psi.ArgumentList.Add(paths.Root);
		using Process process = new Process
		{
			StartInfo = psi
		};
		StringBuilder output = new StringBuilder();
		int linesSeen = 0;
		process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				output.AppendLine(e.Data);
				log?.Report(e.Data);
				int n = Interlocked.Increment(ref linesSeen);
				int pct = Math.Min(96, 55 + n / 6);
				progress?.Report(new DownloadProgress("NeoForge Patching", pct, 100, 0L, (e.Data.Length > 72) ? (e.Data.Substring(0, 72) + "…") : e.Data));
			}
		};
		process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				output.AppendLine(e.Data);
				log?.Report(e.Data);
			}
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromMinutes(6.0));
		await process.WaitForExitAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
		string? installedId = FindInstalledId(mcVersion, resolvedNeo, paths);
		if (installedId == null)
		{
			string tail = output.ToString().Trim();
			if (tail.Length > 600)
			{
				tail = tail.Substring(tail.Length - 600);
			}
			throw new InvalidOperationException("NeoForge installer exited without writing version JSON. Output tail: " + tail);
		}
		progress?.Report(new DownloadProgress("NeoForge", 100, 100, 0L, installedId + " installed"));
		log?.Report("NeoForge " + installedId + " installed");
		return installedId;
	}
}
