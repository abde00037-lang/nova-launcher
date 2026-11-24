namespace Nova.Core;

public sealed record SuspectMod(string ModFile, string? ModId, int Hits, string Sample)
{
	public override string ToString()
	{
		return ModFile;
	}

	public string Evidence()
	{
		return Sample;
	}
}
