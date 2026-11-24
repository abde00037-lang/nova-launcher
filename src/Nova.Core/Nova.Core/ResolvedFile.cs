namespace Nova.Core;

public sealed class ResolvedFile
{
	public string Name { get; init; } = "";

	public string RelativePath { get; init; } = "";

	public string? Url { get; init; }

	public string? Sha1 { get; init; }

	public long Size { get; init; }

	public string? NativeClassifier { get; init; }
}
