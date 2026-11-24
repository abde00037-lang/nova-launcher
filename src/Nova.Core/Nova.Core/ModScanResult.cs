using System;
using System.Collections.Generic;

namespace Nova.Core;

public sealed class ModScanResult
{
	public List<ModEntry> Mods { get; } = new List<ModEntry>();

	public Dictionary<string, List<string>> DuplicateIds { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	public List<string> BrokenJars { get; } = new List<string>();

	public List<string> MissingDependencies { get; } = new List<string>();

	public int EnabledCount { get; set; }

	public int DisabledCount { get; set; }

	public long TotalBytes { get; set; }

	public int Count => Mods.Count;
}
