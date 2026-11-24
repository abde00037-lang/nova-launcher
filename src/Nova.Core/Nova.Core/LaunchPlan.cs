using System.Collections.Generic;
using System.Linq;

namespace Nova.Core;

public sealed class LaunchPlan
{
	public string JavaPath { get; set; } = "";

	public string WorkingDirectory { get; set; } = "";

	public List<string> Arguments { get; } = new List<string>();

	public List<string> Notes { get; } = new List<string>();

	public string Preview => "\"" + JavaPath + "\" " + string.Join(' ', Arguments.Select(Quote));

	private static string Quote(string value)
	{
		if (!value.Contains(' '))
		{
			return value;
		}
		return "\"" + value + "\"";
	}
}
