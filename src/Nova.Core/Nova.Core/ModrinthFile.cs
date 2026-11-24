using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Nova.Core;

internal sealed class ModrinthFile
{
	[JsonPropertyName("path")]
	public string Path { get; set; } = "";

	[JsonPropertyName("hashes")]
	public Dictionary<string, string>? Hashes { get; set; }

	[JsonPropertyName("downloads")]
	public List<string>? Downloads { get; set; }

	[JsonPropertyName("fileSize")]
	public long FileSize { get; set; }

	[JsonPropertyName("env")]
	public Dictionary<string, string>? Env { get; set; }

	public bool IsClientFile
	{
		get
		{
			if (Env != null && Env.TryGetValue("client", out string value))
			{
				return value != "unsupported";
			}
			return true;
		}
	}

	public string? Url => Downloads?.FirstOrDefault();

	public string? Sha1
	{
		get
		{
			if (Hashes == null || !Hashes.TryGetValue("sha1", out string value))
			{
				return null;
			}
			return value;
		}
	}
}
