using System.Collections.Generic;

namespace Nova.Core;

public sealed class ResolvedVersion
{
	public string Id { get; set; } = "";

	public string? ParentId { get; set; }

	public string MainClass { get; set; } = "";

	public string AssetIndexId { get; set; } = "";

	public AssetIndexRef? AssetIndex { get; set; }

	public int RequiredJavaMajor { get; set; } = 8;

	public List<ResolvedFile> Files { get; } = new List<ResolvedFile>();

	public List<ResolvedFile> NativeFiles { get; } = new List<ResolvedFile>();

	public List<string> JvmArgs { get; } = new List<string>();

	public List<string> GameArgs { get; } = new List<string>();

	public string ClientJarVersionId { get; set; } = "";

	public DownloadEntry? ClientDownload { get; set; }

	public List<string> Warnings { get; } = new List<string>();

	public override string ToString()
	{
		return $"{Id} (parent: {ParentId ?? "-"}, {Files.Count} libs, Java {RequiredJavaMajor})";
	}
}
