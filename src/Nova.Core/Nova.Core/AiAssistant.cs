using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

/// <summary>Wire protocol of a user-configured AI endpoint.</summary>
public enum AiProtocol
{
	/// <summary>OpenAI-style POST {base}/chat/completions with a Bearer key.</summary>
	OpenAiCompatible,
	/// <summary>Anthropic-style POST {base}/v1/messages with x-api-key and anthropic-version headers.</summary>
	Anthropic
}

/// <summary>A named bring-your-own-key AI provider with an ordered model fallback chain.</summary>
public sealed class AiProvider
{
	public string Name { get; set; } = string.Empty;
	public AiProtocol Protocol { get; set; } = AiProtocol.OpenAiCompatible;
	/// <summary>Root of the API, e.g. https://api.example.com/v1 (OpenAI-compatible) or https://api.example.com (Anthropic).</summary>
	public string BaseUrl { get; set; } = string.Empty;
	/// <summary>Model ids tried in order; the next one runs when a request fails.</summary>
	public List<string> Models { get; set; } = new List<string>();
}

public sealed class AiReply
{
	public string Model { get; internal set; } = string.Empty;
	public string Text { get; internal set; } = string.Empty;
}

public sealed class AiException : Exception
{
	public AiException(string message) : base(message) { }
}

/// <summary>Provider configuration plus DPAPI-protected API keys under {root}/ai. Keys never appear in plaintext on disk.</summary>
public static class AiStore
{
	public static string AiDirectory(LauncherPaths paths) => Path.Combine(paths.Root, "ai");
	public static string ProvidersFile(LauncherPaths paths) => Path.Combine(AiDirectory(paths), "providers.json");
	public static string KeysFile(LauncherPaths paths) => Path.Combine(AiDirectory(paths), "keys.dat");

	public static List<AiProvider> LoadProviders(LauncherPaths paths)
	{
		string file = ProvidersFile(paths);
		if (!File.Exists(file)) return new List<AiProvider>();
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
		List<AiProvider> list = new List<AiProvider>();
		foreach (JsonElement node in doc.RootElement.EnumerateArray())
		{
			AiProvider provider = new AiProvider
			{
				Name = node.GetProperty("name").GetString() ?? string.Empty,
				BaseUrl = node.GetProperty("baseUrl").GetString() ?? string.Empty
			};
			if (node.TryGetProperty("protocol", out JsonElement proto) && Enum.TryParse<AiProtocol>(proto.GetString(), true, out AiProtocol parsed)) provider.Protocol = parsed;
			if (node.TryGetProperty("models", out JsonElement models)) provider.Models = models.EnumerateArray().Select(m => m.GetString() ?? string.Empty).Where(m => m.Length > 0).ToList();
			list.Add(provider);
		}
		return list;
	}

	public static void SaveProviders(LauncherPaths paths, List<AiProvider> providers)
	{
		Directory.CreateDirectory(AiDirectory(paths));
		JsonArrayNode array = new JsonArrayNode(providers);
		File.WriteAllText(ProvidersFile(paths), array.ToString());
	}

	public static AiProvider? Find(LauncherPaths paths, string name) => LoadProviders(paths).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

	public static void SaveProvider(LauncherPaths paths, AiProvider provider)
	{
		Validate(provider);
		List<AiProvider> list = LoadProviders(paths);
		list.RemoveAll(p => string.Equals(p.Name, provider.Name, StringComparison.OrdinalIgnoreCase));
		list.Add(provider);
		SaveProviders(paths, list);
	}

	public static bool RemoveProvider(LauncherPaths paths, string name)
	{
		List<AiProvider> list = LoadProviders(paths);
		int before = list.Count;
		list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
		if (list.Count == before) return false;
		SaveProviders(paths, list);
		SetKey(paths, name, string.Empty);
		return true;
	}

	public static void SetKey(LauncherPaths paths, string name, string key)
	{
		ValidateName(name);
		Directory.CreateDirectory(AiDirectory(paths));
		string file = KeysFile(paths);
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (File.Exists(file))
		{
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
			foreach (JsonProperty prop in doc.RootElement.EnumerateObject()) map[prop.Name] = prop.Value.GetString() ?? string.Empty;
		}
		if (string.IsNullOrEmpty(key)) map.Remove(name);
		else map[name] = WindowsTokenProtector.Protect(key);
		StringBuilder builder = new StringBuilder("{");
		builder.Append(string.Join(",", map.Select(kv => JsonSerializer.Serialize(kv.Key) + ":" + JsonSerializer.Serialize(kv.Value))));
		builder.Append('}');
		File.WriteAllText(file, builder.ToString());
	}

	public static string? GetKey(LauncherPaths paths, string name)
	{
		string file = KeysFile(paths);
		if (!File.Exists(file)) return null;
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
		if (!doc.RootElement.TryGetProperty(name, out JsonElement node)) return null;
		string blob = node.GetString() ?? string.Empty;
		return string.IsNullOrEmpty(blob) ? null : WindowsTokenProtector.Unprotect(blob);
	}

	public static void Validate(AiProvider provider)
	{
		ValidateName(provider.Name);
		ValidateBaseUrl(provider.BaseUrl);
		if (provider.Models == null || provider.Models.Count == 0 || provider.Models.Any(m => string.IsNullOrWhiteSpace(m))) throw new ArgumentException("AI provider needs at least one model id.");
	}

	private static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Length > 32) throw new ArgumentException("AI provider name must be 1-32 characters.");
		foreach (char c in name)
		{
			bool ok = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
			if (!ok) throw new ArgumentException("AI provider name allows letters, digits, dot, dash and underscore only.");
		}
	}

	private static void ValidateBaseUrl(string baseUrl)
	{
		if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out Uri? uri)) throw new ArgumentException("AI provider base URL is not a valid absolute URL.");
		if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("AI provider base URL must use http or https.");
	}

	private sealed class JsonArrayNode
	{
		private readonly List<AiProvider> _providers;
		public JsonArrayNode(List<AiProvider> providers) { _providers = providers; }
		public override string ToString()
		{
			StringBuilder builder = new StringBuilder("[");
			for (int i = 0; i < _providers.Count; i++)
			{
				AiProvider p = _providers[i];
				if (i > 0) builder.Append(',');
				builder.Append('{');
				builder.Append("\"name\":").Append(JsonSerializer.Serialize(p.Name));
				builder.Append(",\"protocol\":").Append(JsonSerializer.Serialize(p.Protocol.ToString()));
				builder.Append(",\"baseUrl\":").Append(JsonSerializer.Serialize(p.BaseUrl));
				builder.Append(",\"models\":[").Append(string.Join(",", p.Models.Select(m => JsonSerializer.Serialize(m)))).Append(']');
				builder.Append('}');
			}
			return builder.Append(']').ToString();
		}
	}
}

/// <summary>Minimal BYOK chat client for OpenAI-compatible and Anthropic endpoints with per-model fallback.</summary>
public sealed class AiClient : IDisposable
{
	private readonly HttpClient _http;

	public AiClient(int timeoutSeconds = 120)
	{
		_http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/2.1 (BYOK AI assistant)");
	}

	public void Dispose() { _http.Dispose(); }

	public static string NormalizeBase(string baseUrl) => baseUrl.Trim().TrimEnd('/');

	public async Task<List<string>> ListModelsAsync(AiProvider provider, string key, CancellationToken ct = default)
	{
		string baseUri = NormalizeBase(provider.BaseUrl);
		string url = provider.Protocol == AiProtocol.Anthropic ? baseUri + "/v1/models" : baseUri + "/models";
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
		ApplyAuth(request, provider.Protocol, key);
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) throw new AiException("HTTP " + (int)response.StatusCode + ": " + Truncate(body));
		using JsonDocument doc = JsonDocument.Parse(body);
		List<string> ids = new List<string>();
		if (doc.RootElement.TryGetProperty("data", out JsonElement data))
		{
			foreach (JsonElement node in data.EnumerateArray())
			{
				string? id = node.TryGetProperty("id", out JsonElement idNode) ? idNode.GetString() : null;
				if (!string.IsNullOrEmpty(id)) ids.Add(id);
			}
		}
		return ids;
	}

	/// <summary>Tries each model of the provider in order; throws AiException listing every failure when all fail.</summary>
	public async Task<AiReply> ChatAsync(AiProvider provider, string key, string? system, string user, int maxTokens = 512, CancellationToken ct = default)
	{
		if (provider.Models == null || provider.Models.Count == 0) throw new AiException("Provider " + provider.Name + " has no models configured.");
		List<string> errors = new List<string>();
		foreach (string model in provider.Models)
		{
			try
			{
				return await ChatOnceAsync(provider, key, system, user, model, maxTokens, ct).ConfigureAwait(false);
			}
			catch (AiException ex) { errors.Add(model + ": " + ex.Message); }
			catch (HttpRequestException ex) { errors.Add(model + ": " + ex.Message); }
			catch (TaskCanceledException) when (!ct.IsCancellationRequested) { errors.Add(model + ": timeout"); }
		}
		throw new AiException("All models of provider " + provider.Name + " failed: " + string.Join(" | ", errors));
	}

	private async Task<AiReply> ChatOnceAsync(AiProvider provider, string key, string? system, string user, string model, int maxTokens, CancellationToken ct)
	{
		string baseUri = NormalizeBase(provider.BaseUrl);
		string url;
		string payload;
		if (provider.Protocol == AiProtocol.Anthropic)
		{
			url = baseUri + "/v1/messages";
			payload = JsonSerializer.Serialize(new
			{
				model,
				max_tokens = maxTokens,
				system = string.IsNullOrEmpty(system) ? null : system,
				messages = new[] { new { role = "user", content = user } }
			});
		}
		else
		{
			url = baseUri + "/chat/completions";
			List<object> messages = new List<object>();
			if (!string.IsNullOrEmpty(system)) messages.Add(new { role = "system", content = system });
			messages.Add(new { role = "user", content = user });
			payload = JsonSerializer.Serialize(new { model, messages, max_tokens = maxTokens });
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
		ApplyAuth(request, provider.Protocol, key);
		request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) throw new AiException("HTTP " + (int)response.StatusCode + ": " + Truncate(body));
		using JsonDocument doc = JsonDocument.Parse(body);
		string text;
		if (provider.Protocol == AiProtocol.Anthropic)
		{
			StringBuilder builder = new StringBuilder();
			if (doc.RootElement.TryGetProperty("content", out JsonElement content))
			{
				foreach (JsonElement block in content.EnumerateArray())
				{
					if (block.TryGetProperty("type", out JsonElement type) && type.GetString() == "text" && block.TryGetProperty("text", out JsonElement textNode)) builder.Append(textNode.GetString());
				}
			}
			text = builder.ToString();
		}
		else
		{
			text = string.Empty;
			bool reasoningOnly = false;
			if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
			{
				JsonElement message = choices[0].GetProperty("message");
				if (message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String) text = content.GetString() ?? string.Empty;
				if (string.IsNullOrWhiteSpace(text) && message.TryGetProperty("reasoning", out JsonElement reasoning) && reasoning.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(reasoning.GetString())) reasoningOnly = true;
			}
			if (reasoningOnly) text = "[reasoning-only reply; raise max_tokens for a final answer]";
		}
		return new AiReply { Model = model, Text = text };
	}

	/// <summary>One agent step: sends the whole history plus tool specs and returns text and/or tool calls.</summary>
	public async Task<AiAgentTurn> ChatWithToolsAsync(AiProvider provider, string key, List<AiAgentMessage> history, List<AiToolSpec> tools, CancellationToken ct = default)
	{
		string baseUri = NormalizeBase(provider.BaseUrl);
		string url;
		string payload;
		if (provider.Protocol == AiProtocol.Anthropic)
		{
			url = baseUri + "/v1/messages";
			List<object> messages = new List<object>();
			string? system = null;
			foreach (AiAgentMessage msg in history)
			{
				if (msg.Role == "system") { system = msg.Text; continue; }
				if (msg.Role == "assistant")
				{
					List<object> blocks = new List<object>();
					if (!string.IsNullOrEmpty(msg.Text)) blocks.Add(new { type = "text", text = msg.Text });
					if (msg.Calls != null) foreach (AiToolCall call in msg.Calls) blocks.Add(new { type = "tool_use", id = call.Id, name = call.Name, input = JsonDocument.Parse(string.IsNullOrEmpty(call.ArgumentsJson) ? "{}" : call.ArgumentsJson).RootElement });
					messages.Add(new { role = "assistant", content = blocks });
				}
				else if (msg.Results != null)
				{
					List<object> blocks = msg.Results.Select(r => (object)new { type = "tool_result", tool_use_id = r.CallId, content = r.Text }).ToList();
					messages.Add(new { role = "user", content = blocks });
				}
				else
				{
					messages.Add(new { role = "user", content = msg.Text ?? string.Empty });
				}
			}
			payload = JsonSerializer.Serialize(new
			{
				model = provider.Models.FirstOrDefault() ?? string.Empty,
				max_tokens = 1024,
				system,
				messages,
				tools = tools.Select(t => new { name = t.Name, description = t.Description, input_schema = JsonDocument.Parse(t.ParametersSchema).RootElement })
			});
		}
		else
		{
			url = baseUri + "/chat/completions";
			List<object> messages = new List<object>();
			foreach (AiAgentMessage msg in history)
			{
				if (msg.Results != null)
				{
					foreach (AiToolResult result in msg.Results) messages.Add(new { role = "tool", tool_call_id = result.CallId, content = result.Text });
					continue;
				}
				if (msg.Role == "assistant" && msg.Calls != null && msg.Calls.Count > 0)
				{
					messages.Add(new
					{
						role = "assistant",
						content = msg.Text,
						tool_calls = msg.Calls.Select(c => new { id = c.Id, type = "function", function = new { name = c.Name, arguments = string.IsNullOrEmpty(c.ArgumentsJson) ? "{}" : c.ArgumentsJson } })
					});
					continue;
				}
				messages.Add(new { role = msg.Role, content = msg.Text ?? string.Empty });
			}
			payload = JsonSerializer.Serialize(new
			{
				model = provider.Models.FirstOrDefault() ?? string.Empty,
				max_tokens = 1024,
				messages,
				tools = tools.Select(t => new { type = "function", function = new { name = t.Name, description = t.Description, parameters = JsonDocument.Parse(t.ParametersSchema).RootElement } })
			});
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
		ApplyAuth(request, provider.Protocol, key);
		request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) throw new AiException("HTTP " + (int)response.StatusCode + ": " + Truncate(body));
		using JsonDocument doc = JsonDocument.Parse(body);
		AiAgentTurn turn = new AiAgentTurn();
		if (provider.Protocol == AiProtocol.Anthropic)
		{
			StringBuilder builder = new StringBuilder();
			foreach (JsonElement block in doc.RootElement.GetProperty("content").EnumerateArray())
			{
				string type = block.GetProperty("type").GetString() ?? string.Empty;
				if (type == "text") builder.Append(block.GetProperty("text").GetString());
				else if (type == "tool_use") turn.Calls.Add(new AiToolCall(block.GetProperty("id").GetString() ?? string.Empty, block.GetProperty("name").GetString() ?? string.Empty, block.GetProperty("input").GetRawText()));
			}
			turn.Text = builder.ToString();
		}
		else
		{
			JsonElement message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
			if (message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String) turn.Text = content.GetString() ?? string.Empty;
			if (message.TryGetProperty("tool_calls", out JsonElement calls) && calls.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement call in calls.EnumerateArray())
				{
					JsonElement fn = call.GetProperty("function");
					turn.Calls.Add(new AiToolCall(call.GetProperty("id").GetString() ?? string.Empty, fn.GetProperty("name").GetString() ?? string.Empty, fn.GetProperty("arguments").GetString() ?? "{}"));
				}
			}
		}
		return turn;
	}

	private static void ApplyAuth(HttpRequestMessage request, AiProtocol protocol, string key)
	{
		if (protocol == AiProtocol.Anthropic)
		{
			request.Headers.TryAddWithoutValidation("x-api-key", key);
			request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
		}
		else
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
		}
	}

	private static string Truncate(string body) => body == null ? string.Empty : (body.Length <= 240 ? body.Replace('\n', ' ') : body.Substring(0, 240).Replace('\n', ' ') + "...");
}
