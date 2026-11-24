using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nova.Core;

public sealed class CrashDiagnosis
{
	public string SourceFile { get; set; } = "";

	public string SourceKind { get; set; } = "crash-report";

	public string? Description { get; set; }

	public List<string> Causes { get; } = new List<string>();

	public List<SuspectMod> Suspects { get; } = new List<SuspectMod>();

	public List<CrashFinding> Findings { get; } = new List<CrashFinding>();

	public DateTime AnalyzedUtc { get; set; } = DateTime.UtcNow;

	public bool HasSuspects => Suspects.Count > 0;

	public string Markdown(InstanceConfig instance)
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
		handler.AppendLiteral("# Crash report analysis - ");
		handler.AppendFormatted(instance.Name);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 2, stringBuilder2);
		handler.AppendLiteral("- Source: `");
		handler.AppendFormatted(SourceFile);
		handler.AppendLiteral("` (");
		handler.AppendFormatted(SourceKind);
		handler.AppendLiteral(")");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
		handler.AppendLiteral("- Analyzed: ");
		handler.AppendFormatted(AnalyzedUtc.ToLocalTime(), "yyyy-MM-dd HH:mm:ss");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (!string.IsNullOrWhiteSpace(Description))
		{
			stringBuilder.AppendLine("## What the game said");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("> " + Description.Trim());
			stringBuilder.AppendLine();
		}
		if (Causes.Count > 0)
		{
			stringBuilder.AppendLine("## Root cause chain");
			stringBuilder.AppendLine();
			for (int i = 0; i < Causes.Count; i++)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder6 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
				handler.AppendFormatted(i + 1);
				handler.AppendLiteral(". `");
				handler.AppendFormatted(Shorten(Causes[i], 220));
				handler.AppendLiteral("`");
				stringBuilder6.AppendLine(ref handler);
			}
			stringBuilder.AppendLine();
		}
		stringBuilder.AppendLine("## Suspect mods");
		stringBuilder.AppendLine();
		if (Suspects.Count == 0)
		{
			stringBuilder.AppendLine("No installed mod appears in the stack trace. The crash is most likely vanilla, Forge, Java or the graphics driver.");
		}
		else
		{
			stringBuilder.AppendLine("| # | Mod file | Mod id | Frames | Evidence |");
			stringBuilder.AppendLine("|---|----------|--------|--------|----------|");
			int num = 1;
			foreach (SuspectMod item in Suspects.Take(8))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder7 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(20, 5, stringBuilder2);
				handler.AppendLiteral("| ");
				handler.AppendFormatted(num++);
				handler.AppendLiteral(" | `");
				handler.AppendFormatted(item.ModFile);
				handler.AppendLiteral("` | ");
				handler.AppendFormatted(item.ModId ?? "-");
				handler.AppendLiteral(" | ");
				handler.AppendFormatted(item.Hits);
				handler.AppendLiteral(" | `");
				handler.AppendFormatted(Shorten(item.Sample, 120));
				handler.AppendLiteral("` |");
				stringBuilder7.AppendLine(ref handler);
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("The launcher did **not** touch any file. If you want to test without a suspect,");
			stringBuilder.AppendLine("rename `mods\\<file>.jar` to `mods\\<file>.jar.disabled` yourself, then start the game again.");
		}
		stringBuilder.AppendLine();
		if (Findings.Count > 0)
		{
			stringBuilder.AppendLine("## Known patterns");
			stringBuilder.AppendLine();
			foreach (CrashFinding finding in Findings)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder8 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(12, 3, stringBuilder2);
				handler.AppendLiteral("- **");
				handler.AppendFormatted(finding.Title);
				handler.AppendLiteral("** (");
				handler.AppendFormatted(finding.Severity);
				handler.AppendLiteral(") - ");
				handler.AppendFormatted(finding.Suggestion);
				stringBuilder8.AppendLine(ref handler);
			}
			stringBuilder.AppendLine();
		}
		stringBuilder.AppendLine("## Reading order for a human");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("1. Open the crash report and read `Description:` first - it names the failing class.");
		stringBuilder.AppendLine("2. Read the **last** `Caused by:` of the chain: that is the real exception.");
		stringBuilder.AppendLine("3. The frames just above the first Minecraft frame belong to the guilty mod.");
		return stringBuilder.ToString();
	}

	private static string Shorten(string text, int max)
	{
		if (text.Length > max)
		{
			return text.Substring(0, max) + "...";
		}
		return text;
	}
}
