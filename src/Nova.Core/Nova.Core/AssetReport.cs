using System.Collections.Generic;

namespace Nova.Core;

public sealed class AssetReport
{
	public string IndexId { get; set; } = "";

	public int Total { get; set; }

	public int Present { get; set; }

	public int Missing { get; set; }

	public int DownloadFailed { get; set; }

	public List<string> MissingSamples { get; } = new List<string>();

	public List<string> FailedSamples { get; } = new List<string>();

	public bool Complete
	{
		get
		{
			if (Missing == 0)
			{
				return DownloadFailed == 0;
			}
			return false;
		}
	}
}
