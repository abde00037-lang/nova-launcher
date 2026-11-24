using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class VersionManifest
{
	[JsonPropertyName("latest")]
	public ManifestLatest? Latest { get; set; }

	[JsonPropertyName("versions")]
	public List<ManifestVersion>? Versions { get; set; }
}
