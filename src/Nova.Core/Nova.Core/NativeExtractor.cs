using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Nova.Core;

public static class NativeExtractor
{
	public static int ExtractAll(IEnumerable<string> nativeJars, string targetDirectory, bool clean = true)
	{
		if (clean && Directory.Exists(targetDirectory))
		{
			try
			{
				Directory.Delete(targetDirectory, recursive: true);
			}
			catch
			{
			}
		}
		Directory.CreateDirectory(targetDirectory);
		int num = 0;
		foreach (string nativeJar in nativeJars)
		{
			if (!File.Exists(nativeJar))
			{
				continue;
			}
			try
			{
				using ZipArchive zipArchive = ZipFile.OpenRead(nativeJar);
				foreach (ZipArchiveEntry entry in zipArchive.Entries)
				{
					if (!entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) && !entry.FullName.EndsWith("/"))
					{
						string text = Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
						Directory.CreateDirectory(Path.GetDirectoryName(text));
						entry.ExtractToFile(text, overwrite: true);
						num++;
					}
				}
			}
			catch
			{
			}
		}
		return num;
	}
}
