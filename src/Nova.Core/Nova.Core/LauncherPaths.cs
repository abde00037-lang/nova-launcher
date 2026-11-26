using System;
using System.IO;

namespace Nova.Core;

public sealed class LauncherPaths
{
	public string Root { get; }

	public string Versions => Path.Combine(Root, "versions");

	public string Libraries => Path.Combine(Root, "libraries");

	public string Assets => Path.Combine(Root, "assets");

	public string Natives => Path.Combine(Root, "natives");

	public string Instances => Path.Combine(Root, "instances");

	public string JavaHome => Path.Combine(Root, "java");

	public string Cache => Path.Combine(Root, "cache");

	public LauncherPaths(string root)
	{
		Root = Path.GetFullPath(root);
	}

	public void EnsureCreated()
	{
		Directory.CreateDirectory(Versions);
		Directory.CreateDirectory(Libraries);
		Directory.CreateDirectory(Assets);
		Directory.CreateDirectory(Instances);
		Directory.CreateDirectory(JavaHome);
		Directory.CreateDirectory(Cache);
	}

	public string VersionDir(string versionId)
	{
		return Path.Combine(Versions, versionId);
	}

	public string VersionJson(string versionId)
	{
		return Path.Combine(VersionDir(versionId), versionId + ".json");
	}

	public string VersionJar(string versionId)
	{
		return Path.Combine(VersionDir(versionId), versionId + ".jar");
	}

	public static LauncherPaths Default()
	{
		// CLI and UI share one root: running from a launcher bin folder targets its parent,
		// so nova-cli never forks data into bin\data anymore.
		string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(Path.GetFileName(baseDir), "bin", StringComparison.OrdinalIgnoreCase))
		{
			string? parent = Path.GetDirectoryName(baseDir);
			if (!string.IsNullOrEmpty(parent)) return new LauncherPaths(parent);
		}
		return new LauncherPaths(Path.Combine(AppContext.BaseDirectory, "data"));
	}
}
