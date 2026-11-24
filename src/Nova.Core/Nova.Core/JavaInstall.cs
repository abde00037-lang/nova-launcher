namespace Nova.Core;

public sealed record JavaInstall(string Path, int Major, string RawVersion, string Source)
{
	public override string ToString()
	{
		return $"Java {Major} - {RawVersion}";
	}
}
