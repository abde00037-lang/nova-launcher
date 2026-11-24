using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nova.Core;

public static class CrashAnalyzer
{
	private sealed record Pattern(Regex Match, string Severity, string Title, string Suggestion);

	private static readonly Pattern[] Patterns = new Pattern[13]
	{
		new Pattern(new Regex("java\\.lang\\.OutOfMemoryError|GC overhead limit exceeded", RegexOptions.IgnoreCase), "FAIL", "Out of memory", "Raise the instance memory (6 GB is the minimum for a 140 mod pack) and close other programs."),
		new Pattern(new Regex("NoClassDefFoundError: (bib|b[A-Za-z]{2}|[a-z]{3})\\b"), "FAIL", "Obfuscated class loaded into a deobfuscated environment", "A production (reobfuscated) jar is inside a development workspace, or OptiFine is loaded in a Gradle dev run. Use the real launcher, not runClient."),
		new Pattern(new Regex("UnsupportedClassVersionError|class file version (\\d+)"), "FAIL", "Wrong Java version", "1.12.2 must run on Java 8. Pick a Java 8 install for this instance."),
		new Pattern(new Regex("UnsatisfiedLinkError|Failed to locate library|no lwjgl.*in java\\.library\\.path", RegexOptions.IgnoreCase), "FAIL", "A native library could not be loaded", "The launcher did not extract the natives jars (lwjgl.dll, openal.dll) into the natives folder, or the wrong OS classifier was picked."),
		new Pattern(new Regex("Pixel format not accelerated|org\\.lwjgl\\.opengl\\.OpenGLException|GL ERROR|Failed to create display"), "FAIL", "Graphics driver rejected the window", "Update the GPU driver, or run with the dedicated GPU forced for javaw.exe."),
		new Pattern(new Regex("Missing or unsupported mandatory dependencies|MissingModsException|requires .* which is missing:", RegexOptions.IgnoreCase), "FAIL", "Missing mod dependency", "Re-enable the mod that was disabled, or install the required library again."),
		new Pattern(new Regex("Duplicate mods|duplicate modid|Found a duplicate mod", RegexOptions.IgnoreCase), "FAIL", "Duplicate mod id", "Two jars declare the same modid. Keep one version only."),
		new Pattern(new Regex("Mixin.*(apply|transform).*(failed|error)|MixinApplyError|InvalidMixinException", RegexOptions.IgnoreCase), "FAIL", "Mixin failed to apply", "Check that !mixinbooter is installed and that the mod was built for this Forge version."),
		new Pattern(new Regex("java\\.lang\\.NoSuchMethodError|java\\.lang\\.NoSuchFieldError"), "FAIL", "Binary incompatibility between mods", "Two mods expect different versions of the same library (often Forge or CoFH). Align the versions."),
		new Pattern(new Regex("java\\.lang\\.ClassNotFoundException"), "WARN", "A class disappeared while loading", "Usually a half-downloaded jar. Re-download the mod file."),
		new Pattern(new Regex("Exception in server tick loop|Ticking (entity|block)"), "WARN", "A world or entity tick crashed", "Often an old world with an entity from a removed mod. Restore a backup of that world."),
		new Pattern(new Regex("Missing sound for event"), "INFO", "Silent sounds", "Benign for mod sounds. If vanilla sounds are gone, the asset index is missing or mismatched."),
		new Pattern(new Regex("The game crashed whilst (initializing|rendering)"), "WARN", "Crash during init/render", "Read the first 'Caused by' line; the real reason is rarely the last one.")
	};

	public static List<CrashFinding> AnalyzeFile(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return new List<CrashFinding>();
			}
			return AnalyzeText(File.ReadAllText(path));
		}
		catch
		{
			return new List<CrashFinding>();
		}
	}

	public static List<CrashFinding> AnalyzeText(string text)
	{
		List<CrashFinding> list = new List<CrashFinding>();
		Pattern[] patterns = Patterns;
		foreach (Pattern pattern in patterns)
		{
			Match match = pattern.Match.Match(text);
			if (match.Success && !list.Any((CrashFinding f) => f.Title == pattern.Title))
			{
				string text2 = snippetOf(text, match.Index);
				list.Add(new CrashFinding(pattern.Severity, pattern.Title, pattern.Suggestion + "   [" + text2 + "]"));
			}
		}
		return list;
	}

	private static string snippetOf(string text, int index)
	{
		int num = Math.Max(0, index - 40);
		int length = Math.Min(140, text.Length - num);
		return Regex.Replace(text.Substring(num, length), "\\s+", " ").Trim();
	}
}
