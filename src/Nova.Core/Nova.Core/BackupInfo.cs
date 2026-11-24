using System;
using System.IO;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class BackupInfo
{
	public string FilePath { get; set; } = "";

	public string WorldName { get; set; } = "";

	public string InstanceId { get; set; } = "";

	public DateTime CreatedUtc { get; set; }

	public long Bytes { get; set; }

	public int FileCount { get; set; }

	public string? Sha256 { get; set; }

	[JsonIgnore]
	public string FileName => Path.GetFileName(FilePath);

	[JsonIgnore]
	public double Megabytes => Math.Round((double)Bytes / 1048576.0, 1);

	public override string ToString()
	{
		return $"{WorldName}  {CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {Megabytes} MB";
	}
}
