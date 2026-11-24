using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class CurseMinecraft
{
	[JsonPropertyName("version")]
	public string? Version { get; set; }

	[JsonPropertyName("modLoaders")]
	public List<CurseLoader>? ModLoaders { get; set; }
}
