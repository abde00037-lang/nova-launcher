using System;
using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class NovaPackManifest
{
	[JsonPropertyName("format")]
	public string? Format { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("versionId")]
	public string? VersionId { get; set; }

	[JsonPropertyName("mcVersion")]
	public string? McVersion { get; set; }

	[JsonPropertyName("forgeVersion")]
	public string? ForgeVersion { get; set; }

	[JsonPropertyName("playerName")]
	public string? PlayerName { get; set; }

	[JsonPropertyName("memoryMb")]
	public int MemoryMb { get; set; }

	[JsonPropertyName("includesWorlds")]
	public bool IncludesWorlds { get; set; }

	[JsonPropertyName("modCount")]
	public int ModCount { get; set; }

	[JsonPropertyName("exportedUtc")]
	public DateTime ExportedUtc { get; set; }
}
