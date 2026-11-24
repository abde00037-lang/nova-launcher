using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public static class IconCache
{
	private static readonly HttpClient Client = CreateClient();

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromSeconds(20.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/0.2 (minecraft modpack launcher)");
		return httpClient;
	}

	public static async Task<string?> FetchAsync(string url, string cacheDirectory, string key, CancellationToken ct = default(CancellationToken))
	{
		_ = 1;
		try
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				return null;
			}
			Directory.CreateDirectory(cacheDirectory);
			string safe = new string(key.Select((char c) => (!char.IsLetterOrDigit(c)) ? '-' : char.ToLowerInvariant(c)).ToArray());
			if (safe.Length > 60)
			{
				safe = safe.Substring(0, 60);
			}
			if (safe.Length == 0)
			{
				return null;
			}
			using (IEnumerator<string> enumerator = Directory.EnumerateFiles(cacheDirectory, safe + ".*").GetEnumerator())
			{
				if (enumerator.MoveNext())
				{
					return enumerator.Current;
				}
			}
			byte[] array = await Client.GetByteArrayAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (array.Length == 0 || array.Length > 4000000)
			{
				return null;
			}
			string text = ModMetaStore.SniffExtension(array);
			if (text == null)
			{
				return null;
			}
			string path = Path.Combine(cacheDirectory, safe + text);
			await File.WriteAllBytesAsync(path, array, ct).ConfigureAwait(continueOnCapturedContext: false);
			return path;
		}
		catch
		{
			return null;
		}
	}
}
