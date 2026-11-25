using System.Collections.Generic;

namespace Nova.Core;

public sealed class PackImportResult
{
	public string InstanceId { get; set; } = "";

	public string Name { get; set; } = "";

	public string GameDirectory { get; set; } = "";

	public string VersionId { get; set; } = "";

	public int FilesWritten { get; set; }

	public int FilesSkipped { get; set; }

	public int FilesFailed { get; set; }

	public bool SetupFailed { get; set; }

	public List<string> Missing { get; } = new List<string>();

	public List<string> Notes { get; } = new List<string>();

	public string Summary => $"{FilesWritten} file(s) written | {FilesSkipped} already present | {FilesFailed} failed";
}

/// <summary>Progress through files listed in a modpack manifest.</summary>
public sealed record PackImportProgress(int Completed, int Total, int Failed, long BytesTransferred = 0);
