using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class JavaVersionBlock
{
	[JsonPropertyName("component")]
	public string? Component { get; set; }

	[JsonPropertyName("majorVersion")]
	public int MajorVersion { get; set; }
}
