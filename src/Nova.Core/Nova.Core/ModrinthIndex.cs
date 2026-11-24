using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class ModrinthIndex
{
	[JsonPropertyName("formatVersion")]
	public int FormatVersion { get; set; }

	[JsonPropertyName("game")]
	public string? Game { get; set; }

	[JsonPropertyName("versionId")]
	public string? VersionId { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("summary")]
	public string? Summary { get; set; }

	[JsonPropertyName("files")]
	public List<ModrinthFile>? Files { get; set; }

	[JsonPropertyName("dependencies")]
	public Dictionary<string, string>? Dependencies { get; set; }
}
