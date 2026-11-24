using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class DownloadEntry
{
	[JsonPropertyName("path")]
	public string? Path { get; set; }

	[JsonPropertyName("sha1")]
	public string? Sha1 { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("url")]
	public string? Url { get; set; }
}
