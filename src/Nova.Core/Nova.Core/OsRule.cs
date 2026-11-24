using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class OsRule
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("arch")]
	public string? Arch { get; set; }

	[JsonPropertyName("version")]
	public string? Version { get; set; }
}
