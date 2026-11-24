using System;
using System.Collections.Generic;

namespace Nova.Core;

public static class RuleEvaluator
{
	public static string OsName => "windows";

	public static string OsArch
	{
		get
		{
			if (!Environment.Is64BitOperatingSystem)
			{
				return "x86";
			}
			return "x86_64";
		}
	}

	public static bool IsAllowed(List<Rule>? rules)
	{
		if (rules == null || rules.Count == 0)
		{
			return true;
		}
		bool result = false;
		foreach (Rule rule in rules)
		{
			if (Matches(rule))
			{
				result = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
			}
		}
		return result;
	}

	private static bool Matches(Rule rule)
	{
		Dictionary<string, bool> features = rule.Features;
		if (features != null && features.Count > 0)
		{
			foreach (KeyValuePair<string, bool> feature in rule.Features)
			{
				if (feature.Value)
				{
					return false;
				}
			}
		}
		if (rule.Os == null)
		{
			return true;
		}
		if (!string.IsNullOrEmpty(rule.Os.Name) && !string.Equals(rule.Os.Name, OsName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(rule.Os.Arch) && !string.Equals((rule.Os.Arch == "x86") ? "x86" : "x86_64", OsArch, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}
}
