using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Nova.Core;

/// <summary>Resumes interrupted update transfers; SHA-256 is always verified before use.</summary>
internal static class UpdateDownloader
{
    private const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;

    internal static async Task<string> FetchAsync(string url, string partial, string sha256, long expectedSize, IProgress<string>? log, CancellationToken ct)
    {
        if (expectedSize < 0 || expectedSize > MaximumPackageBytes) throw new InvalidDataException("Invalid update package size.");
        using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20), AutomaticDecompression = DecompressionMethods.None };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaWorkbench/" + LauncherUpdater.CurrentVersion);
        string? error = null;
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset > MaximumPackageBytes || (expectedSize > 0 && offset > expectedSize))
            { File.Delete(partial); offset = 0; }
            if (offset > 0 && (expectedSize == 0 || offset == expectedSize))
            {
                if (await MatchesAsync(partial, sha256, ct).ConfigureAwait(false)) return partial;
                if (expectedSize > 0)
                {
                    File.Delete(partial); offset = 0;
                    log?.Report("cached update failed SHA-256; downloading again");
                }
            }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.AcceptEncoding.ParseAdd("identity");
                if (offset > 0) req.Headers.Range = new RangeHeaderValue(offset, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                if (resp.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = resp.Content.Headers.ContentRange;
                    if (range == null || !range.HasRange || range.From != offset || !range.HasLength ||
                        range.Length <= 0 || range.Length > MaximumPackageBytes || (expectedSize > 0 && range.Length != expectedSize))
                        throw new InvalidDataException("Invalid update download range.");
                }
                else if (resp.StatusCode == HttpStatusCode.OK) offset = 0; // Server ignored Range: restart, never append a full response.
                else throw new InvalidDataException("Unexpected update download response.");
                if (resp.Content.Headers.ContentEncoding.Count != 0) throw new InvalidDataException("Encoded update downloads cannot be resumed safely.");
                long? contentLength = resp.Content.Headers.ContentLength;
                if (contentLength.HasValue && (contentLength.Value > MaximumPackageBytes - offset ||
                    (expectedSize > 0 && contentLength.Value > expectedSize - offset))) throw new InvalidDataException("Update download exceeds expected size.");
                log?.Report(offset > 0 ? $"resuming update at {offset / 1048576.0:F1} MB" : "downloading update package…");
                await using (var dst = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 81920, true))
                await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    byte[] buffer = new byte[81920]; long current = offset; long lastTick = 0;
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        idle.CancelAfter(TimeSpan.FromSeconds(30));
                        int n = await src.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                        if (n == 0) break;
                        if (n > MaximumPackageBytes - current || (expectedSize > 0 && n > expectedSize - current)) throw new InvalidDataException("Update download exceeds expected size.");
                        await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                        current += n;
                        long now = Environment.TickCount64;
                        if (now - lastTick >= 1000) { lastTick = now; log?.Report(expectedSize > 0 ? $"downloading update: {current / 1048576.0:F1} / {expectedSize / 1048576.0:F1} MB" : $"downloading update: {current / 1048576.0:F1} MB"); }
                    }
                }
                long final = new FileInfo(partial).Length;
                if (expectedSize > 0 && final != expectedSize) throw new IOException("Update download ended before all bytes arrived.");
                log?.Report("verifying update SHA-256…");
                if (await MatchesAsync(partial, sha256, ct).ConfigureAwait(false)) return partial;
                File.Delete(partial);
                throw new InvalidDataException("Update SHA-256 mismatch; no files were installed.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
            {
                error = ex.GetType().Name + ": " + System.Text.RegularExpressions.Regex.Replace(ex.Message, @"https?://\S+", "[URL hidden]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (attempt == 10) break;
                long saved = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                log?.Report($"connection interrupted; retrying from {saved / 1048576.0:F1} MB ({attempt}/10)");
                await Task.Delay(Math.Min(500 * attempt, 3000), ct).ConfigureAwait(false);
            }
        }
        throw new IOException("Update download incomplete; the partial download is saved for retry. " + error);
    }

    private static async Task<bool> MatchesAsync(string file, string expected, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
