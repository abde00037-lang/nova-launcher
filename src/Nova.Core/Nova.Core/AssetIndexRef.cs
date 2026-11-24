using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class AssetIndexRef
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("sha1")]
	public string? Sha1 { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("url")]
	public string? Url { get; set; }

	[JsonPropertyName("totalSize")]
	public long TotalSize { get; set; }
}
