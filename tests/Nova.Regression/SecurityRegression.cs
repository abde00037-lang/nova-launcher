using Nova.Core;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using System.Diagnostics;

static class SecurityRegression
{
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    static async Task Reject(Func<Task> action, string name) { try { await action(); } catch (InvalidDataException) { Console.WriteLine("PASS " + name); return; } throw new Exception("FAIL " + name); }
    static string Zip(string root, params (string name, string content)[] entries)
    {
        string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries) using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
        return path;
    }
    public static async Task Run(string root)
    {
        Check(!GameLogFormatter.ShouldShow("Missing sound for event foo") && GameLogFormatter.ShouldShow("[thread/ERROR] failed"), "game log filter unchanged");
        Check(GameLogFormatter.LevelOf("[thread/WARN] warning") == "WARN" && GameLogFormatter.CleanLine("[thread/INFO]: hello") == "hello", "game log formatting unchanged");
        string sandbox = Path.Combine(root, "security-tests"); Directory.CreateDirectory(sandbox);
        string dest = Path.Combine(sandbox, "natives"); Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "keep.txt"), "keep");
        foreach (string path in new[] { "../../outside.txt", "../outside", @"..\..\outside", "/absolute", @"C:\outside", "C:/outside", @"\\server\share\file", "a/../outside", "a/evil:stream", "NUL.txt", "trailing./file" })
        {
            string jar = Zip(sandbox, ("ok/native.dll", "ok"), (path, "evil"));
            await Reject(() => { NativeExtractor.ExtractAll(new[] { jar }, dest); return Task.CompletedTask; }, "native rejects " + path);
            Check(File.ReadAllText(Path.Combine(dest, "keep.txt")) == "keep" && !File.Exists(Path.Combine(dest, "ok", "native.dll")), "native no writes before validation");
        }
        string installerBad = Zip(sandbox, ("safe/a.dll", "okay"), ("../../outside.txt", "evil"));
        string installerOut = Path.Combine(sandbox, "installer-out");
        await Reject(() => { using var archive = ZipFile.OpenRead(installerBad); SafeInstallerZip.Extract(archive, installerOut, x => x); return Task.CompletedTask; }, "installer ZIP traversal rejected");
        Check(!Directory.Exists(installerOut), "installer rejects before extraction");
        string installerGood = Zip(sandbox, ("bin/Nova.dll", "okay"));
        using (var archive = ZipFile.OpenRead(installerGood)) SafeInstallerZip.Extract(archive, installerOut, x => x);
        Check(File.ReadAllText(Path.Combine(installerOut, "bin", "Nova.dll")) == "okay", "installer normal extraction");
        var smallActual = Zip(sandbox, ("large.dll", new string('x', 1000)));
        byte[] forged = File.ReadAllBytes(smallActual);
        for (int i = 0; i < forged.Length - 46; i++)
            if (forged[i] == 0x50 && forged[i+1] == 0x4b && forged[i+2] == 0x01 && forged[i+3] == 0x02)
            { BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(i + 24, 4), 10); break; }
        File.WriteAllBytes(smallActual, forged);
        await Reject(() => { NativeExtractor.ExtractAll(new[] { smallActual }, dest, limits: new ArchiveLimits { MaxEntries=20, MaxFileBytes=100, MaxTotalBytes=500, MaxManifestBytes=100 }); return Task.CompletedTask; }, "forged size or corrupt CRC rejected");
        Check(File.ReadAllText(Path.Combine(dest, "keep.txt")) == "keep" && !File.Exists(Path.Combine(dest, "large.dll")), "oversized extraction rolled back");
        var valid = Zip(sandbox, ("META-INF/MANIFEST.MF", "metadata"), ("lib/native.dll", "data"), ("config/a..b", "valid"));
        Check(NativeExtractor.ExtractAll(new[] { valid }, dest) == 2 && File.ReadAllText(Path.Combine(dest, "lib", "native.dll")) == "data", "native normal extraction");
        Check(!File.Exists(Path.Combine(dest, "META-INF", "MANIFEST.MF")), "native ignores metadata");
        string packRoot = Path.Combine(sandbox, "pack-root"); var paths = new LauncherPaths(packRoot);
        foreach (string badPath in new[] { "../../escaped", @"..\..\escaped", "/absolute", "C:/evil", @"\\server\share\evil", "mods/../outside" })
        {
            string index = JsonSerializer.Serialize(new { name="badpack", files=new[] { new { path=badPath, fileSize=1, downloads=Array.Empty<string>() } } });
            string file = Zip(sandbox, ("modrinth.index.json", index));
            await Reject(() => PackImporter.ImportAsync(file, paths), "modrinth rejects " + badPath);
            Check(!Directory.Exists(paths.Instances) || !Directory.EnumerateFileSystemEntries(paths.Instances).Any(), "no partial instance after rejection");
        }
        string badMetadata = Zip(sandbox, ("modrinth.index.json", "{\"name\":\"bad\",\"files\":[],\"dependencies\":{\"minecraft\":\"../../escape\",\"fabric-loader\":\"0.15\"}}"));
        await Reject(() => PackImporter.ImportAsync(badMetadata, paths), "malicious version metadata rejected before loader setup");
        string overrideZip = Zip(sandbox, ("modrinth.index.json", "{\"name\":\"bad\",\"files\":[]}"), ("overrides/good.txt", "good"), ("overrides/../../escape", "evil"));
        await Reject(() => PackImporter.ImportAsync(overrideZip, paths), "override traversal rejected");
        Check(!Directory.Exists(paths.Instances) || !Directory.EnumerateFileSystemEntries(paths.Instances).Any(), "override cleanup");
        string normal = Zip(sandbox, ("nova-pack.json", "{\"name\":\"normal\"}"), ("mods/test.jar", "mod"), ("config/options.txt", "safe"));
        var imported = await PackImporter.ImportAsync(normal, paths);
        Check(File.ReadAllText(Path.Combine(imported.GameDirectory, "config", "options.txt")) == "safe", "normal pack import");
        var small = new ArchiveLimits { MaxEntries=2, MaxFileBytes=1024, MaxTotalBytes=2048, MaxManifestBytes=1024 };
        await Reject(() => PackImporter.ImportAsync(normal, paths, limits: small), "zip entry limit");
        var tiny = new ArchiveLimits { MaxEntries=100, MaxFileBytes=100, MaxTotalBytes=20, MaxManifestBytes=100 };
        await Reject(() => PackImporter.ImportAsync(normal, paths, limits: tiny), "zip total limit");
        string fat = Zip(sandbox, ("nova-pack.json", "{}"), ("mods/large.jar", new string('x', 1000)));
        await Reject(() => PackImporter.ImportAsync(fat, paths, limits: new ArchiveLimits { MaxEntries=100, MaxFileBytes=100, MaxTotalBytes=5000, MaxManifestBytes=100 }), "zip individual file limit");
        {
            string outside = Path.Combine(sandbox, "outside"); Directory.CreateDirectory(outside);
            string linkRoot = Path.Combine(sandbox, "linkroot"); Directory.CreateDirectory(linkRoot);
            string link = Path.Combine(linkRoot, "link");
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
                start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(outside);
                using var process = Process.Start(start) ?? throw new Exception("could not start junction test"); await process.WaitForExitAsync();
                Check(process.ExitCode == 0, "created Windows junction for test");
            }
            else Directory.CreateSymbolicLink(link, outside);
            await Reject(() => { SafePath.ResolveWithin(linkRoot, "link/test"); return Task.CompletedTask; }, "existing symlink rejected");
            Check(!File.Exists(Path.Combine(outside, "test")), "symlink target untouched");
            Directory.Delete(link, false); // Delete the link itself, never recurse through it.
        }
        await using var server = new DownloadFixture();
        string remoteHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("response"))).ToLowerInvariant();
        string goodIndex = JsonSerializer.Serialize(new { name="modrinth-good", files=new[] { new { path="mods/test.jar", fileSize=8, hashes=new { sha1=remoteHash }, downloads=new[] { server.Url } } } });
        string goodModrinth = Zip(sandbox, ("modrinth.index.json", goodIndex), ("overrides/config/good.txt", "okay"));
        var importedModrinth = await PackImporter.ImportAsync(goodModrinth, paths);
        Check(File.ReadAllText(Path.Combine(importedModrinth.GameDirectory, "mods", "test.jar")) == "response" && File.ReadAllText(Path.Combine(importedModrinth.GameDirectory, "config", "good.txt")) == "okay", "normal Modrinth download and overrides");
        string lyingIndex = JsonSerializer.Serialize(new { name="modrinth-lying", files=new[] { new { path="mods/test.jar", fileSize=1, downloads=new[] { server.Url } } } });
        string lying = Zip(sandbox, ("modrinth.index.json", lyingIndex));
        try { await PackImporter.ImportAsync(lying, paths); throw new Exception("FAIL oversized Modrinth download"); }
        catch (DownloadLimitException) { Console.WriteLine("PASS oversized Modrinth download rejected"); }
        Check(!Directory.EnumerateDirectories(paths.Instances, ".nova-import-*").Any() && !Directory.Exists(Path.Combine(paths.Instances, "modrinth-lying")), "failed Modrinth import fully cleaned");
        string one = Path.Combine(sandbox, "one.bin");
        using var d1 = new Downloader(4); using var d2 = new Downloader(4);
        int initialRequests = server.Requests;
        var results = await Task.WhenAll(d1.EnsureAsync(one, server.Url, null), d2.EnsureAsync(one, server.Url, null));
        Check(results.Count(r => r == FetchResult.Downloaded) == 1 && results.Count(r => r == FetchResult.AlreadyValid) == 1 && server.Requests == initialRequests + 1, "same-path downloads deduplicated across instances");
        string processTarget = Path.Combine(sandbox, "cross-process.bin");
        var processInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            processInfo.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        processInfo.ArgumentList.Add("--download-worker"); processInfo.ArgumentList.Add(server.Url); processInfo.ArgumentList.Add(processTarget);
        int crossBefore = server.Requests;
        using (var p1 = Process.Start(processInfo) ?? throw new Exception("worker 1 failed"))
        using (var p2 = Process.Start(processInfo) ?? throw new Exception("worker 2 failed"))
        {
            await Task.WhenAll(p1.WaitForExitAsync(), p2.WaitForExitAsync());
            Check(p1.ExitCode == 0 && p2.ExitCode == 0 && server.Requests == crossBefore + 1, "same-path downloads deduplicated across processes");
        }
        string cancelPath = Path.Combine(sandbox, "cancel");
        var first = d1.EnsureAsync(cancelPath, server.Url, null);
        await Task.Delay(40);
        using (var cancel = new CancellationTokenSource(50))
        {
            try { await d2.EnsureAsync(cancelPath, server.Url, null, cancel.Token); throw new Exception("FAIL waiting cancellation"); }
            catch (OperationCanceledException) { Console.WriteLine("PASS waiting cancellation"); }
        }
        await first;
        Check(await d2.EnsureAsync(cancelPath, server.Url, null) == FetchResult.AlreadyValid, "path lock released after cancellation");
        int before = server.Requests;
        await Task.WhenAll(d1.EnsureAsync(Path.Combine(sandbox, "two"), server.Url, null), d1.EnsureAsync(Path.Combine(sandbox, "three"), server.Url, null));
        Check(server.Requests == before + 2 && server.Peak >= 2, "different paths download concurrently");
        string limited = Path.Combine(sandbox, "limited");
        try { await d1.EnsureAsync(limited, server.Url, null, maxBytes: 1); throw new Exception("FAIL download size limit"); }
        catch (DownloadLimitException) { Console.WriteLine("PASS download size limit"); }
        Check(!File.Exists(limited) && !Directory.EnumerateFiles(sandbox, "limited*.part").Any(), "limited download cleanup");
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("response")));
        await d1.EnsureAsync(one, server.Url, hash);
        File.WriteAllText(one, "modified");
        Check(await d1.EnsureAsync(one, server.Url, hash) == FetchResult.Downloaded && File.ReadAllText(one) == "response", "stale hash not trusted");
    }
}

sealed class DownloadFixture : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0); readonly CancellationTokenSource stop = new(); readonly Task worker;
    int active, peak, requests;
    public int Requests => requests; public int Peak => peak; public string Url { get; }
    public DownloadFixture() { listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/file"; worker = Serve(); }
    async Task Serve()
    {
        try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Handle(client); } }
        catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
    }
    async Task Handle(TcpClient client)
    {
        using (client) using (var stream=client.GetStream()) using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
        {
            try
            {
                string? line; do { line=await reader.ReadLineAsync(); } while (!string.IsNullOrEmpty(line));
                Interlocked.Increment(ref requests); int n=Interlocked.Increment(ref active); int p;
                do { p=peak; if (n<=p) break; } while (Interlocked.CompareExchange(ref peak,n,p)!=p);
                await Task.Delay(250);
                byte[] bytes=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 8\r\nConnection: close\r\n\r\nresponse"); await stream.WriteAsync(bytes);
                Interlocked.Decrement(ref active);
            }
            catch (IOException) { Interlocked.Decrement(ref active); }
        }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await worker; stop.Dispose(); }
}
