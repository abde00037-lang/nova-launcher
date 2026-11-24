using System.Collections.Generic;

namespace Nova.Core;

public sealed record ModEntry(string FileName, string? ModId, string? Name, string? Version, IReadOnlyList<string> Required, bool Enabled, long SizeBytes);
