using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class Downloader : IDisposable
{
	private readonly HttpClient _http;

	private readonly SemaphoreSlim _gate;

	private readonly ConcurrentDictionary<string, string?> _hashCache = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

	public long BytesDownloaded;

	public int FilesDownloaded;

	public int FilesSkipped;

	public int FilesFailed;

	public int Retries;

	public Action<long>? OnBytesRead { get; set; }

	public Downloader(int parallelism = 8)
	{
		_gate = new SemaphoreSlim(Math.Max(1, parallelism));
		SocketsHttpHandler handler = new SocketsHttpHandler
		{
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
			ConnectTimeout = TimeSpan.FromSeconds(20.0),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
		};
		_http = new HttpClient(handler)
		{
			Timeout = TimeSpan.FromMinutes(10.0)
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/1.0");
	}

	public async Task<FetchResult> EnsureAsync(string targetPath, string? url, string? sha1, CancellationToken ct = default(CancellationToken), Action<long, long>? byteProgress = null)
	{
		if (File.Exists(targetPath))
		{
			if (string.IsNullOrEmpty(sha1))
			{
				if (new FileInfo(targetPath).Length > 0)
				{
					Interlocked.Increment(ref FilesSkipped);
					return FetchResult.AlreadyValid;
				}
			}
			else
			{
				string? text = await Sha1Async(targetPath, ct).ConfigureAwait(continueOnCapturedContext: false);
				_hashCache[targetPath] = text;
				if (string.Equals(text, sha1, StringComparison.OrdinalIgnoreCase))
				{
					Interlocked.Increment(ref FilesSkipped);
					return FetchResult.AlreadyValid;
				}
			}
		}
		if (string.IsNullOrEmpty(url))
		{
			Interlocked.Increment(ref FilesFailed);
			return FetchResult.Failed;
		}
		await _gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			for (int attempt = 1; attempt <= 3; attempt++)
			{
				string tmp = targetPath + "." + attempt + ".part";
				try
				{
					string? dir = Path.GetDirectoryName(targetPath);
					if (!string.IsNullOrEmpty(dir))
					{
						Directory.CreateDirectory(dir);
					}
					using (HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false))
					{
						response.EnsureSuccessStatusCode();
						long totalLength = response.Content.Headers.ContentLength ?? 0;
						long currentFileBytes = 0;
						await using Stream src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
						await using FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
						byte[] buffer = new byte[81920];
						while (true)
						{
							int read = await src.ReadAsync(buffer, ct).ConfigureAwait(continueOnCapturedContext: false);
							if (read <= 0)
							{
								break;
							}
							await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(continueOnCapturedContext: false);
							long totalSoFar = Interlocked.Add(ref BytesDownloaded, read);
							currentFileBytes += read;
							byteProgress?.Invoke(currentFileBytes, totalLength);
							OnBytesRead?.Invoke(totalSoFar);
						}
					}
					if (!string.IsNullOrEmpty(sha1) && !string.Equals(await Sha1Async(tmp, ct).ConfigureAwait(continueOnCapturedContext: false), sha1, StringComparison.OrdinalIgnoreCase))
					{
						TryDelete(tmp);
						return FetchResult.HashMismatch;
					}
					File.Move(tmp, targetPath, overwrite: true);
					_hashCache[targetPath] = sha1;
					Interlocked.Increment(ref FilesDownloaded);
					return FetchResult.Downloaded;
				}
				catch (OperationCanceledException)
				{
					TryDelete(tmp);
					throw;
				}
				catch
				{
					TryDelete(tmp);
					if (attempt == 3)
					{
						Interlocked.Increment(ref FilesFailed);
						return FetchResult.Failed;
					}
					Interlocked.Increment(ref Retries);
					await Task.Delay(400 * attempt, ct).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			Interlocked.Increment(ref FilesFailed);
			return FetchResult.Failed;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<string?> Sha1Async(string path, CancellationToken ct = default(CancellationToken))
	{
		if (_hashCache.TryGetValue(path, out string? value) && !string.IsNullOrEmpty(value))
		{
			return value;
		}
		try
		{
			string result;
			await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
			{
				string text = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
				_hashCache[path] = text;
				result = text;
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		_http.Dispose();
		_gate.Dispose();
	}
}
