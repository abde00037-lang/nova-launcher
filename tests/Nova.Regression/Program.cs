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
        string settingsRoot = Path.Combine(root, "settings-migration"); Directory.CreateDirectory(settingsRoot);
        var settingsPaths = new LauncherPaths(settingsRoot);
        string settingsFile = Path.Combine(settingsRoot, "settings.json");
        File.WriteAllText(settingsFile, "{\"curseForgeKey\":\"example-secret-test\",\"accent\":\"Blue\"}");
        var settings = LauncherSettings.Load(settingsPaths);
        Check(settings.CurseForgeKey == "example-secret-test" && settings.Accent == "Blue", "legacy CurseForge key read");
        string settingsJson = File.ReadAllText(settingsFile);
        Check(!settingsJson.Contains("example-secret-test") && !settingsJson.Contains("\"curseForgeKey\"") && settingsJson.Contains("protectedCurseForgeKey"), "legacy CurseForge key migrated to DPAPI");
        Check(LauncherSettings.Load(settingsPaths).CurseForgeKey == "example-secret-test", "protected CurseForge key round trip");
        settings.CurseForgeKey = null; settings.Save(settingsPaths);
        Check(!File.ReadAllText(settingsFile).Contains("protectedCurseForgeKey"), "cleared CurseForge key removed");
        File.WriteAllText(settingsFile, "{\"protectedCurseForgeKey\":\"invalid-data\",\"accent\":\"Blue\"}");
        var locked = LauncherSettings.Load(settingsPaths); locked.Accent = "Red"; locked.Save(settingsPaths);
        Check(File.ReadAllText(settingsFile).Contains("invalid-data"), "unavailable protected key preserved on settings save");
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
    WorldRegression.Run(root);
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

        // --- BYOK AI assistant layer: local fake server only, no external network and no real keys ---
        int aiPort;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); aiPort = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        using var aiListener = new HttpListener();
        aiListener.Prefixes.Add($"http://127.0.0.1:{aiPort}/");
        aiListener.Start();
        var aiServer = Task.Run(async () =>
        {
            while (aiListener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await aiListener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() =>
                {
                    string path = ctx.Request.Url!.AbsolutePath;
                    string body; int status = 200;
                    if (path == "/oai/v1/chat/completions")
                    {
                        using var reqReader = new StreamReader(ctx.Request.InputStream);
                        string reqBody = reqReader.ReadToEnd();
                        if (reqBody.Contains("bad-model")) { status = 500; body = "{\"error\":\"boom\"}"; }
                        else body = "{\"choices\":[{\"message\":{\"content\":\"HELLO-OPENAI\"}}]}";
                    }
                    else if (path == "/oai/v1/models") body = "{\"data\":[{\"id\":\"bad-model\"},{\"id\":\"good-model\"}]}";
                    else if (path == "/anth/v1/messages") body = "{\"content\":[{\"type\":\"text\",\"text\":\"HELLO-ANTHROPIC\"}]}";
                    else { status = 404; body = "{\"error\":\"nf\"}"; }
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                });
            }
        });
        var aiPaths = new LauncherPaths(Path.Combine(root, "ai-root"));
        Directory.CreateDirectory(aiPaths.Root);
        var aiProv = new AiProvider { Name = "testprov", Protocol = AiProtocol.OpenAiCompatible, BaseUrl = $"http://127.0.0.1:{aiPort}/oai/v1", Models = new List<string> { "bad-model", "good-model" } };
        AiStore.SaveProvider(aiPaths, aiProv);
        AiStore.SetKey(aiPaths, "testprov", "sk-test-secret-abc123");
        Check(AiStore.LoadProviders(aiPaths).Single().Models.Count == 2, "ai provider config round trip");
        Check(!File.ReadAllText(AiStore.KeysFile(aiPaths)).Contains("sk-test-secret-abc123"), "ai key never stored in plaintext");
        using (var aiClient = new AiClient(30))
        {
            var aiReply = await aiClient.ChatAsync(aiProv, AiStore.GetKey(aiPaths, "testprov")!, null, "hi", 64);
            Check(aiReply.Model == "good-model" && aiReply.Text == "HELLO-OPENAI", "ai model fallback after provider error");
            Check((await aiClient.ListModelsAsync(aiProv, "k")).Contains("good-model"), "ai model listing");
            var anthProv = new AiProvider { Name = "anth", Protocol = AiProtocol.Anthropic, BaseUrl = $"http://127.0.0.1:{aiPort}/anth", Models = new List<string> { "am1" } };
            var anthReply = await aiClient.ChatAsync(anthProv, "k", "sys", "hi", 64);
            Check(anthReply.Text == "HELLO-ANTHROPIC", "ai anthropic protocol parse");
            var deadProv = new AiProvider { Name = "dead", Protocol = AiProtocol.OpenAiCompatible, BaseUrl = $"http://127.0.0.1:{aiPort}/nope", Models = new List<string> { "m" } };
            try { await aiClient.ChatAsync(deadProv, "k", null, "hi", 8); throw new Exception("accepted failing provider"); }
            catch (AiException) { Console.WriteLine("PASS ai all-model failure reported"); }
        }
        Reject(() => AiStore.SaveProvider(aiPaths, new AiProvider { Name = "bad name", BaseUrl = "http://x", Models = new List<string> { "m" } }), "ai rejects bad provider name");
        Reject(() => AiStore.SaveProvider(aiPaths, new AiProvider { Name = "ok", BaseUrl = "ftp://x", Models = new List<string> { "m" } }), "ai rejects non-http base url");
        Reject(() => AiStore.SaveProvider(aiPaths, new AiProvider { Name = "ok", BaseUrl = "http://x", Models = new List<string>() }), "ai rejects empty model list");
        Check(AiStore.RemoveProvider(aiPaths, "testprov") && AiStore.GetKey(aiPaths, "testprov") == null, "ai provider removal clears key");
        aiListener.Stop();
        try { await aiServer.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }

        // --- Supervised agent loop: fake provider issues tool calls; approval gate denies writes ---
        int agPort;
        using (var probe2 = new TcpListener(IPAddress.Loopback, 0)) { probe2.Start(); agPort = ((IPEndPoint)probe2.LocalEndpoint).Port; probe2.Stop(); }
        using var agListener = new HttpListener();
        agListener.Prefixes.Add($"http://127.0.0.1:{agPort}/");
        agListener.Start();
        int agStep = 0;
        var agServer = Task.Run(async () =>
        {
            while (agListener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await agListener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() =>
                {
                    string path = ctx.Request.Url!.AbsolutePath;
                    int step = Interlocked.Increment(ref agStep);
                    string body;
                    if (path == "/ag/v1/chat/completions")
                    {
                        body = step == 1
                            ? "{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"list_instances\",\"arguments\":\"{}\"}},{\"id\":\"c2\",\"type\":\"function\",\"function\":{\"name\":\"toggle_mod\",\"arguments\":\"{\\\"id\\\":\\\"t1\\\",\\\"file\\\":\\\"dummy-mod.jar\\\",\\\"enabled\\\":false}\"}}]}}]}"
                            : "{\"choices\":[{\"message\":{\"content\":\"AGENT-DONE\"}}]}";
                    }
                    else if (path == "/ag/v1/messages")
                    {
                        body = step == 1
                            ? "{\"content\":[{\"type\":\"tool_use\",\"id\":\"a1\",\"name\":\"list_instances\",\"input\":{}}]}"
                            : "{\"content\":[{\"type\":\"text\",\"text\":\"ANTH-AGENT-DONE\"}]}";
                    }
                    else { body = "{\"error\":\"nf\"}"; ctx.Response.StatusCode = 404; }
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    if (ctx.Response.StatusCode == 200) ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                });
            }
        });
        var agPaths = new LauncherPaths(Path.Combine(root, "ai-agent"));
        Directory.CreateDirectory(agPaths.Root);
        List<string> handled = new List<string>();
        List<string> denied = new List<string>();
        AiToolHandler handler = (call, ct) => { handled.Add(call.Name); return Task.FromResult(call.Name == "list_instances" ? "INSTANCE-FAKE" : "SHOULD-NOT-RUN"); };
        AiApprovalGate gate = (spec, call, ct) => { denied.Add(call.Name); return Task.FromResult(false); };
        using (var agClient = new AiClient(30))
        {
            var agAgent = new AiAgent(agClient);
            var agProv = new AiProvider { Name = "ag", Protocol = AiProtocol.OpenAiCompatible, BaseUrl = $"http://127.0.0.1:{agPort}/ag/v1", Models = new List<string> { "m" } };
            var agResult = await agAgent.RunAsync(agProv, "k", "sys", "task", handler, gate);
            Check(agResult.Text == "AGENT-DONE", "agent loop finishes with final answer");
            Check(handled.Contains("list_instances") && !handled.Contains("toggle_mod"), "agent runs read-only tool only");
            Check(denied.Contains("toggle_mod") && agResult.Trace.Any(t => t.Contains("DENIED")), "agent write tool denied by gate");
            var anthProv = new AiProvider { Name = "aga", Protocol = AiProtocol.Anthropic, BaseUrl = $"http://127.0.0.1:{agPort}/ag", Models = new List<string> { "m" } };
            var anthResult = await agAgent.RunAsync(anthProv, "k", "sys", "task", handler, gate);
            Check(anthResult.Text == "ANTH-AGENT-DONE", "agent loop works over anthropic protocol");
        }
        agListener.Stop();
        try { await agServer.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }

        // --- AiToolbox behaviours on a disposable instance ---
        var tbPaths = new LauncherPaths(Path.Combine(root, "ai-tools"));
        Directory.CreateDirectory(tbPaths.Root);
        string tbGame = Path.Combine(tbPaths.Root, "instances", "t1", "game");
        string tbMods = Path.Combine(tbGame, "mods");
        Directory.CreateDirectory(tbMods);
        Directory.CreateDirectory(Path.Combine(tbGame, "logs"));
        var tbStore = new InstanceStore(tbPaths);
        tbStore.Save(new InstanceConfig { Id = "t1", Name = "T1", GameDirectory = tbGame, VersionId = "1.20.1-fabric" });
        File.WriteAllText(Path.Combine(tbGame, "logs", "latest.log"), "line1\nauthorization: supersecrettoken\nline3\n");
        string logOut = await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "read_log", "{\"id\":\"t1\",\"lines\":20}"));
        Check(logOut.Contains("REDACTED") && !logOut.Contains("supersecrettoken"), "agent read_log redacts secrets");
        File.WriteAllText(Path.Combine(tbMods, "dummy-mod.jar"), "x");
        await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "toggle_mod", "{\"id\":\"t1\",\"file\":\"dummy-mod.jar\",\"enabled\":false}"));
        Check(File.Exists(Path.Combine(tbMods, "dummy-mod.jar.disabled")), "agent toggle_mod disables mod file");
        await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "set_memory_mb", "{\"id\":\"t1\",\"mb\":8192}"));
        Check(tbStore.Find("t1")!.MemoryMb == 8192, "agent set_memory_mb persists");
        Check((await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "set_memory_mb", "{\"id\":\"t1\",\"mb\":99}"))).StartsWith("memory must"), "agent memory out of range rejected");
        void FakeModJar(string file, string modid)
        {
            using var fs = File.Create(Path.Combine(tbMods, file));
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("fabric.mod.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"schemaVersion\":1,\"id\":\"" + modid + "\",\"version\":\"1.0\"}");
        }
        FakeModJar("dup-a.jar", "dupmod");
        FakeModJar("dup-b.jar", "dupmod");
        string conflicts = await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "mod_conflicts", "{\"id\":\"t1\"}"));
        Check(conflicts.Contains("duplicate modid dupmod"), "agent mod_conflicts finds duplicate modids");
        Check((await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "check_mod_updates", "{\"id\":\"t1\"}"))) == "mods are up to date", "agent check_mod_updates empty mods offline");
        string exportOut = await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "export_pack", "{\"id\":\"t1\"}"));
        Check(exportOut.StartsWith("exported ") && Directory.Exists(Path.Combine(tbPaths.Root, "packs")), "agent export_pack writes into packs folder");
        byte[] dupOriginal = File.ReadAllBytes(Path.Combine(tbMods, "dup-a.jar"));
        byte[] dupBOriginal = File.ReadAllBytes(Path.Combine(tbMods, "dup-b.jar"));
        string snap = await ModSnapshot.CreateAsync(tbMods);
        string snapId = Path.GetFileName(snap);
        var listedSnapshots = await ModSnapshot.ListAsync(tbMods);
        Check(listedSnapshots.Count == 1 && listedSnapshots[0].Id == snapId && listedSnapshots[0].FileCount >= 2,
            "mod snapshot list exposes valid snapshots and file counts");
        string corruptSnapshot = Path.Combine(tbMods, ".nova-mod-snapshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(corruptSnapshot);
        File.WriteAllText(Path.Combine(corruptSnapshot, "manifest.json"),
            """{"CreatedUtc":"2026-01-01T00:00:00Z","Files":[{"FileName":"../escape.jar","Sha256":"bad"}]}""");
        Check((await ModSnapshot.ListAsync(tbMods)).Count == 1, "mod snapshot list ignores malformed or unsafe manifests");
        string snapshotDupB = Path.Combine(snap, "files", "dup-b.jar");
        File.WriteAllText(snapshotDupB, "tampered snapshot content");
        File.WriteAllText(Path.Combine(tbMods, "dup-a.jar"), "before failed restore");
        try
        {
            await ModSnapshot.RestoreAsync(tbMods, snapId);
            throw new Exception("FAIL corrupt snapshot restored");
        }
        catch (IOException e) when (e.Message.Contains("failed verification", StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine("PASS corrupt mod snapshot rejected before restore"); }
        Check(File.ReadAllText(Path.Combine(tbMods, "dup-a.jar")) == "before failed restore",
            "corrupt later snapshot file does not partially restore earlier mods");
        File.WriteAllBytes(snapshotDupB, dupBOriginal);
        File.WriteAllBytes(Path.Combine(tbMods, "dup-a.jar"), Encoding.UTF8.GetBytes("changed-content"));
        File.WriteAllText(Path.Combine(tbMods, "extra-mod.jar"), "new");
        string restoreOut = await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "restore_mods", "{\"id\":\"t1\",\"snapshot\":\"" + snapId + "\"}"));
        Check(restoreOut.StartsWith("restored ") && restoreOut.Contains("recovery snapshot "), "agent restore_mods round trip creates a recovery snapshot");
        string recoveryId = restoreOut.Split("recovery snapshot ", StringSplitOptions.None).Last();
        Check(File.ReadAllText(Path.Combine(tbMods, ".nova-mod-snapshots", recoveryId, "files", "dup-a.jar")) == "changed-content",
            "restore recovery snapshot preserves the pre-restore mod content");
        Check(File.ReadAllBytes(Path.Combine(tbMods, "dup-a.jar")).SequenceEqual(dupOriginal), "restore_mods copies verified snapshot content back");
        Check(File.Exists(Path.Combine(tbMods, "extra-mod.jar.disabled")), "restore_mods disables mods added after snapshot");
        Check((await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "restore_mods", "{\"id\":\"t1\",\"snapshot\":\"zz\"}"))).StartsWith("snapshot id must"), "agent restore_mods rejects bad snapshot id");
        Check((await AiToolbox.HandleAsync(tbPaths, new AiToolCall("x", "install_mod", "{\"id\":\"nope\",\"slug\":\"x\"}"))).StartsWith("unknown instance"), "agent install_mod validates instance");

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
