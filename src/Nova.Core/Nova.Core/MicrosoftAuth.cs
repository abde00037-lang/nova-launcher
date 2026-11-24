using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class MicrosoftAuth : IDisposable
{
	private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";

	private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

	private const string Scope = "XboxLive.signin offline_access";

	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(45.0)
	};

	public static bool LooksLikeClientId(string? value)
	{
		Guid result;
		return Guid.TryParse(value ?? "", out result);
	}

	public async Task<DeviceCodeInfo> StartDeviceCodeAsync(string clientId, CancellationToken ct = default(CancellationToken))
	{
		if (!LooksLikeClientId(clientId))
		{
			throw new InvalidOperationException("a Microsoft client id (GUID) is required - see Settings → Microsoft sign-in");
		}
		using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["client_id"] = clientId,
			["scope"] = "XboxLive.signin offline_access"
		});
		using HttpResponseMessage response = await _http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", content, ct).ConfigureAwait(continueOnCapturedContext: false);
		string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(Explain(text, (int)response.StatusCode));
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text);
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement value;
		JsonElement value2;
		JsonElement value3;
		return new DeviceCodeInfo
		{
			DeviceCode = (rootElement.GetProperty("device_code").GetString() ?? ""),
			UserCode = (rootElement.GetProperty("user_code").GetString() ?? ""),
			VerificationUri = (rootElement.TryGetProperty("verification_uri", out value) ? (value.GetString() ?? "") : "https://microsoft.com/link"),
			IntervalSeconds = (rootElement.TryGetProperty("interval", out value2) ? value2.GetInt32() : 5),
			ExpiresUtc = DateTime.UtcNow.AddSeconds(rootElement.TryGetProperty("expires_in", out value3) ? value3.GetInt32() : 900)
		};
	}

	public async Task<NovaAccount?> WaitForSignInAsync(string clientId, DeviceCodeInfo info, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		TimeSpan interval = TimeSpan.FromSeconds(Math.Max(3, info.IntervalSeconds));
		while (DateTime.UtcNow < info.ExpiresUtc)
		{
			ct.ThrowIfCancellationRequested();
			await Task.Delay(interval, ct).ConfigureAwait(continueOnCapturedContext: false);
			using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
				["client_id"] = clientId,
				["device_code"] = info.DeviceCode
			});
			using HttpResponseMessage response = await _http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", content, ct).ConfigureAwait(continueOnCapturedContext: false);
			string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				if (text.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (text.Contains("slow_down", StringComparison.OrdinalIgnoreCase))
				{
					interval += TimeSpan.FromSeconds(2.0);
					continue;
				}
				if (text.Contains("expired_token", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				if (text.Contains("authorization_declined", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				throw new InvalidOperationException(Explain(text, (int)response.StatusCode));
			}
			using JsonDocument document = JsonDocument.Parse(text);
			JsonElement rootElement = document.RootElement;
			string msaToken = rootElement.GetProperty("access_token").GetString() ?? "";
			string refreshToken = (rootElement.TryGetProperty("refresh_token", out var value) ? value.GetString() : null);
			int expiresIn = (rootElement.TryGetProperty("expires_in", out var value2) ? value2.GetInt32() : 3600);
			log?.Report("microsoft: signed in, asking Xbox Live…");
			return await CompleteChainAsync(msaToken, refreshToken, expiresIn, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return null;
	}

	public async Task<NovaAccount?> RefreshAsync(string clientId, NovaAccount account, IProgress<string>? log = null, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(account.RefreshToken))
		{
			return null;
		}
		using FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["client_id"] = clientId,
			["refresh_token"] = account.RefreshToken,
			["scope"] = "XboxLive.signin offline_access"
		});
		using HttpResponseMessage response = await _http.PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", content, ct).ConfigureAwait(continueOnCapturedContext: false);
		string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement rootElement = document.RootElement;
		string msaToken = rootElement.GetProperty("access_token").GetString() ?? "";
		string refreshToken = (rootElement.TryGetProperty("refresh_token", out var value) ? value.GetString() : account.RefreshToken);
		int expiresIn = (rootElement.TryGetProperty("expires_in", out var value2) ? value2.GetInt32() : 3600);
		log?.Report("microsoft: refreshing session…");
		NovaAccount novaAccount = await CompleteChainAsync(msaToken, refreshToken, expiresIn, log, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (novaAccount == null)
		{
			return null;
		}
		novaAccount.Id = account.Id;
		novaAccount.AddedUtc = account.AddedUtc;
		return novaAccount;
	}

	private async Task<NovaAccount?> CompleteChainAsync(string msaToken, string? refreshToken, int expiresIn, IProgress<string>? log, CancellationToken ct)
	{
		using HttpResponseMessage xblResponse = await _http.PostAsJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new
		{
			Properties = new
			{
				AuthMethod = "RPS",
				SiteName = "user.auth.xboxlive.com",
				RpsTicket = "d=" + msaToken
			},
			RelyingParty = "http://auth.xboxlive.com",
			TokenType = "JWT"
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		string text = await xblResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!xblResponse.IsSuccessStatusCode)
		{
			throw new InvalidOperationException("Xbox Live refused the Microsoft token: " + Trim(text));
		}
		using JsonDocument xblDocument = JsonDocument.Parse(text);
		string text2 = xblDocument.RootElement.GetProperty("Token").GetString() ?? "";
		string uhs = xblDocument.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString() ?? "";
		using HttpResponseMessage xstsResponse = await _http.PostAsJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
		{
			Properties = new
			{
				SandboxId = "RETAIL",
				UserTokens = new string[1] { text2 }
			},
			RelyingParty = "rp://api.minecraftservices.com/",
			TokenType = "JWT"
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		string text3 = await xstsResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!xstsResponse.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(XstsError(text3));
		}
		using JsonDocument xstsDocument = JsonDocument.Parse(text3);
		string text4 = xstsDocument.RootElement.GetProperty("Token").GetString() ?? "";
		string xuid;
		if (xstsDocument.RootElement.TryGetProperty("DisplayClaims", out var value))
		{
			xuid = (value.GetProperty("xui")[0].TryGetProperty("xid", out var value2) ? (value2.GetString() ?? "") : "");
		}
		else
		{
			xuid = "";
		}
		using HttpResponseMessage mcResponse = await _http.PostAsJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new
		{
			identityToken = "XBL3.0 x=" + uhs + ";" + text4
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		string text5 = await mcResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!mcResponse.IsSuccessStatusCode)
		{
			throw new InvalidOperationException("Minecraft login failed: " + Trim(text5));
		}
		using JsonDocument mcDocument = JsonDocument.Parse(text5);
		string mcToken = mcDocument.RootElement.GetProperty("access_token").GetString() ?? "";
		using HttpRequestMessage profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
		profileRequest.Headers.Add("Authorization", "Bearer " + mcToken);
		using HttpResponseMessage profileResponse = await _http.SendAsync(profileRequest, ct).ConfigureAwait(continueOnCapturedContext: false);
		string text6 = await profileResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!profileResponse.IsSuccessStatusCode)
		{
			throw new InvalidOperationException((profileResponse.StatusCode == HttpStatusCode.NotFound) ? "this Microsoft account does not own Minecraft: Java Edition" : ("could not read the Minecraft profile: " + Trim(text6)));
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text6);
		JsonElement rootElement = jsonDocument.RootElement;
		string text7 = rootElement.GetProperty("name").GetString() ?? "Player";
		string text8 = rootElement.GetProperty("id").GetString() ?? "";
		log?.Report("microsoft: signed in as " + text7);
		NovaAccount novaAccount = new NovaAccount
		{
			Id = "msa-" + text8,
			Kind = AccountKind.Microsoft,
			Name = text7
		};
		string uuid;
		if (text8.Length != 32)
		{
			uuid = text8;
		}
		else
		{
			uuid = text8.Substring(0, 8) + "-" + text8.Substring(8, 4) + "-" + text8.Substring(12, 4) + "-" + text8.Substring(16, 4) + "-" + text8.Substring(20);
		}
		novaAccount.Uuid = uuid;
		novaAccount.AccessToken = mcToken;
		novaAccount.RefreshToken = refreshToken;
		novaAccount.ExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
		novaAccount.Xuid = xuid;
		return novaAccount;
	}

	private static string XstsError(string body)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			if (jsonDocument.RootElement.TryGetProperty("XErr", out var value))
			{
				long @int = value.GetInt64();
				long num = @int - 2148916233u;
				if ((ulong)num <= 5uL)
				{
					switch ((int)num)
					{
					case 0:
						return "that Microsoft account has no Xbox profile - sign in once at xbox.com, then try again";
					case 2:
						return "Xbox Live is not available in this account's country";
					case 3:
					case 4:
						return "the account needs adult verification (child account)";
					case 5:
						return "this is a child account - an adult must add it to a Microsoft family";
					}
				}
				return $"Xbox Live answered with error {@int}";
			}
		}
		catch
		{
		}
		return "Xbox Live refused the request: " + Trim(body);
	}

	private static string Explain(string body, int status)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			if (jsonDocument.RootElement.TryGetProperty("error_description", out var value))
			{
				return Trim(value.GetString() ?? "");
			}
			if (jsonDocument.RootElement.TryGetProperty("error", out var value2))
			{
				return (value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "") : value2.ToString();
			}
		}
		catch
		{
		}
		return $"Microsoft answered {status}";
	}

	private static string Trim(string value)
	{
		if (value.Length > 220)
		{
			return value.Substring(0, 220) + "…";
		}
		return value;
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
