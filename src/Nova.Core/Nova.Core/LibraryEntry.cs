using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class LibraryEntry
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("url")]
	public string? Url { get; set; }

	[JsonPropertyName("downloads")]
	public LibraryDownloads? Downloads { get; set; }

	[JsonPropertyName("natives")]
	public Dictionary<string, string>? Natives { get; set; }

	[JsonPropertyName("rules")]
	public List<Rule>? Rules { get; set; }

	[JsonPropertyName("extract")]
	public ExtractRule? Extract { get; set; }
}
