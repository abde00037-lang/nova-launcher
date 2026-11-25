using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed record AiToolSpec(string Name, string Description, string ParametersSchema, bool ReadOnly);

public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

public sealed record AiToolResult(string CallId, string Text);

/// <summary>One protocol-neutral agent history entry: text message, assistant tool calls, or tool results.</summary>
public sealed class AiAgentMessage
{
	public string Role { get; set; } = "user";
	public string? Text { get; set; }
	public List<AiToolCall>? Calls { get; set; }
	public List<AiToolResult>? Results { get; set; }

	public static AiAgentMessage User(string text) => new AiAgentMessage { Role = "user", Text = text };
	public static AiAgentMessage System(string text) => new AiAgentMessage { Role = "system", Text = text };
	public static AiAgentMessage Assistant(string? text, List<AiToolCall>? calls) => new AiAgentMessage { Role = "assistant", Text = text, Calls = calls };
	public static AiAgentMessage Tools(List<AiToolResult> results) => new AiAgentMessage { Role = "tool", Results = results };
}

public sealed class AiAgentTurn
{
	public string Text { get; set; } = string.Empty;
	public List<AiToolCall> Calls { get; } = new List<AiToolCall>();
}

public sealed class AiAgentResult
{
	public string Text { get; set; } = string.Empty;
	public List<string> Trace { get; } = new List<string>();
}

public delegate Task<string> AiToolHandler(AiToolCall call, CancellationToken ct);

/// <summary>Return true to allow a write tool call; false denies it without executing anything.</summary>
public delegate Task<bool> AiApprovalGate(AiToolSpec spec, AiToolCall call, CancellationToken ct);

/// <summary>Supervised agent loop: read-only tools run automatically, write tools only through the approval gate.</summary>
public sealed class AiAgent
{
	public const int MaxSteps = 6;
	private readonly AiClient _client;

	public AiAgent(AiClient client) { _client = client; }

	public async Task<AiAgentResult> RunAsync(AiProvider provider, string key, string system, string user, AiToolHandler handler, AiApprovalGate gate, CancellationToken ct = default)
	{
		List<AiAgentMessage> history = new List<AiAgentMessage>();
		if (!string.IsNullOrEmpty(system)) history.Add(AiAgentMessage.System(system));
		history.Add(AiAgentMessage.User(user));
		AiAgentResult result = new AiAgentResult();
		for (int step = 0; step < MaxSteps; step++)
		{
			AiAgentTurn turn = await StepWithFallbackAsync(provider, key, history, ct).ConfigureAwait(false);
			if (turn.Calls.Count == 0)
			{
				result.Text = string.IsNullOrWhiteSpace(turn.Text) ? "(empty reply)" : turn.Text;
				return result;
			}
			history.Add(AiAgentMessage.Assistant(string.IsNullOrWhiteSpace(turn.Text) ? null : turn.Text, turn.Calls));
			List<AiToolResult> results = new List<AiToolResult>();
			foreach (AiToolCall call in turn.Calls)
			{
				AiToolSpec? spec = AiToolbox.Specs.FirstOrDefault(s => s.Name == call.Name);
				string output;
				if (spec == null)
				{
					output = "unknown tool '" + call.Name + "' - denied";
				}
				else if (!spec.ReadOnly)
				{
					bool approved = await gate(spec, call, ct).ConfigureAwait(false);
					if (!approved)
					{
						output = "DENIED by user";
					}
					else
					{
						try { output = await handler(call, ct).ConfigureAwait(false); }
						catch (Exception ex) { output = "ERROR: " + ex.Message; }
					}
				}
				else
				{
					try { output = await handler(call, ct).ConfigureAwait(false); }
					catch (Exception ex) { output = "ERROR: " + ex.Message; }
				}
				result.Trace.Add(call.Name + " -> " + (output.Length <= 300 ? output : output.Substring(0, 300) + "..."));
				results.Add(new AiToolResult(call.Id, output));
			}
			history.Add(AiAgentMessage.Tools(results));
		}
		result.Text = "(agent stopped after " + MaxSteps + " tool steps without a final answer)";
		return result;
	}

	private async Task<AiAgentTurn> StepWithFallbackAsync(AiProvider provider, string key, List<AiAgentMessage> history, CancellationToken ct)
	{
		List<string> errors = new List<string>();
		foreach (string model in provider.Models)
		{
			AiProvider single = new AiProvider { Name = provider.Name, Protocol = provider.Protocol, BaseUrl = provider.BaseUrl, Models = new List<string> { model } };
			try
			{
				return await _client.ChatWithToolsAsync(single, key, history, AiToolbox.Specs, ct).ConfigureAwait(false);
			}
			catch (AiException ex) { errors.Add(model + ": " + ex.Message); }
			catch (HttpRequestException ex) { errors.Add(model + ": " + ex.Message); }
			catch (TaskCanceledException) when (!ct.IsCancellationRequested) { errors.Add(model + ": timeout"); }
		}
		throw new AiException("All models of provider " + provider.Name + " failed: " + string.Join(" | ", errors));
	}
}

/// <summary>Built-in launcher tools. Read-only tools never mutate; write tools are gated by AiAgent.</summary>
public static class AiToolbox
{
	private static readonly Regex SecretPattern = new Regex("(access[_-]?token|authorization|bearer|session[_-]?id)\\s*[:=]\\s*\\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static List<AiToolSpec> Specs { get; } = new List<AiToolSpec>
	{
		new AiToolSpec("list_instances", "List launcher profiles (instances) with id, name, Minecraft version and memory.", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}", true),
		new AiToolSpec("instance_detail", "Show one profile: version, loader, memory, JVM args and mod counts.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}},\"required\":[\"id\"],\"additionalProperties\":false}", true),
		new AiToolSpec("analyze_crash", "Analyse the newest crash report (or latest.log) of a profile and return findings with fixes.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}},\"required\":[\"id\"],\"additionalProperties\":false}", true),
		new AiToolSpec("mod_conflicts", "Detect duplicate mod ids and missing required dependencies inside a profile's mods folder.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}},\"required\":[\"id\"],\"additionalProperties\":false}", true),
		new AiToolSpec("read_log", "Return the tail of a profile's logs/latest.log with secrets redacted.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"},\"lines\":{\"type\":\"integer\"}},\"required\":[\"id\"],\"additionalProperties\":false}", true),
		new AiToolSpec("toggle_mod", "Enable or disable one mod file of a profile (rename to .disabled). Writes to the mods folder.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"},\"file\":{\"type\":\"string\"},\"enabled\":{\"type\":\"boolean\"}},\"required\":[\"id\",\"file\",\"enabled\"],\"additionalProperties\":false}", false),
		new AiToolSpec("set_memory_mb", "Set the instance memory limit in MB (1024-32768). Writes the instance config.", "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"},\"mb\":{\"type\":\"integer\"}},\"required\":[\"id\",\"mb\"],\"additionalProperties\":false}", false)
	};

	public static string Handle(LauncherPaths paths, AiToolCall call, CancellationToken ct = default)
	{
		using JsonDocument args = JsonDocument.Parse(string.IsNullOrEmpty(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
		JsonElement root = args.RootElement;
		string Str(string name) => root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;
		InstanceStore store = new InstanceStore(paths);
		switch (call.Name)
		{
		case "list_instances":
		{
			List<InstanceConfig> all = store.All();
			if (all.Count == 0) return "no instances";
			return string.Join("\n", all.Select(i => i.Id + " | " + i.Name + " | " + i.VersionId + " | memory=" + i.MemoryMb + "MB"));
		}
		case "instance_detail":
		case "analyze_crash":
		case "mod_conflicts":
		case "read_log":
		case "toggle_mod":
		case "set_memory_mb":
		{
			string id = Str("id");
			InstanceConfig? instance = store.Find(id);
			if (instance == null) return "unknown instance id '" + id + "'";
			string modsDir = Path.Combine(instance.GameDirectory, "mods");
			switch (call.Name)
			{
			case "instance_detail":
			{
				ModScanResult scan = Directory.Exists(modsDir) ? ModScanner.Scan(modsDir) : new ModScanResult();
				return "name=" + instance.Name + "\nversion=" + instance.VersionId + "\nmemory=" + instance.MemoryMb + "MB\njvmArgs=" + string.Join(" ", instance.JvmArgs) + "\nmods enabled=" + scan.Mods.Count(m => m.Enabled) + " disabled=" + scan.Mods.Count(m => !m.Enabled) + "\nmods: " + string.Join(", ", scan.Mods.OrderBy(m => m.FileName).Take(15).Select(m => m.FileName));
			}
			case "analyze_crash":
			{
				string crashDir = Path.Combine(instance.GameDirectory, "crash-reports");
				string? newest = null;
				if (Directory.Exists(crashDir)) newest = Directory.GetFiles(crashDir, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
				List<CrashFinding> findings = newest != null ? CrashAnalyzer.AnalyzeFile(newest) : CrashAnalyzer.AnalyzeText(ReadLogTail(instance, 120));
				if (findings.Count == 0) return "no known crash pattern matched" + (newest != null ? " in " + Path.GetFileName(newest) : string.Empty);
				return string.Join("\n", findings.Select(f => f.Severity + " " + f.Title + " - " + f.Suggestion));
			}
			case "mod_conflicts":
			{
				if (!Directory.Exists(modsDir)) return "no mods folder";
				ModScanResult scan = ModScanner.Scan(modsDir);
				List<string> lines = new List<string>();
				foreach (IGrouping<string, ModEntry> group in scan.Mods.Where(m => !string.IsNullOrEmpty(m.ModId)).GroupBy(m => m.ModId!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
				{
					lines.Add("duplicate modid " + group.Key + ": " + string.Join(", ", group.Select(m => m.FileName)));
				}
				HashSet<string> present = new HashSet<string>(scan.Mods.Where(m => m.Enabled && !string.IsNullOrEmpty(m.ModId)).Select(m => m.ModId!), StringComparer.OrdinalIgnoreCase);
				foreach (ModEntry mod in scan.Mods.Where(m => m.Enabled))
				{
					foreach (string required in mod.Required)
					{
						if (!present.Contains(required)) lines.Add("missing dependency for " + mod.FileName + ": " + required);
					}
				}
				return lines.Count == 0 ? "no duplicate modids and no missing dependencies detected" : string.Join("\n", lines);
			}
			case "read_log":
			{
				int linesWanted = root.TryGetProperty("lines", out JsonElement lv) && lv.TryGetInt32(out int parsed) ? Math.Clamp(parsed, 10, 200) : 60;
				string tail = ReadLogTail(instance, linesWanted);
				return tail.Length == 0 ? "no latest.log found" : SecretPattern.Replace(tail, "$1=REDACTED");
			}
			case "toggle_mod":
			{
				string file = Str("file");
				bool enabled = root.TryGetProperty("enabled", out JsonElement ev) && ev.ValueKind == JsonValueKind.True;
				if (file.Length == 0 || file.IndexOfAny(new[] { '/', '\\' }) >= 0 || file == "." || file == "..") return "invalid mod file name";
				bool ok = ModManager.SetEnabled(modsDir, file, enabled);
				return ok ? ((enabled ? "enabled " : "disabled ") + ModManager.DisplayName(file)) : "could not change " + file;
			}
			default:
			{
				int mb = root.TryGetProperty("mb", out JsonElement mv) && mv.TryGetInt32(out int m) ? m : 0;
				if (mb < 1024 || mb > 32768) return "memory must be between 1024 and 32768 MB";
				instance.MemoryMb = mb;
				store.Save(instance);
				return "memory set to " + mb + "MB for " + instance.Name;
			}
			}
		}
		default:
			return "unknown tool '" + call.Name + "'";
		}
	}

	private static string ReadLogTail(InstanceConfig instance, int lines)
	{
		string log = Path.Combine(instance.GameDirectory, "logs", "latest.log");
		if (!File.Exists(log)) return string.Empty;
		string[] all = File.ReadAllLines(log);
		return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
	}
}
