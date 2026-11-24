using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class AssetIndexJson
{
	[JsonPropertyName("objects")]
	public Dictionary<string, AssetObject>? Objects { get; set; }

	[JsonPropertyName("virtual")]
	public bool Virtual { get; set; }

	[JsonPropertyName("map_to_resources")]
	public bool MapToResources { get; set; }
}
