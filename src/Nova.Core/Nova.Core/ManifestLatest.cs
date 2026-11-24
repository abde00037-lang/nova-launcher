using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class ManifestLatest
{
	[JsonPropertyName("release")]
	public string? Release { get; set; }

	[JsonPropertyName("snapshot")]
	public string? Snapshot { get; set; }
}
