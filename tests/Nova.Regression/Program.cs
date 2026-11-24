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
        string staged = await LauncherUpdater.StageUpdateAsync(paths, update);
        Check(File.Exists(Path.Combine(staged, "Nova.Core.dll")), "valid update staged");
        File.WriteAllText(Path.Combine(root, "sentinel"), "safe");
        var bad = update with { ChecksumUrl = url + "/BADSUMS.txt" };
        try { await LauncherUpdater.StageUpdateAsync(paths, bad); throw new Exception("accepted mismatch"); } catch (InvalidDataException) { Console.WriteLine("PASS checksum mismatch rejected before staging"); }
        Check(File.ReadAllText(Path.Combine(root, "sentinel")) == "safe", "data unchanged");
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
    public string Url { get; }
    public UpdateFixture()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("Nova.Core.dll").Open())) writer.Write("test binary only");
        zip = memory.ToArray();
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
                string? header;
                do { header = await reader.ReadLineAsync(); } while (!string.IsNullOrEmpty(header));
                byte[] body = line.Contains("/asset.zip ") ? zip : line.Contains("/BADSUMS.txt ") ? bad : good;
                byte[] prefix = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(prefix); await stream.WriteAsync(body);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync() { cancel.Cancel(); listener.Stop(); await worker; cancel.Dispose(); }
}
