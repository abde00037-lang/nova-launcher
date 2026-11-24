using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class AssetObject
{
	[JsonPropertyName("hash")]
	public string Hash { get; set; } = "";

	[JsonPropertyName("size")]
	public long Size { get; set; }
}
