using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class VersionJson
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("inheritsFrom")]
	public string? InheritsFrom { get; set; }

	[JsonPropertyName("type")]
	public string? Type { get; set; }

	[JsonPropertyName("mainClass")]
	public string? MainClass { get; set; }

	[JsonPropertyName("assets")]
	public string? Assets { get; set; }

	[JsonPropertyName("assetIndex")]
	public AssetIndexRef? AssetIndex { get; set; }

	[JsonPropertyName("downloads")]
	public VersionDownloads? Downloads { get; set; }

	[JsonPropertyName("libraries")]
	public List<LibraryEntry>? Libraries { get; set; }

	[JsonPropertyName("minecraftArguments")]
	public string? MinecraftArguments { get; set; }

	[JsonPropertyName("arguments")]
	public ArgumentsBlock? Arguments { get; set; }

	[JsonPropertyName("javaVersion")]
	public JavaVersionBlock? JavaVersion { get; set; }

	[JsonPropertyName("jar")]
	public string? Jar { get; set; }
}
