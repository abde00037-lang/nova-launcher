using System.Collections.Generic;

namespace Nova.Core;

public sealed class ModInstallResult
{
	public List<string> Installed { get; } = new List<string>();

	public List<string> Skipped { get; } = new List<string>();

	public List<string> Failed { get; } = new List<string>();

	public string Summary
	{
		get
		{
			if (Installed.Count != 0)
			{
				return $"{Installed.Count} mod file(s) installed" + ((Skipped.Count > 0) ? $", {Skipped.Count} already present" : "");
			}
			if (Failed.Count != 0)
			{
				return $"{Failed.Count} file(s) failed";
			}
			return "nothing new to install";
		}
	}
}
