using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class Rule
{
	[JsonPropertyName("action")]
	public string? Action { get; set; }

	[JsonPropertyName("os")]
	public OsRule? Os { get; set; }

	[JsonPropertyName("features")]
	public Dictionary<string, bool>? Features { get; set; }
}
