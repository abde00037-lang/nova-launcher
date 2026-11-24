using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class VersionDownloads
{
	[JsonPropertyName("client")]
	public DownloadEntry? Client { get; set; }

	[JsonPropertyName("server")]
	public DownloadEntry? Server { get; set; }
}
