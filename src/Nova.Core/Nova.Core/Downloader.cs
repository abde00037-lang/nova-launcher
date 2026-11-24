using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

public sealed class Downloader : IDisposable
{
	private readonly HttpClient _http;

	private readonly SemaphoreSlim _gate;

	// Bounded striped locks coordinate separate Downloader instances without leaking one semaphore per path.
	private static readonly SemaphoreSlim[] PathLocks = CreatePathLocks();
	private static SemaphoreSlim[] CreatePathLocks()
	{
		var locks = new SemaphoreSlim[1024];
		for (int i = 0; i < locks.Length; i++) locks[i] = new SemaphoreSlim(1, 1);
		return locks;
	}

	public string? LastError { get; private set; }

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

	public async Task<FetchResult> EnsureAsync(string targetPath, string? url, string? sha1, CancellationToken ct = default(CancellationToken), Action<long, long>? byteProgress = null, long? maxBytes = null)
	{
		targetPath = Path.GetFullPath(targetPath);
		string lockKey = OperatingSystem.IsWindows() ? targetPath.ToUpperInvariant() : targetPath;
		SemaphoreSlim pathLock = PathLocks[(int)((uint)StringComparer.Ordinal.GetHashCode(lockKey) % (uint)PathLocks.Length)];
		await pathLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			// A bounded, stable set of lock files also coordinates the GUI and CLI in separate processes.
			using FileStream processLock = await AcquireProcessLockAsync(lockKey, ct).ConfigureAwait(false);
			// Recheck under the per-path lock; another caller may have completed while we waited.
			if (File.Exists(targetPath))
			{
				bool valid = string.IsNullOrEmpty(sha1)
					? new FileInfo(targetPath).Length > 0 && (!maxBytes.HasValue || new FileInfo(targetPath).Length <= maxBytes.Value)
					: string.Equals(await Sha1Async(targetPath, ct).ConfigureAwait(false), sha1, StringComparison.OrdinalIgnoreCase) && (!maxBytes.HasValue || new FileInfo(targetPath).Length <= maxBytes.Value);
				if (valid) { Interlocked.Increment(ref FilesSkipped); return FetchResult.AlreadyValid; }
			}
			if (string.IsNullOrEmpty(url)) { Interlocked.Increment(ref FilesFailed); return FetchResult.Failed; }
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
							currentFileBytes += read;
							if (maxBytes.HasValue && currentFileBytes > maxBytes.Value) throw new DownloadLimitException("Download exceeded its permitted size.");
							await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(continueOnCapturedContext: false);
							long totalSoFar = Interlocked.Add(ref BytesDownloaded, read);
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
					Interlocked.Increment(ref FilesDownloaded);
					return FetchResult.Downloaded;
				}
				catch (DownloadLimitException) { TryDelete(tmp); throw; }
				catch (OperationCanceledException)
				{
					TryDelete(tmp);
					throw;
				}
				catch (Exception ex)
				{
					// Hide URLs (which may contain signed query parameters) from user-visible logs.
					LastError = ex.GetType().Name + ": " + System.Text.RegularExpressions.Regex.Replace(ex.Message, @"https?://\S+", "[URL hidden]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
		finally { pathLock.Release(); }
	}

	private static async Task<FileStream> AcquireProcessLockAsync(string key, CancellationToken ct)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
		int stripe = ((hash[0] << 8) | hash[1]) % 1024;
		string directory = Path.Combine(Path.GetTempPath(), "nova-download-locks");
		Directory.CreateDirectory(directory);
		string name = Path.Combine(directory, stripe.ToString("D4") + ".lock");
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			try { return new FileStream(name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
			catch (IOException ex) when (OperatingSystem.IsWindows() ? ((ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33) : (ex.HResult & 0xffff) == 11)
			{ await Task.Delay(100, ct).ConfigureAwait(false); }
		}
	}

	public async Task<string?> Sha1Async(string path, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			string result;
			await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
			{
				string text = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
				result = text;
			}
			return result;
		}
		catch (OperationCanceledException) { throw; }
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

/// <summary>Raised when a transfer exceeds its explicitly configured byte limit.</summary>
public sealed class DownloadLimitException : IOException
{
    public DownloadLimitException(string message) : base(message) { }
}
