namespace Nova.Core;

public readonly record struct Coordinates(string Group, string Artifact, string Version, string? Classifier, string Extension = "jar")
{
	public static Coordinates Parse(string name)
	{
		string raw = name ?? "";
		string ext = "jar";
		int atIdx = raw.IndexOf('@');
		if (atIdx > 0)
		{
			string parsedExt = raw.Substring(atIdx + 1).Trim();
			if (!string.IsNullOrEmpty(parsedExt))
			{
				ext = parsedExt;
			}
			raw = raw.Substring(0, atIdx);
		}
		string[] array = raw.Split(':');
		return new Coordinates(
			array[0],
			(array.Length > 1) ? array[1] : array[0],
			(array.Length > 2) ? array[2] : "0",
			(array.Length > 3) ? array[3] : null,
			ext);
	}

	public string ToPath(string? classifier)
	{
		string? eff = classifier ?? Classifier;
		string ext = (string.IsNullOrEmpty(Extension) ? "jar" : Extension);
		string text = Artifact + "-" + Version + (string.IsNullOrEmpty(eff) ? "" : ("-" + eff)) + "." + ext;
		return string.Join('/', Group.Split('.')) + "/" + Artifact + "/" + Version + "/" + text;
	}

	public string ToUrl(string? classifier, string? baseUrl = null)
	{
		string root = (string.IsNullOrWhiteSpace(baseUrl) ? "https://libraries.minecraft.net/" : baseUrl.Trim());
		if (!root.EndsWith('/'))
		{
			root += "/";
		}
		return root + ToPath(classifier);
	}
}
