using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class CurseLoader
{
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	[JsonPropertyName("primary")]
	public bool Primary { get; set; }
}
