using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class CurseManifest
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("version")]
	public string? Version { get; set; }

	[JsonPropertyName("minecraft")]
	public CurseMinecraft? Minecraft { get; set; }

	[JsonPropertyName("files")]
	public List<CurseFile>? Files { get; set; }
}
