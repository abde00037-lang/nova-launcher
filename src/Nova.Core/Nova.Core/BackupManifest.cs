using System;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class BackupManifest
{
	[JsonPropertyName("format")]
	public string Format { get; set; } = "nova-backup/1";

	[JsonPropertyName("worldName")]
	public string WorldName { get; set; } = "";

	[JsonPropertyName("instanceId")]
	public string InstanceId { get; set; } = "";

	[JsonPropertyName("versionId")]
	public string VersionId { get; set; } = "";

	[JsonPropertyName("createdUtc")]
	public DateTime CreatedUtc { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; } = "";

	[JsonPropertyName("fileCount")]
	public int FileCount { get; set; }

	[JsonPropertyName("totalBytes")]
	public long TotalBytes { get; set; }

	[JsonPropertyName("createdBy")]
	public string CreatedBy { get; set; } = "Nova Launcher";
}
