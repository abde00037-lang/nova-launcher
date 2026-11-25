using Nova.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

if (args.Length == 3 && args[0] == "--download-worker")
{
    using var workerDownloader = new Downloader(2);
    var workerResult = await workerDownloader.EnsureAsync(args[2], args[1], null);
    if (workerResult != FetchResult.Downloaded && workerResult != FetchResult.AlreadyValid) Environment.ExitCode = 1;
    return;
}

string root = Path.Combine(Path.GetTempPath(), "nova-regression-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    var paths = new LauncherPaths(root);
    void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Console.WriteLine("PASS " + name); return; } throw new Exception("FAIL " + name); }
    foreach (string bad in new[] { "ab", "bad!name", "a b", new string('a', 17), "éclair" }) Reject(() => NovaAccount.CreateOffline(bad), "offline rejects " + bad);
    Check(NovaAccount.CreateOffline("Test_Player").Name == "Test_Player", "valid offline name");
    var store = new InstanceStore(paths);
    foreach (string bad in new[] { "../escape", @"..\..\escape", "/absolute", "..", "a/b" }) Reject(() => store.FolderFor(bad), "invalid instance id " + bad);
    Check(store.FolderFor("normal-1").StartsWith(paths.Instances), "valid instance id");
    Check(VersionLibrary.InRange("26.3") && VersionLibrary.InRange("1.8") && !VersionLibrary.InRange("1.7.10"), "version range");
    Check(VersionResolver.GuessJavaMajor("26.3") == 25, "new Java fallback");
    string h = new string('a', 64);
    Check(LauncherUpdater.ExpectedSha256(h + "  nova-bin-win-x64.zip\n", "nova-bin-win-x64.zip") == h, "checksum parse");
    try { LauncherUpdater.ExpectedSha256(h + "  other.zip", "nova-bin-win-x64.zip"); throw new Exception("accepted missing hash"); } catch (InvalidDataException) { Console.WriteLine("PASS missing checksum rejected"); }
    try { LauncherUpdater.ExpectedSha256(h + "  nova-bin-win-x64.zip\n" + h + "  nova-bin-win-x64.zip", "nova-bin-win-x64.zip"); throw new Exception("accepted duplicate hash"); } catch (InvalidDataException) { Console.WriteLine("PASS duplicate checksum rejected"); }
    if (OperatingSystem.IsWindows())
    {
        var accounts = new AccountStore(paths);
        var account = new NovaAccount { Id = "msa-faketest", Name = "FakeTest", Kind = AccountKind.Microsoft, AccessToken = "test-only-access-token", RefreshToken = "test-only-refresh-token" };
        string accountsDir = Path.Combine(root, "accounts"); Directory.CreateDirectory(accountsDir);
        string file = Path.Combine(accountsDir, "msa-faketest.json");
        File.WriteAllText(file, JsonSerializer.Serialize(account));
        var migrated = accounts.Find(account.Id);
        Check(migrated?.AccessToken == account.AccessToken && migrated?.RefreshToken == account.RefreshToken, "legacy tokens read and migrated");
        string json = File.ReadAllText(file);
        Check(!json.Contains(account.AccessToken!) && !json.Contains(account.RefreshToken!) && json.Contains("protectedRefreshToken"), "migrated tokens encrypted");
        Check(accounts.Find(account.Id)?.RefreshToken == account.RefreshToken, "encrypted token round trip");
        accounts.Save(account);
        Check(!File.ReadAllText(file).Contains(account.RefreshToken!), "new token protected");
    }
    await SecurityRegression.Run(root);
    if (args.Length >= 1 && File.Exists(args[0]))
    {
        string realArchiveOutput = Path.Combine(root, "real-archive");
        int realCount = NativeExtractor.ExtractAll(new[] { args[0] }, realArchiveOutput);
        Check(realCount > 0 && File.Exists(Path.Combine(realArchiveOutput, "Nova.Core.dll")), "real release archive extracts with limits and CRC verification");
    }
    if (args.Length >= 2 && File.Exists(args[1]))
    {
        string nativeOutput = Path.Combine(root, "real-native");
        int nativeCount = NativeExtractor.ExtractAll(new[] { args[1] }, nativeOutput);
        Check(nativeCount > 0 && Directory.GetFiles(nativeOutput, "*.dll", SearchOption.AllDirectories).Length > 0, "real Minecraft LWJGL native archive extraction");
    }
    await using (var fixture = new UpdateFixture())
    {
        string url = fixture.Url;
        var update = new LauncherUpdateInfo("1", "2", "v2", "test", "", "", url + "/asset.zip", "nova-bin-win-x64.zip", 0, null, url + "/SHA256SUMS.txt");
        update = update with { AssetSizeBytes = fixture.Size };
        string staged = await LauncherUpdater.StageUpdateAsync(paths, update);
        Check(File.Exists(Path.Combine(staged, "Nova.Core.dll")), "valid update staged");
        File.WriteAllText(Path.Combine(root, "sentinel"), "safe");
        var bad = update with { ChecksumUrl = url + "/BADSUMS.txt" };
        try { await LauncherUpdater.StageUpdateAsync(paths, bad); throw new Exception("accepted mismatch"); } catch (InvalidDataException) { Console.WriteLine("PASS checksum mismatch rejected before staging"); }
        Check(File.ReadAllText(Path.Combine(root, "sentinel")) == "safe", "data unchanged");
        foreach (string kind in new[] { "incomplete", "badconfig" })
        {
            var invalidPackage = update with { DownloadUrl = url + "/" + kind + ".zip", ChecksumUrl = url + "/" + kind + ".txt", AssetSizeBytes = 0 };
            try { await LauncherUpdater.StageUpdateAsync(new LauncherPaths(Path.Combine(root, kind)), invalidPackage); throw new Exception("accepted incomplete update"); }
            catch (InvalidDataException) { Console.WriteLine("PASS " + kind + " update rejected before installation"); }
        }
        var resumePaths = new LauncherPaths(Path.Combine(root, "resume"));
        string resumed = await LauncherUpdater.StageUpdateAsync(resumePaths, update with { DownloadUrl = url + "/flaky.zip" });
        Check(File.Exists(Path.Combine(resumed, "Nova.exe")) && fixture.RangeRequests > 0, "interrupted update resumes using byte ranges");
        var ignorePaths = new LauncherPaths(Path.Combine(root, "ignored-range"));
        string ignored = await LauncherUpdater.StageUpdateAsync(ignorePaths, update with { DownloadUrl = url + "/ignore.zip" });
        Check(File.Exists(Path.Combine(ignored, "Nova.exe")), "server ignoring Range safely restarts transfer");
        var badRangePaths = new LauncherPaths(Path.Combine(root, "bad-range"));
        try { await LauncherUpdater.StageUpdateAsync(badRangePaths, update with { DownloadUrl = url + "/bad-range.zip" }); throw new Exception("accepted bad Range"); }
        catch (InvalidDataException) { Console.WriteLine("PASS invalid content range rejected"); }
        var cancelPaths = new LauncherPaths(Path.Combine(root, "cancel-stage"));
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelProgress = new InlineProgress<string>(message => { if (message.StartsWith("connection interrupted")) cancellation.Cancel(); });
            try { await LauncherUpdater.StageUpdateAsync(cancelPaths, update with { DownloadUrl = url + "/cancel.zip" }, cancelProgress, cancellation.Token); throw new Exception("accepted cancelled update"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS update cancellation propagates"); }
        }
        Check(Directory.GetFiles(Path.Combine(cancelPaths.Cache, "launcher-update"), "*.part").Any(f => new FileInfo(f).Length > 0), "cancelled update preserves partial bytes");
        string afterCancel = await LauncherUpdater.StageUpdateAsync(cancelPaths, update with { DownloadUrl = url + "/cancel.zip" });
        Check(File.Exists(Path.Combine(afterCancel, "Nova.exe")), "later update resumes after cancellation");
        var lockPaths = new LauncherPaths(Path.Combine(root, "locked-stage"));
        string lockDir = Path.Combine(lockPaths.Cache, "launcher-update"); Directory.CreateDirectory(lockDir);
        using (var held = new FileStream(Path.Combine(lockDir, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var cancelled = new CancellationTokenSource(300))
        {
            try { await LauncherUpdater.StageUpdateAsync(lockPaths, update, ct: cancelled.Token); throw new Exception("accepted cancelled lock wait"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS cancellation while waiting for update lock"); }
        }
        var stalePaths = new LauncherPaths(Path.Combine(root, "stale-cache"));
        string staleDir = Path.Combine(stalePaths.Cache, "launcher-update"); Directory.CreateDirectory(staleDir);
        File.WriteAllBytes(Path.Combine(staleDir, "download-" + fixture.Hash.ToLowerInvariant() + ".part"), new byte[fixture.Size]);
        string freshStage = await LauncherUpdater.StageUpdateAsync(stalePaths, update);
        Check(File.Exists(Path.Combine(freshStage, "Nova.exe")), "corrupt completed cache discarded and re-downloaded");
        var samePaths = new LauncherPaths(Path.Combine(root, "parallel-stage"));
        var twoStages = await Task.WhenAll(LauncherUpdater.StageUpdateAsync(samePaths, update), LauncherUpdater.StageUpdateAsync(samePaths, update));
        Check(twoStages[0] != twoStages[1] && twoStages.All(Directory.Exists), "parallel update staging is serialized and isolated");
        if (OperatingSystem.IsWindows()) await UpdaterSwapperRegression.Run(root);

    }
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

// Loopback-only server exercises download, integrity check and extraction without GitHub or real data.
sealed class UpdateFixture : IAsyncDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancel = new CancellationTokenSource();
    private readonly Task worker;
    private readonly byte[] zip;
    private readonly byte[] good;
    private readonly byte[] bad;
    private readonly byte[] incomplete, badConfig;
    public string Url { get; }
    public int Size => zip.Length;
    public string Hash => Convert.ToHexString(SHA256.HashData(zip));
    public int RangeRequests { get; private set; }
    private int flakyCount, ignoreCount, badRangeCount, cancelCount;
    public UpdateFixture()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (string file in new[] { "Nova.exe", "Nova.dll", "Nova.Core.dll", "nova-cli.exe", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "Nova.runtimeconfig.json", "nova-cli.runtimeconfig.json" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(file).Open());
                writer.Write(file.EndsWith(".json") ? "{\"runtimeOptions\":{\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"}]}}" : "test binary only");
            }
        }
        zip = memory.ToArray();
        using (var a = new MemoryStream())
        {
            using (var z = new ZipArchive(a, ZipArchiveMode.Create, true))
            using (var w = new StreamWriter(z.CreateEntry("Nova.Core.dll").Open())) w.Write("incomplete");
            incomplete = a.ToArray();
        }
        using (var b = new MemoryStream())
        {
            using (var z = new ZipArchive(b, ZipArchiveMode.Create, true))
            {
                foreach (string file in new[] { "Nova.exe", "Nova.dll", "Nova.Core.dll", "nova-cli.exe", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "Nova.runtimeconfig.json", "nova-cli.runtimeconfig.json" })
                { using var w = new StreamWriter(z.CreateEntry(file).Open()); w.Write(file.EndsWith(".json") ? "{\"runtimeOptions\":{\"frameworks\":[]}}" : "test"); }
            }
            badConfig = b.ToArray();
        }
        good = Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(zip)) + "  nova-bin-win-x64.zip\n");
        bad = Encoding.ASCII.GetBytes(new string('0', 64) + "  nova-bin-win-x64.zip\n");
        listener.Start();
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        worker = Serve();
    }
    private async Task Serve()
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancel.Token);
                using NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                string line = await reader.ReadLineAsync() ?? "";
                string? header; long offset = 0;
                do { header = await reader.ReadLineAsync(); if (header != null && header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) { offset = long.Parse(header.Split('=')[1].Split('-')[0]); RangeRequests++; } } while (!string.IsNullOrEmpty(header));
                bool flaky = line.Contains("/flaky.zip "), ignored = line.Contains("/ignore.zip "), invalid = line.Contains("/bad-range.zip "), cancelled = line.Contains("/cancel.zip ");
                bool zipRequest = line.Contains("/asset.zip ") || flaky || ignored || invalid || cancelled;
                byte[] body = line.Contains("/incomplete.zip ") ? incomplete : line.Contains("/badconfig.zip ") ? badConfig :
                    line.Contains("/incomplete.txt ") ? Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(incomplete)) + "  nova-bin-win-x64.zip\n") :
                    line.Contains("/badconfig.txt ") ? Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(badConfig)) + "  nova-bin-win-x64.zip\n") :
                    zipRequest ? zip : line.Contains("/BADSUMS.txt ") ? bad : good;
                bool firstFault = flaky ? ++flakyCount == 1 : ignored ? ++ignoreCount == 1 : invalid ? ++badRangeCount == 1 : cancelled ? ++cancelCount == 1 : false;
                if (firstFault)
                {
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head); await stream.WriteAsync(body.AsMemory(0, body.Length / 2));
                }
                else if (offset > 0 && !ignored)
                {
                    long reported = invalid ? offset + 1 : offset;
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 206 Partial Content\r\nContent-Length: {body.Length - offset}\r\nContent-Range: bytes {reported}-{body.Length - 1}/{body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head); await stream.WriteAsync(body.AsMemory((int)offset));
                }
                else
                {
                    byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head); await stream.WriteAsync(body);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync() { cancel.Cancel(); listener.Stop(); await worker; cancel.Dispose(); }
}
