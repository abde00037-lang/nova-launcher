using System.Collections.Generic;
using System.Linq;

namespace Nova.Core;

public sealed class DoctorReport
{
	public List<CheckItem> Items { get; } = new List<CheckItem>();

	public ResolvedVersion? Version { get; set; }

	public JavaInstall? Java { get; set; }

	public ModScanResult? Mods { get; set; }

	public AssetReport? Assets { get; set; }

	public int Fails => Enumerable.Count(Items, (CheckItem i) => i.Status == CheckStatus.Fail);

	public int Warns => Enumerable.Count(Items, (CheckItem i) => i.Status == CheckStatus.Warn);

	public bool Healthy => Fails == 0;

	public void Add(string area, CheckStatus status, string message, string? suggestion = null, bool fixable = false)
	{
		Items.Add(new CheckItem(area, status, message, suggestion, fixable));
	}
}
