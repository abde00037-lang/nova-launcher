using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Nova.Core;

public static class PackExporter
{
	private static readonly string[] AlwaysInclude = new string[7] { "mods", "config", "defaultconfigs", "scripts", "resourcepacks", "shaderpacks", "datapacks" };

	private static readonly string[] AlwaysFiles = new string[3] { "options.txt", "servers.dat", "optionsshaders.txt" };

	public static (string Path, int Files, long Bytes) Export(InstanceConfig instance, string outputZip, bool includeWorlds = false, IProgress<string>? log = null)
	{
		if (!Directory.Exists(instance.GameDirectory))
		{
			throw new DirectoryNotFoundException("game folder does not exist: " + instance.GameDirectory);
		}
		string mcVersion = ModStore.McVersionOf(instance.VersionId);
		object? obj;
		if (!instance.VersionId.Contains("-forge-"))
		{
			obj = null;
		}
		else
		{
			string versionId = instance.VersionId;
			int num = instance.VersionId.IndexOf("-forge-", StringComparison.Ordinal) + 7;
			obj = versionId.Substring(num, versionId.Length - num);
		}
		string? forgeVersion = (string?)obj;
		string path = Path.Combine(instance.GameDirectory, "mods");
		int modCount = ModManager.CountInstalled(path);
		NovaPackManifest value = new NovaPackManifest
		{
			Format = "nova-pack/1",
			Name = instance.Name,
			VersionId = instance.VersionId,
			McVersion = mcVersion,
			ForgeVersion = forgeVersion,
			PlayerName = instance.PlayerName,
			MemoryMb = instance.MemoryMb,
			IncludesWorlds = includeWorlds,
			ModCount = modCount,
			ExportedUtc = DateTime.UtcNow
		};
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputZip)));
		if (File.Exists(outputZip))
		{
			File.Delete(outputZip);
		}
		int num2 = 0;
		long num3 = 0L;
		using (ZipArchive zipArchive = ZipFile.Open(outputZip, ZipArchiveMode.Create))
		{
			using (Stream stream = zipArchive.CreateEntry("nova-pack.json", CompressionLevel.Optimal).Open())
			{
				using StreamWriter streamWriter = new StreamWriter(stream);
				streamWriter.Write(JsonSerializer.Serialize(value, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
			List<string> list = new List<string>(AlwaysInclude);
			if (includeWorlds)
			{
				list.Add("saves");
			}
			foreach (string item in list)
			{
				string path2 = Path.Combine(instance.GameDirectory, item);
				if (!Directory.Exists(path2))
				{
					continue;
				}
				foreach (string item2 in Directory.EnumerateFiles(path2, "*", SearchOption.AllDirectories))
				{
					string entryName = Path.GetRelativePath(instance.GameDirectory, item2).Replace('\\', '/');
					using (Stream destination = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal).Open())
					{
						using FileStream fileStream = File.OpenRead(item2);
						fileStream.CopyTo(destination);
						num3 += fileStream.Length;
					}
					num2++;
				}
				log?.Report(item + ": packed");
			}
			string[] alwaysFiles = AlwaysFiles;
			foreach (string text in alwaysFiles)
			{
				string path3 = Path.Combine(instance.GameDirectory, text);
				if (!File.Exists(path3))
				{
					continue;
				}
				using (Stream destination2 = zipArchive.CreateEntry(text, CompressionLevel.Optimal).Open())
				{
					using FileStream fileStream2 = File.OpenRead(path3);
					fileStream2.CopyTo(destination2);
					num3 += fileStream2.Length;
				}
				num2++;
			}
		}
		log?.Report($"exported {num2} file(s), {new FileInfo(outputZip).Length / 1024 / 1024} MB");
		return (Path: outputZip, Files: num2, Bytes: new FileInfo(outputZip).Length);
	}
}
