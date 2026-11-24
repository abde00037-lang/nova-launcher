using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nova.Core;

public static class PackInspector
{
	public const string NovaPackEntry = "nova-pack.json";

	public const string ModrinthEntry = "modrinth.index.json";

	public const string CurseEntry = "manifest.json";

	public static PackInfo Inspect(string zipPath, ArchiveLimits? limits = null)
	{
		PackInfo packInfo = new PackInfo
		{
			SourceFile = zipPath,
			Name = Path.GetFileNameWithoutExtension(zipPath)
		};
		if (!File.Exists(zipPath))
		{
			packInfo.Notes.Add("file does not exist");
			return packInfo;
		}
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		(limits ?? ArchiveLimits.Default).ValidateArchive(zipArchive);
		ZipArchiveEntry entry = zipArchive.GetEntry("nova-pack.json");
		if (entry != null)
		{
			NovaPackManifest novaPackManifest = ReadJson<NovaPackManifest>(entry, (limits ?? ArchiveLimits.Default).MaxManifestBytes) ?? new NovaPackManifest();
			packInfo.Format = PackFormat.NovaPack;
			packInfo.Name = (string.IsNullOrWhiteSpace(novaPackManifest.Name) ? packInfo.Name : novaPackManifest.Name);
			packInfo.VersionId = novaPackManifest.VersionId ?? "";
			packInfo.McVersion = novaPackManifest.McVersion ?? GuessMcVersion(novaPackManifest.VersionId);
			packInfo.Loader = (string.IsNullOrWhiteSpace(novaPackManifest.ForgeVersion) ? null : "Forge");
			packInfo.LoaderVersion = novaPackManifest.ForgeVersion;
			packInfo.OverrideFiles = CountOverrides(zipArchive, "");
			if (novaPackManifest.IncludesWorlds)
			{
				packInfo.Notes.Add("includes saved worlds");
			}
			return packInfo;
		}
		ZipArchiveEntry entry2 = zipArchive.GetEntry("modrinth.index.json");
		if (entry2 != null)
		{
			ModrinthIndex modrinthIndex = ReadJson<ModrinthIndex>(entry2, (limits ?? ArchiveLimits.Default).MaxManifestBytes) ?? new ModrinthIndex();
			packInfo.Format = PackFormat.Modrinth;
			packInfo.Name = (string.IsNullOrWhiteSpace(modrinthIndex.Name) ? packInfo.Name : modrinthIndex.Name);
			packInfo.VersionId = modrinthIndex.VersionId ?? "";
			packInfo.McVersion = ((modrinthIndex.Dependencies != null && modrinthIndex.Dependencies.TryGetValue("minecraft", out string value)) ? value : "");
			ApplyLoader(packInfo, modrinthIndex.Dependencies);
			foreach (ModrinthFile item in modrinthIndex.Files ?? new List<ModrinthFile>())
			{
				if (item.IsClientFile)
				{
					packInfo.ListedFiles++;
					packInfo.ListedBytes += item.FileSize;
					if (item.Url == null)
					{
						packInfo.Notes.Add("a listed file has no download url: " + item.Path);
					}
				}
			}
			packInfo.OverrideFiles = CountOverrides(zipArchive, "overrides/") + CountOverrides(zipArchive, "client-overrides/");
			return packInfo;
		}
		ZipArchiveEntry entry3 = zipArchive.GetEntry("manifest.json");
		if (entry3 != null)
		{
			CurseManifest curseManifest = ReadJson<CurseManifest>(entry3, (limits ?? ArchiveLimits.Default).MaxManifestBytes) ?? new CurseManifest();
			packInfo.Format = PackFormat.CurseForge;
			packInfo.Name = (string.IsNullOrWhiteSpace(curseManifest.Name) ? packInfo.Name : curseManifest.Name);
			packInfo.McVersion = curseManifest.Minecraft?.Version ?? "";
			string text = curseManifest.Minecraft?.ModLoaders?.FirstOrDefault((CurseLoader l) => l.Primary)?.Id ?? curseManifest.Minecraft?.ModLoaders?.FirstOrDefault()?.Id;
			if (!string.IsNullOrWhiteSpace(text))
			{
				int num = text.IndexOf('-');
				packInfo.Loader = ((num > 0) ? TitleCase(text.Substring(0, num)) : text);
				string loaderVersion;
				if (num <= 0)
				{
					loaderVersion = "";
				}
				else
				{
					string text2 = text;
					int num2 = num + 1;
					loaderVersion = text2.Substring(num2, text2.Length - num2);
				}
				packInfo.LoaderVersion = loaderVersion;
			}
			packInfo.ListedFiles = curseManifest.Files?.Count ?? 0;
			packInfo.Notes.Add("CurseForge ids cannot be downloaded without an API key - the overrides are installed and the ids are listed for manual download");
			packInfo.OverrideFiles = CountOverrides(zipArchive, "overrides/");
			return packInfo;
		}
		if (zipArchive.Entries.Any((ZipArchiveEntry e) => e.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)))
		{
			packInfo.Format = PackFormat.PlainZip;
			packInfo.OverrideFiles = Enumerable.Count(zipArchive.Entries, (ZipArchiveEntry e) => !e.FullName.EndsWith('/'));
			packInfo.McVersion = GuessMcVersionFromJars(zipArchive);
			packInfo.Notes.Add("no pack manifest: every file is copied as-is");
			return packInfo;
		}
		packInfo.Notes.Add("this zip does not look like a modpack");
		return packInfo;
	}

	private static void ApplyLoader(PackInfo info, Dictionary<string, string>? dependencies)
	{
		if (dependencies == null)
		{
			return;
		}
		foreach (var (text3, loaderVersion) in dependencies)
		{
			if (!text3.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
			{
				info.Loader = TitleCase(text3);
				info.LoaderVersion = loaderVersion;
				break;
			}
		}
	}

	private static string TitleCase(string raw)
	{
		if (raw.Length != 0)
		{
			return char.ToUpperInvariant(raw[0]) + raw.Substring(1, raw.Length - 1).ToLowerInvariant();
		}
		return raw;
	}

	private static string GuessMcVersion(string? versionId)
	{
		if (string.IsNullOrWhiteSpace(versionId))
		{
			return "";
		}
		int num = versionId.IndexOf("-", StringComparison.Ordinal);
		if (num <= 0)
		{
			return versionId;
		}
		return versionId.Substring(0, num);
	}

	private static string GuessMcVersionFromJars(ZipArchive zip)
	{
		foreach (ZipArchiveEntry item in zip.Entries.Where((ZipArchiveEntry e) => e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).Take(40))
		{
			Match match = Regex.Match(Path.GetFileNameWithoutExtension(item.Name), "1\\.\\d{1,2}(\\.\\d{1,2})?");
			if (match.Success)
			{
				return match.Value;
			}
		}
		return "";
	}

	private static int CountOverrides(ZipArchive zip, string prefix)
	{
		return Enumerable.Count(zip.Entries, (ZipArchiveEntry e) => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith('/'));
	}

	internal static T? ReadJson<T>(ZipArchiveEntry entry, long maxBytes = 32L * 1024 * 1024) where T : class
	{
		if (entry.Length > maxBytes) throw new InvalidDataException("Pack manifest exceeds its size limit.");
		using Stream input = entry.Open();
		using MemoryStream bounded = new MemoryStream();
		byte[] buffer = new byte[81920];
		int n;
		while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
		{
			if (n > maxBytes - bounded.Length) throw new InvalidDataException("Pack manifest exceeds its size limit.");
			bounded.Write(buffer, 0, n);
		}
		bounded.Position = 0;
		try { return JsonSerializer.Deserialize<T>(bounded, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
		catch (JsonException) { return null; }
	}
}
