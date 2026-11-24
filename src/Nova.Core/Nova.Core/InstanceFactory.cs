using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class InstanceFactory
{
	public static async Task<InstanceConfig> CreateAsync(LauncherPaths paths, string name, string versionId, int memoryMb = 4096, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken), IProgress<DownloadProgress>? progress = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("a name is required");
		}
		if (string.IsNullOrWhiteSpace(versionId))
		{
			throw new ArgumentException("a version is required");
		}
		paths.EnsureCreated();
		string actualVersionId = versionId;
		if (versionId.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
		{
			string mc = ModStore.McVersionOf(versionId);
			string rest = versionId.Substring("fabric-loader-".Length);
			int idx = rest.LastIndexOf('-');
			string? loaderVer = ((idx > 0) ? rest.Substring(0, idx) : null);
			actualVersionId = await FabricInstaller.InstallAsync(mc, loaderVer, paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		}
		else if (versionId.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase))
		{
			string neoVer = versionId.Substring("neoforge-".Length);
			string mc2 = NeoForgeInstaller.McVersionFromNeoForge(neoVer) ?? throw new ArgumentException("cannot determine Minecraft version for NeoForge " + neoVer);
			actualVersionId = await NeoForgeInstaller.InstallAsync(mc2, neoVer, paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		}
		else if (versionId.Contains("-neoforge-", StringComparison.OrdinalIgnoreCase))
		{
			string[] parts = versionId.Split("-neoforge-", 2);
			actualVersionId = await NeoForgeInstaller.InstallAsync(parts[0], parts[1], paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		}
		else if (versionId.Contains("-forge-", StringComparison.OrdinalIgnoreCase))
		{
			string[] array = versionId.Split("-forge-", 2);
			actualVersionId = await ForgeInstaller.InstallAsync(array[0], array[1], paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			await MojangMeta.InstallAsync(versionId, paths, log, ct, progress).ConfigureAwait(continueOnCapturedContext: false);
		}
		InstanceStore instanceStore = new InstanceStore(paths);
		string text = InstanceStore.Slug(name);
		int num = 2;
		while (instanceStore.Find(text) != null)
		{
			text = InstanceStore.Slug(name) + "-" + num++;
		}
		string text2 = Path.Combine(instanceStore.FolderFor(text), "game");
		Directory.CreateDirectory(Path.Combine(text2, "mods"));
		Directory.CreateDirectory(Path.Combine(text2, "saves"));
		InstanceConfig instanceConfig = new InstanceConfig
		{
			Id = text,
			Name = name,
			GameDirectory = text2,
			VersionId = actualVersionId,
			MemoryMb = memoryMb,
			PlayerName = "Player",
			BackupWorldsBeforeLaunch = true,
			CreatedUtc = DateTime.UtcNow
		};
		instanceStore.Save(instanceConfig);
		log?.Report("instance created: " + text);
		return instanceConfig;
	}

	public static async Task<InstanceConfig> DuplicateAsync(LauncherPaths paths, InstanceConfig source, string newName, bool copyGameFiles, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(newName))
		{
			throw new ArgumentException("a name is required");
		}
		InstanceStore store = new InstanceStore(paths);
		string id = InstanceStore.Slug(newName);
		int num = 2;
		while (store.Find(id) != null)
		{
			id = InstanceStore.Slug(newName) + "-" + num++;
		}
		string path = store.FolderFor(id);
		string gameDir = Path.Combine(path, "game");
		Directory.CreateDirectory(gameDir);
		if (copyGameFiles && Directory.Exists(source.GameDirectory))
		{
			log?.Report("copying the game folder (mods, config, resourcepacks)…");
			await Task.Run(() =>
			{
				CopyDirectory(source.GameDirectory, gameDir, ct);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
		}
		InstanceConfig instanceConfig = new InstanceConfig
		{
			Id = id,
			Name = newName,
			GameDirectory = gameDir,
			VersionId = source.VersionId,
			MemoryMb = source.MemoryMb,
			PlayerName = source.PlayerName,
			JvmArgs = source.JvmArgs.ToList(),
			GameArgs = source.GameArgs.ToList(),
			BackupWorldsBeforeLaunch = source.BackupWorldsBeforeLaunch,
			CreatedUtc = DateTime.UtcNow
		};
		store.Save(instanceConfig);
		log?.Report("copy ready: " + id);
		return instanceConfig;
	}

	private static void CopyDirectory(string from, string to, CancellationToken ct)
	{
		Directory.CreateDirectory(to);
		foreach (string item in Directory.EnumerateFiles(from))
		{
			ct.ThrowIfCancellationRequested();
			File.Copy(item, Path.Combine(to, Path.GetFileName(item)), overwrite: true);
		}
		foreach (string item2 in Directory.EnumerateDirectories(from))
		{
			ct.ThrowIfCancellationRequested();
			CopyDirectory(item2, Path.Combine(to, Path.GetFileName(item2)), ct);
		}
	}
}
