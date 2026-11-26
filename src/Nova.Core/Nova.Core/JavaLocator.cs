using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nova.Core;

public static class JavaLocator
{
	private static List<JavaInstall>? _cache;

	private static readonly List<string> ExtraRoots = new List<string>();

	public static void RegisterRoot(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			return;
		}
		lock (ExtraRoots)
		{
			if (!ExtraRoots.Contains(directory, StringComparer.OrdinalIgnoreCase))
			{
				ExtraRoots.Add(directory);
			}
		}
		_cache = null;
	}

	public static IReadOnlyList<JavaInstall> Discover(bool refresh = false)
	{
		if (_cache != null && !refresh)
		{
			return _cache;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array = new string[3] { "JAVA_HOME", "JAVA_JDK_HOME", "JDK_HOME" };
		for (int i = 0; i < array.Length; i++)
		{
			string environmentVariable = Environment.GetEnvironmentVariable(array[i]);
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				AddIfExists(hashSet, Path.Combine(environmentVariable, "bin", "java.exe"));
			}
		}
		List<string> list = new List<string> { Path.Combine(AppContext.BaseDirectory, "..", "java"), "D:\\nova\\java", "C:\\Program Files\\Java", "C:\\Program Files\\Eclipse Adoptium", "C:\\Program Files\\Microsoft", "C:\\Program Files\\Zulu", "C:\\Program Files (x86)\\Java", "D:\\jdk8", "D:\\gradle_home\\jdks", "D:\\gradle-home\\jdks", "D:\\Java" };
		lock (ExtraRoots)
		{
			list.AddRange(ExtraRoots);
		}
		foreach (string item in list)
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			AddIfExists(hashSet, Path.Combine(item, "bin", "java.exe"));
			foreach (string item2 in SafeDirs(item))
			{
				AddIfExists(hashSet, Path.Combine(item2, "bin", "java.exe"));
				foreach (string item3 in SafeDirs(item2))
				{
					AddIfExists(hashSet, Path.Combine(item3, "bin", "java.exe"));
				}
			}
		}
		List<JavaInstall> list2 = new List<JavaInstall>();
		foreach (string item4 in hashSet)
		{
			JavaInstall javaInstall = Probe(item4);
			if ((object)javaInstall != null)
			{
				list2.Add(javaInstall);
			}
		}
		list2 = (from g in list2.Where((JavaInstall j) => !j.Path.Replace('/', '\\').EndsWith("\\jre\\bin\\java.exe", StringComparison.OrdinalIgnoreCase)).GroupBy((JavaInstall j) => j.Path, StringComparer.OrdinalIgnoreCase)
			select g.First()).ToList();
		_cache = list2.OrderByDescending((JavaInstall j) => j.Major).ToList();
		return _cache;
	}

	public static JavaInstall? Pick(int requiredMajor)
	{
		IReadOnlyList<JavaInstall> source = Discover();
		JavaInstall javaInstall = source.FirstOrDefault((JavaInstall j) => j.Major == requiredMajor);
		if ((object)javaInstall != null)
		{
			return javaInstall;
		}
		JavaInstall javaInstall2 = (from j in source
			where j.Major > requiredMajor
			orderby j.Major
			select j).FirstOrDefault();
		if ((object)javaInstall2 != null && requiredMajor >= 17)
		{
			return javaInstall2;
		}
		return source.FirstOrDefault();
	}

	public static JavaInstall? Probe(string javaPath, string source = "scan")
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo(javaPath)
			{
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				ArgumentList = { "-version" }
			});
			if (process == null)
			{
				return null;
			}
			string input = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
			if (!process.WaitForExit(8000))
			{
				try
				{
					process.Kill(entireProcessTree: true);
					process.WaitForExit(3000);
				}
				catch
				{
				}
				return null;
			}
			Match match = Regex.Match(input, "version \"([^\"]+)\"");
			string text = (match.Success ? match.Groups[1].Value : "unknown");
			int major = ParseMajor(text);
			return new JavaInstall(javaPath, major, text, source);
		}
		catch
		{
			return null;
		}
	}

	public static int ParseMajor(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0;
		}
		string[] array = raw.Split('.', '_', '-');
		if (array.Length >= 2 && array[0] == "1" && int.TryParse(array[1], out var result))
		{
			return result;
		}
		if (!int.TryParse(array[0], out var result2))
		{
			return 0;
		}
		return result2;
	}

	private static IEnumerable<string> SafeDirs(string path)
	{
		try
		{
			return Directory.GetDirectories(path);
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static void AddIfExists(HashSet<string> set, string path)
	{
		if (File.Exists(path))
		{
			set.Add(path);
		}
	}
}
