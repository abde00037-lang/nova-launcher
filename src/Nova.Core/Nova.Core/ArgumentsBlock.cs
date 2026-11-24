using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class ArgumentsBlock
{
	[JsonPropertyName("game")]
	public List<JsonElement>? Game { get; set; }

	[JsonPropertyName("jvm")]
	public List<JsonElement>? Jvm { get; set; }
}
