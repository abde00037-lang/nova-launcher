using System;
using System.IO;
using System.Threading;

namespace Nova.Core;

/// <summary>Append-only diagnostic log under {root}/logs/nova-core.log. Never throws.</summary>
public static class NovaLog
{
	private static readonly object Gate = new object();

	public static void Write(string? root, string level, string message)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(root)) return;
			string dir = Path.Combine(root, "logs");
			Directory.CreateDirectory(dir);
			string line = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + level + "] " + message + Environment.NewLine;
			lock (Gate) File.AppendAllText(Path.Combine(dir, "nova-core.log"), line);
		}
		catch { }
	}
}
