using System;
using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class InstanceVm : ObservableObject
{
	public InstanceConfig Config { get; }

	public string Id => Config.Id;

	public string Name
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Config.Name))
			{
				return Config.Name;
			}
			return Config.Id;
		}
	}

	public string VersionId => Config.VersionId;

	public string GameDirectory => Config.GameDirectory;

	public int ModCount { get; }

	public string JavaLabel { get; }

	public int JavaMajor { get; }

	public string MemoryLabel => Config.MemoryMb + " MB";

	public string Subtitle => $"{Config.VersionId}  ·  {ModCount} mods  ·  {Config.MemoryMb} MB";

	public string LastPlayed
	{
		get
		{
			if (Config.LastPlayedUtc.HasValue)
			{
				return "last played " + Humanize(Config.LastPlayedUtc.Value);
			}
			return "never played";
		}
	}

	public string McVersion => ModStore.McVersionOf(Config.VersionId);

	public string ForgeVersion => ModStore.FormatLoaderLabel(Config.VersionId);

	public InstanceVm(InstanceConfig config, int modCount, string javaLabel, JavaInstall? java)
	{
		Config = config;
		ModCount = modCount;
		JavaLabel = javaLabel;
		JavaMajor = java?.Major ?? 0;
	}

	private static string Humanize(DateTime utc)
	{
		TimeSpan timeSpan = DateTime.UtcNow - utc;
		if (timeSpan.TotalMinutes < 1.0)
		{
			return "just now";
		}
		if (timeSpan.TotalHours < 1.0)
		{
			return $"{(int)timeSpan.TotalMinutes} min ago";
		}
		if (timeSpan.TotalDays < 1.0)
		{
			return $"{(int)timeSpan.TotalHours} hr ago";
		}
		if (timeSpan.TotalDays < 14.0)
		{
			return $"{(int)timeSpan.TotalDays} d ago";
		}
		return utc.ToLocalTime().ToString("yyyy-MM-dd");
	}
}
