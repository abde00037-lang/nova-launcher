using System;
using System.Globalization;

namespace Nova.Core;

public sealed class MinecraftVersion
{
	public string Id { get; init; } = "";

	public DateTime ReleaseUtc { get; init; }

	public bool Installed { get; set; }

	public string? ForgeRecommended { get; init; }

	public string? ForgeLatest { get; init; }

	public bool HasFabric { get; init; }

	public string? NeoForgeLatest { get; init; }

	public bool HasNeoForge => !string.IsNullOrEmpty(NeoForgeLatest);

	public bool HasForge
	{
		get
		{
			if (ForgeRecommended == null)
			{
				return ForgeLatest != null;
			}
			return true;
		}
	}

	public string ReleaseLabel
	{
		get
		{
			if (!(ReleaseUtc == default(DateTime)))
			{
				return ReleaseUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			}
			return "";
		}
	}

	public string ForgeLabel
	{
		get
		{
			if (ForgeRecommended != null)
			{
				return "forge " + ForgeRecommended + " (recommended)";
			}
			if (ForgeLatest != null)
			{
				return "forge " + ForgeLatest + " (latest)";
			}
			return "no forge build";
		}
	}
}
