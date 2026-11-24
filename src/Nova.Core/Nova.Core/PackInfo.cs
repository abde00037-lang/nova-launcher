using System.Collections.Generic;

namespace Nova.Core;

public sealed class PackInfo
{
	public PackFormat Format { get; set; }

	public string SourceFile { get; set; } = "";

	public string Name { get; set; } = "";

	public string VersionId { get; set; } = "";

	public string McVersion { get; set; } = "";

	public string? Loader { get; set; }

	public string? LoaderVersion { get; set; }

	public int ListedFiles { get; set; }

	public long ListedBytes { get; set; }

	public int OverrideFiles { get; set; }

	public List<string> Notes { get; } = new List<string>();

	public bool CanDownloadEverything
	{
		get
		{
			PackFormat format = Format;
			if ((uint)(format - 1) <= 1u || format == PackFormat.PlainZip)
			{
				return true;
			}
			return false;
		}
	}

	public string Summary
	{
		get
		{
			string value = ((Loader == null) ? "vanilla" : (Loader + " " + LoaderVersion).Trim());
			string value2 = ((ListedFiles > 0) ? $"{ListedFiles} listed files" : "no file list");
			return $"{FormatName} | {McVersion} | {value} | {value2}";
		}
	}

	public string FormatName => Format switch
	{
		PackFormat.NovaPack => "Nova pack", 
		PackFormat.Modrinth => "Modrinth .mrpack", 
		PackFormat.CurseForge => "CurseForge zip", 
		PackFormat.PlainZip => "plain zip", 
		_ => "unknown", 
	};
}
