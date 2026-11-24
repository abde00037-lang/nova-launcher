using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace Nova.Core;

public sealed class NovaAccount
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("kind")]
	public AccountKind Kind { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("uuid")]
	public string Uuid { get; set; } = "";

	[JsonPropertyName("accessToken")]
	public string? AccessToken { get; set; }

	[JsonPropertyName("refreshToken")]
	public string? RefreshToken { get; set; }

	[JsonPropertyName("expiresUtc")]
	public DateTime? ExpiresUtc { get; set; }

	[JsonPropertyName("xuid")]
	public string? Xuid { get; set; }

	[JsonPropertyName("addedUtc")]
	public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

	[JsonIgnore]
	public bool IsMicrosoft => Kind == AccountKind.Microsoft;

	[JsonIgnore]
	public string KindLabel
	{
		get
		{
			if (!IsMicrosoft)
			{
				return "Offline";
			}
			return "Microsoft";
		}
	}

	[JsonIgnore]
	public string Subtitle
	{
		get
		{
			if (!IsMicrosoft)
			{
				return "offline identity · singleplayer, LAN and servers that allow offline players";
			}
			DateTime? expiresUtc = ExpiresUtc;
			if (expiresUtc.HasValue)
			{
				DateTime valueOrDefault = expiresUtc.GetValueOrDefault();
				if (valueOrDefault > DateTime.UtcNow)
				{
					return "signed in · online play ready";
				}
			}
			return "signed in · token will refresh on launch";
		}
	}

	[JsonIgnore]
	public PlayerIdentity Identity => new PlayerIdentity(Name, string.IsNullOrWhiteSpace(Uuid) ? OfflineIdentity.Uuid(Name) : Uuid, (IsMicrosoft && !string.IsNullOrWhiteSpace(AccessToken)) ? AccessToken : "0", IsMicrosoft ? "msa" : "legacy", Xuid ?? "", "{}");

	public static NovaAccount CreateOffline(string name)
	{
		string text = (name ?? "").Trim();
		if (text.Length < 3 || text.Length > 16 || !text.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
		{
			throw new ArgumentException("Player name must be 3–16 English letters, digits or underscores.");
		}
		return new NovaAccount
		{
			Id = "offline-" + Slug(text),
			Kind = AccountKind.Offline,
			Name = text,
			Uuid = OfflineIdentity.Uuid(text)
		};
	}

	private static string Slug(string value)
	{
		return new string((from c in value.ToLowerInvariant()
			select (!char.IsLetterOrDigit(c)) ? '-' : c).ToArray());
	}
}
