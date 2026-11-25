using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nova.Core;

public static class LaunchBuilder
{
	public static LaunchPlan Build(InstanceConfig instance, ResolvedVersion version, LauncherPaths paths, JavaInstall java, string nativesDirectory, PlayerIdentity? identity = null, string? quickPlayWorld = null)
	{
		LaunchPlan launchPlan = new LaunchPlan
		{
			JavaPath = java.Path,
			WorkingDirectory = instance.GameDirectory
		};
		string text = instance.LibrariesHome;
		text = Path.GetFullPath(string.IsNullOrWhiteSpace(text) ? paths.Libraries : text).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string path = (string.IsNullOrWhiteSpace(instance.VersionHome) ? paths.Versions : instance.VersionHome);
		string text2 = (string.IsNullOrWhiteSpace(instance.AssetsDirectory) ? paths.Assets : instance.AssetsDirectory);
		string fullNatives = Path.GetFullPath(nativesDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string fullGameDir = Path.GetFullPath(instance.GameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		List<string> list = new List<string>();
		foreach (ResolvedFile file in version.Files)
		{
			list.Add(Path.Combine(text, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
		}
		string item = Path.Combine(path, version.ClientJarVersionId, version.ClientJarVersionId + ".jar");
		list.Add(item);
		string text3 = identity?.Name ?? (string.IsNullOrWhiteSpace(instance.PlayerName) ? "Player" : instance.PlayerName);
		Dictionary<string, string> placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["${auth_player_name}"] = text3,
			["${version_name}"] = version.Id,
			["${game_directory}"] = fullGameDir,
			["${assets_root}"] = text2,
			["${game_assets}"] = Path.Combine(text2, "virtual", "legacy"),
			["${assets_index_name}"] = version.AssetIndexId,
			["${auth_uuid}"] = identity?.Uuid ?? OfflineUuid(text3),
			["${auth_access_token}"] = identity?.AccessToken ?? "0",
			["${auth_session}"] = identity?.AccessToken ?? "-",
			["${user_type}"] = identity?.UserType ?? "legacy",
			["${auth_xuid}"] = identity?.Xuid ?? "",
			["${clientid}"] = ((identity?.UserType == "msa") ? Guid.NewGuid().ToString() : ""),
			["${user_properties}"] = "{}",
			["${version_type}"] = "release",
			["${natives_directory}"] = fullNatives,
			["${launcher_name}"] = "Nova",
			["${launcher_version}"] = "0.2",
			["${classpath}"] = string.Join(Path.PathSeparator, list),
			["${classpath_separator}"] = Path.PathSeparator.ToString(),
			["${library_directory}"] = text,
			["${primary_jar}"] = item,
			["${primary_jar_name}"] = Path.GetFileName(item),
			["${resolution_width}"] = "",
			["${resolution_height}"] = ""
		};
		launchPlan.Arguments.Add("-Xmx" + Math.Max(512, instance.MemoryMb) + "M");
		launchPlan.Arguments.Add("-Xms" + Math.Max(256, Math.Min(instance.MemoryMb / 2, 2048)) + "M");
		if (instance.MemoryMb >= 4096)
		{
			launchPlan.Arguments.Add("-XX:+UnlockExperimentalVMOptions");
			launchPlan.Arguments.Add("-XX:+UseG1GC");
			launchPlan.Arguments.Add("-XX:+ParallelRefProcEnabled");
			launchPlan.Arguments.Add("-XX:MaxGCPauseMillis=50");
			launchPlan.Arguments.Add("-XX:G1NewSizePercent=20");
			launchPlan.Arguments.Add("-XX:G1ReservePercent=20");
			launchPlan.Arguments.Add("-XX:G1HeapRegionSize=32M");
		}
		launchPlan.Arguments.Add("-Dfile.encoding=UTF-8");
		if (version.JvmArgs.Count == 0)
		{
			launchPlan.Arguments.Add("-Djava.library.path=" + nativesDirectory);
			launchPlan.Arguments.Add("-Dfml.ignoreInvalidMinecraftCertificates=true");
			launchPlan.Arguments.Add("-Dfml.ignorePatchDiscrepancies=true");
			launchPlan.Arguments.Add("-Dminecraft.launcher.brand=Nova");
			launchPlan.Arguments.Add("-Dminecraft.launcher.version=0.1");
		}
		foreach (string jvmArg in instance.JvmArgs)
		{
			if (!string.IsNullOrWhiteSpace(jvmArg))
			{
				launchPlan.Arguments.Add(jvmArg);
			}
		}
		List<string> list2 = (from a in version.JvmArgs
			select Substitute(a, placeholders) into a
			where !string.IsNullOrEmpty(a)
			select a).ToList();
		bool flag = version.JvmArgs.Any((string a) => a == "-cp" || a == "-classpath" || a.Contains("${classpath}"));
		launchPlan.Arguments.AddRange(list2);
		if (!flag)
		{
			launchPlan.Arguments.Add("-cp");
			launchPlan.Arguments.Add(string.Join(Path.PathSeparator, list));
		}
		launchPlan.Arguments.Add(version.MainClass);
		List<string> list3 = version.GameArgs.Select((string a) => Substitute(a, placeholders)).ToList();
		List<string> list4 = new List<string>();
		foreach (string item2 in list3)
		{
			if (item2.Length == 0)
			{
				if (launchPlan.Arguments.Count > 0)
				{
					List<string> arguments = launchPlan.Arguments;
					if (arguments[arguments.Count - 1].StartsWith("--"))
					{
						List<string> arguments2 = launchPlan.Arguments;
						if (arguments2[arguments2.Count - 1] != version.MainClass)
						{
							List<string> arguments3 = launchPlan.Arguments;
							list4.Add(arguments3[arguments3.Count - 1]);
						}
					}
				}
				if (list4.Count > 0)
				{
					launchPlan.Arguments.RemoveAt(launchPlan.Arguments.Count - 1);
				}
			}
			else
			{
				launchPlan.Arguments.Add(item2);
			}
		}
		if (list4.Count > 0)
		{
			launchPlan.Notes.Add("dropped arguments with no offline value: " + string.Join(", ", list4.Distinct()));
		}
		foreach (string gameArg in instance.GameArgs)
		{
			if (!string.IsNullOrWhiteSpace(gameArg))
			{
				launchPlan.Arguments.Add(gameArg);
			}
		}
		if (quickPlayWorld != null)
		{
			if (!version.SupportsQuickPlaySingleplayer) throw new InvalidOperationException("Direct world join is not supported by this Minecraft version; open the world from the game menu.");
			WorldManager.ResolveWorld(instance.GameDirectory, quickPlayWorld);
			if (instance.GameArgs.Concat(version.GameArgs).Any(a => a.StartsWith("--quickPlay", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("Custom quick-play arguments conflict with the selected world.");
			launchPlan.Arguments.Add("--quickPlaySingleplayer");
			launchPlan.Arguments.Add(quickPlayWorld);
		}
		if (!version.GameArgs.Any((string a) => a.Contains("${assets_index_name}")))
		{
			launchPlan.Notes.Add("version JSON has no --assetIndex argument; the game may ignore the asset index");
		}
		return launchPlan;
	}

	private static string Substitute(string token, Dictionary<string, string> placeholders)
	{
		string text = token;
		foreach (var (text4, newValue) in placeholders)
		{
			if (text.Contains(text4))
			{
				text = text.Replace(text4, newValue);
			}
		}
		if (!text.Contains("${"))
		{
			return text;
		}
		return "";
	}

	public static string OfflineUuid(string name)
	{
		byte[] array = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
		array[6] = (byte)((array[6] & 0xF) | 0x30);
		array[8] = (byte)((array[8] & 0x3F) | 0x80);
		string text = Convert.ToHexString(array).ToLowerInvariant();
		string[] array2 = new string[9]
		{
			text.Substring(0, 8),
			"-",
			text.Substring(8, 4),
			"-",
			text.Substring(12, 4),
			"-",
			text.Substring(16, 4),
			"-",
			null
		};
		string text2 = text;
		array2[8] = text2.Substring(20, text2.Length - 20);
		return string.Concat(array2);
	}

	public static int RecommendMemoryMb(int modCount)
	{
		if (modCount <= 120)
		{
			if (modCount > 20)
			{
				if (modCount <= 60)
				{
					return 3072;
				}
				return 4096;
			}
			return 2048;
		}
		if (modCount <= 250)
		{
			return 6144;
		}
		return 8192;
	}
}
