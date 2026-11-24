using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class CurseFile
{
	[JsonPropertyName("projectID")]
	public int ProjectId { get; set; }

	[JsonPropertyName("fileID")]
	public int FileId { get; set; }

	[JsonPropertyName("required")]
	public bool Required { get; set; }
}
