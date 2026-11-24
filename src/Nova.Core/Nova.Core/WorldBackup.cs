using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Nova.Core;

public static class WorldBackup
{
	public const string ManifestName = "nova-backup.json";

	public static string BackupRoot(LauncherPaths paths, string instanceId)
	{
		return Path.Combine(paths.Root, "backups", instanceId);
	}

	public static List<string> ListWorlds(string gameDirectory)
	{
		string path = Path.Combine(gameDirectory, "saves");
		if (!Directory.Exists(path))
		{
			return new List<string>();
		}
		return (from d in Directory.GetDirectories(path)
			where File.Exists(Path.Combine(d, "level.dat"))
			select Path.GetFileName(d)).OrderBy((string n) => n, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static BackupInfo Create(LauncherPaths paths, InstanceConfig instance, string worldName, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		string text = Path.Combine(instance.GameDirectory, "saves", worldName);
		if (!Directory.Exists(text))
		{
			throw new DirectoryNotFoundException("world not found: " + text);
		}
		string text2 = BackupRoot(paths, instance.Id);
		Directory.CreateDirectory(text2);
		string text3 = UniquePath(text2, string.Concat(str2: DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"), str0: SafeName(worldName), str1: "__", str3: ".zip"));
		string text4 = text3 + ".part";
		ct.ThrowIfCancellationRequested();
		log?.Report("backing up '" + worldName + "' ...");
		string[] files = Directory.GetFiles(text, "*", SearchOption.AllDirectories);
		long totalBytes = files.Sum((string f) => new FileInfo(f).Length);
		ZipFile.CreateFromDirectory(text, text4, CompressionLevel.SmallestSize, includeBaseDirectory: false);
		BackupManifest backupManifest = new BackupManifest
		{
			WorldName = worldName,
			InstanceId = instance.Id,
			VersionId = instance.VersionId,
			CreatedUtc = DateTime.UtcNow,
			Source = text,
			FileCount = files.Length,
			TotalBytes = totalBytes
		};
		using (ZipArchive zipArchive = ZipFile.Open(text4, ZipArchiveMode.Update))
		{
			using Stream stream = zipArchive.CreateEntry("nova-backup.json", CompressionLevel.NoCompression).Open();
			using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			streamWriter.Write(JsonSerializer.Serialize(backupManifest, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		File.Move(text4, text3, overwrite: true);
		string text5 = Sha256OfFile(text3);
		File.WriteAllText(text3 + ".sha256", text5);
		BackupInfo backupInfo = new BackupInfo
		{
			FilePath = text3,
			WorldName = worldName,
			InstanceId = instance.Id,
			CreatedUtc = backupManifest.CreatedUtc,
			Bytes = new FileInfo(text3).Length,
			FileCount = files.Length,
			Sha256 = text5
		};
		log?.Report($"{worldName}: {backupInfo.Megabytes} MB from {files.Length} files -> {Path.GetFileName(text3)}");
		return backupInfo;
	}

	public static List<BackupInfo> CreateChanged(LauncherPaths paths, InstanceConfig instance, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		List<BackupInfo> list = new List<BackupInfo>();
		List<BackupInfo> source = List(paths, instance.Id);
		foreach (string world in ListWorlds(instance.GameDirectory))
		{
			DateTime dateTime = Directory.GetFiles(Path.Combine(instance.GameDirectory, "saves", world), "*", SearchOption.AllDirectories).Max((string f) => new FileInfo(f).LastWriteTimeUtc);
			BackupInfo backupInfo = (from b in source
				where b.WorldName.Equals(world, StringComparison.OrdinalIgnoreCase)
				orderby b.CreatedUtc descending
				select b).FirstOrDefault();
			if (backupInfo != null && dateTime <= backupInfo.CreatedUtc)
			{
				log?.Report($"{world}: unchanged since {backupInfo.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}, skipped");
			}
			else
			{
				list.Add(Create(paths, instance, world, log, ct));
			}
		}
		return list;
	}

	public static List<BackupInfo> List(LauncherPaths paths, string instanceId)
	{
		string path = BackupRoot(paths, instanceId);
		if (!Directory.Exists(path))
		{
			return new List<BackupInfo>();
		}
		List<BackupInfo> list = new List<BackupInfo>();
		string[] files = Directory.GetFiles(path, "*.zip");
		foreach (string text in files)
		{
			BackupInfo backupInfo = new BackupInfo
			{
				FilePath = text,
				InstanceId = instanceId,
				CreatedUtc = File.GetLastWriteTimeUtc(text),
				Bytes = new FileInfo(text).Length
			};
			try
			{
				using ZipArchive zipArchive = ZipFile.OpenRead(text);
				ZipArchiveEntry entry = zipArchive.GetEntry("nova-backup.json");
				if (entry != null)
				{
					using StreamReader streamReader = new StreamReader(entry.Open());
					BackupManifest backupManifest = JsonSerializer.Deserialize<BackupManifest>(streamReader.ReadToEnd());
					if (backupManifest != null)
					{
						backupInfo.WorldName = backupManifest.WorldName;
						backupInfo.CreatedUtc = backupManifest.CreatedUtc;
						backupInfo.FileCount = backupManifest.FileCount;
					}
				}
			}
			catch
			{
			}
			if (string.IsNullOrEmpty(backupInfo.WorldName))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
				int num = fileNameWithoutExtension.LastIndexOf("__", StringComparison.Ordinal);
				backupInfo.WorldName = ((num > 0) ? fileNameWithoutExtension.Substring(0, num) : fileNameWithoutExtension);
			}
			string path2 = text + ".sha256";
			if (File.Exists(path2))
			{
				backupInfo.Sha256 = File.ReadAllText(path2).Trim();
			}
			list.Add(backupInfo);
		}
		return list.OrderByDescending((BackupInfo b) => b.CreatedUtc).ToList();
	}

	public static bool Verify(BackupInfo backup, out string message)
	{
		try
		{
			if (!File.Exists(backup.FilePath))
			{
				message = "file missing";
				return false;
			}
			if (!string.IsNullOrEmpty(backup.Sha256) && !string.Equals(Sha256OfFile(backup.FilePath), backup.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				message = "SHA256 mismatch - the backup file is damaged";
				return false;
			}
			using ZipArchive zipArchive = ZipFile.OpenRead(backup.FilePath);
			if (zipArchive.GetEntry("nova-backup.json") == null)
			{
				message = "no manifest (old or foreign zip)";
				return false;
			}
			if (zipArchive.GetEntry("level.dat") == null)
			{
				message = "no level.dat inside - not a world backup";
				return false;
			}
			message = $"{zipArchive.Entries.Count} entries, hash ok";
			return true;
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
	}

	public static string Restore(LauncherPaths paths, InstanceConfig instance, BackupInfo backup, string? targetName = null, bool replace = false, IProgress<string>? log = null, ArchiveLimits? limits = null)
	{
		string text = Path.Combine(instance.GameDirectory, "saves");
		Directory.CreateDirectory(text);
		string text2 = targetName ?? (backup.WorldName + " (restored " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ")");
		string text3 = SafePath.ResolveWithin(text, text2);
		string text4 = Path.Combine(paths.Root, "cache", "restore-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		log?.Report("extracting to a temporary folder ...");
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(backup.FilePath);
			limits ??= ArchiveLimits.Default;
			SafeArchive.Extract(archive, text4, "", limits, new ArchiveBudget(limits));
		}
		catch
		{
			if (Directory.Exists(text4)) Directory.Delete(text4, true);
			throw;
		}
		File.Delete(Path.Combine(text4, "nova-backup.json"));
		if (Directory.Exists(text3))
		{
			if (!replace)
			{
				text3 = UniqueDirectory(text, text2);
				log?.Report("target name was taken, using: " + Path.GetFileName(text3));
			}
			else
			{
				string text5 = Path.Combine(text, Path.GetFileName(text3) + ".replaced-" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
				log?.Report("renaming the current world to: " + Path.GetFileName(text5) + " (nothing is deleted)");
				Directory.Move(text3, text5);
			}
		}
		Directory.Move(text4, text3);
		log?.Report("restored to " + text3);
		return text3;
	}

	public static List<BackupInfo> Prune(LauncherPaths paths, string instanceId, int keepPerWorld, bool apply, IProgress<string>? log = null)
	{
		List<BackupInfo> list = new List<BackupInfo>();
		foreach (IGrouping<string, BackupInfo> item in List(paths, instanceId).GroupBy((BackupInfo b) => b.WorldName, StringComparer.OrdinalIgnoreCase))
		{
			foreach (BackupInfo item2 in item.OrderByDescending((BackupInfo b) => b.CreatedUtc).Skip(Math.Max(1, keepPerWorld)))
			{
				list.Add(item2);
				log?.Report((apply ? "deleted " : "would delete ") + Path.GetFileName(item2.FilePath));
				if (!apply)
				{
					continue;
				}
				try
				{
					File.Delete(item2.FilePath);
					string path = item2.FilePath + ".sha256";
					if (File.Exists(path))
					{
						File.Delete(path);
					}
				}
				catch (Exception ex)
				{
					log?.Report("could not delete " + item2.FileName + ": " + ex.Message);
				}
			}
		}
		return list;
	}

	public static string SafeName(string name)
	{
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		StringBuilder stringBuilder = new StringBuilder(name.Length);
		foreach (char c in name)
		{
			stringBuilder.Append(Enumerable.Contains(invalidFileNameChars, c) ? '_' : c);
		}
		string text = stringBuilder.ToString().Trim().TrimEnd('.');
		if (text.Length != 0)
		{
			if (text.Length <= 60)
			{
				return text;
			}
			return text.Substring(0, 60);
		}
		return "world";
	}

	private static string UniquePath(string directory, string fileName)
	{
		string text = Path.Combine(directory, fileName);
		if (!File.Exists(text))
		{
			return text;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		string extension = Path.GetExtension(fileName);
		int num = 2;
		string text2;
		while (true)
		{
			text2 = Path.Combine(directory, fileNameWithoutExtension + "-" + num + extension);
			if (!File.Exists(text2))
			{
				break;
			}
			num++;
		}
		return text2;
	}

	private static string UniqueDirectory(string parent, string name)
	{
		string text = Path.Combine(parent, name);
		int num = 2;
		while (Directory.Exists(text))
		{
			text = Path.Combine(parent, name + " " + num);
			num++;
		}
		return text;
	}

	private static string Sha256OfFile(string path)
	{
		using FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		using SHA256 sHA = SHA256.Create();
		return Convert.ToHexString(sHA.ComputeHash(inputStream)).ToLowerInvariant();
	}
}
