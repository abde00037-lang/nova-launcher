using System;
using System.Linq;

namespace Nova.Core;

public sealed record StoreMod(string Id, string Slug, string Name, string Author, string Summary, long Downloads, string? IconUrl, StoreSource Source)
{
	public string Initials
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Name))
			{
				return string.Concat(from w in Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2)
					select char.ToUpperInvariant(w[0]));
			}
			return "?";
		}
	}

	public string DownloadsLabel
	{
		get
		{
			if (Downloads < 1000000)
			{
				if (Downloads < 1000)
				{
					return Downloads.ToString();
				}
				return ((double)Downloads / 1000.0).ToString("0.#") + "K";
			}
			return ((double)Downloads / 1000000.0).ToString("0.#") + "M";
		}
	}

	public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);
}
