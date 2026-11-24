using System;
using System.Collections.Generic;

namespace Nova.Core;

public sealed class InstanceConfig
{
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public string GameDirectory { get; set; } = "";

	public string VersionId { get; set; } = "";

	public string? VersionHome { get; set; }

	public string? LibrariesHome { get; set; }

	public string? AssetsDirectory { get; set; }

	public string? JavaPath { get; set; }

	public int MemoryMb { get; set; } = 4096;

	public List<string> JvmArgs { get; set; } = new List<string>();

	public List<string> GameArgs { get; set; } = new List<string>();

	public string PlayerName { get; set; } = "Player";

	public bool BackupWorldsBeforeLaunch { get; set; }

	public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

	public DateTime? LastPlayedUtc { get; set; }

	public string? LastExitSummary { get; set; }
}
