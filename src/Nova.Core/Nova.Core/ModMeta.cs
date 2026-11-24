using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class ModMeta
{
	[JsonPropertyName("title")]
	public string Title { get; set; } = "";

	[JsonPropertyName("description")]
	public string Description { get; set; } = "";

	[JsonPropertyName("slug")]
	public string? Slug { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; } = "";

	[JsonPropertyName("icon")]
	public string? IconFile { get; set; }
}
