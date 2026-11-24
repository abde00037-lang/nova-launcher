using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class ExtractRule
{
	[JsonPropertyName("exclude")]
	public List<string>? Exclude { get; set; }
}
