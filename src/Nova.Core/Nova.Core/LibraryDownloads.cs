using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class LibraryDownloads
{
	[JsonPropertyName("artifact")]
	public DownloadEntry? Artifact { get; set; }

	[JsonPropertyName("classifiers")]
	public Dictionary<string, DownloadEntry>? Classifiers { get; set; }
}
