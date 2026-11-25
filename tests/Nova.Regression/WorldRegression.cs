using Nova.Core;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

static class WorldRegression
{
    static void Check(bool v, string n) { if (!v) throw new Exception("FAIL " + n); Console.WriteLine("PASS " + n); }
    static void Reject<T>(Action a, string n) where T : Exception { try { a(); } catch (T) { Console.WriteLine("PASS " + n); return; } throw new Exception("FAIL " + n); }
    static void I32(Stream s, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); s.Write(b); }
    static void I64(Stream s, long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); s.Write(b); }
    static void Text(Stream s, string text)
    {
        var b = new MemoryStream();
        foreach (char c in text)
        {
            if (c >= 1 && c <= 127) b.WriteByte((byte)c);
            else if (c <= 0x7ff) { b.WriteByte((byte)(0xc0 | (c >> 6))); b.WriteByte((byte)(0x80 | (c & 63))); }
            else { b.WriteByte((byte)(0xe0 | (c >> 12))); b.WriteByte((byte)(0x80 | ((c >> 6) & 63))); b.WriteByte((byte)(0x80 | (c & 63))); }
        }
        Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)b.Length); s.Write(length); b.WriteTo(s);
    }
    static byte[] Nbt(string name, bool duplicate = false)
    {
        using var s = new MemoryStream(); s.WriteByte(10); Text(s, ""); s.WriteByte(10); Text(s, "Data");
        s.WriteByte(8); Text(s, "LevelName"); Text(s, name);
        if (duplicate) { s.WriteByte(8); Text(s, "LevelName"); Text(s, "duplicate"); }
        s.WriteByte(1); Text(s, "flag"); s.WriteByte(42);
        s.WriteByte(2); Text(s, "short"); s.WriteByte(1); s.WriteByte(2);
        s.WriteByte(3); Text(s, "int"); I32(s, 321);
        s.WriteByte(4); Text(s, "long"); I64(s, 987654);
        s.WriteByte(5); Text(s, "float"); I32(s, 123);
        s.WriteByte(6); Text(s, "double"); I64(s, 456);
        s.WriteByte(7); Text(s, "bytes"); I32(s, 3); s.Write(new byte[] { 7, 8, 9 });
        s.WriteByte(9); Text(s, "list"); s.WriteByte(8); I32(s, 2); Text(s, "one"); Text(s, "two");
        s.WriteByte(10); Text(s, "nested"); s.WriteByte(8); Text(s, "LevelName"); Text(s, "untouched nested"); s.WriteByte(0);
        s.WriteByte(11); Text(s, "ints"); I32(s, 2); I32(s, 1); I32(s, 2);
        s.WriteByte(12); Text(s, "longs"); I32(s, 2); I64(s, 3); I64(s, 4);
        s.WriteByte(0); s.WriteByte(0); return s.ToArray();
    }
    static void Gzip(string path, byte[] raw) { using var f = File.Create(path); using var g = new GZipStream(f, CompressionLevel.Optimal); g.Write(raw); }
    static byte[] Gunzip(string path) { using var f = File.OpenRead(path); using var g = new GZipStream(f, CompressionMode.Decompress); using var m = new MemoryStream(); g.CopyTo(m); return m.ToArray(); }
    public static void Run(string root)
    {
        string game = Path.Combine(root, "world-test"); string saves = Path.Combine(game, "saves");
        string world = Path.Combine(saves, "World Space"); Directory.CreateDirectory(Path.Combine(world, "region"));
        string level = Path.Combine(world, "level.dat"); Gzip(level, Nbt("Original world"));
        File.WriteAllText(Path.Combine(world, "level.dat_old"), "previous metadata");
        File.WriteAllText(Path.Combine(world, "region", "r.0.0.mca"), "important region bytes");
        Check(WorldManager.ReadDisplayName(game, "World Space") == "Original world", "world display name read from level.dat");
        byte[] before = File.ReadAllBytes(level);
        string backup = WorldManager.Rename(game, "World Space", "عالم جديد 😀");
        Check(File.ReadAllBytes(backup).SequenceEqual(before), "rename saves original metadata byte for byte");
        Check(WorldManager.ReadDisplayName(game, "World Space") == "عالم جديد 😀" && Gunzip(level).SequenceEqual(Nbt("عالم جديد 😀")), "rename changes only LevelName including Unicode");
        Check(File.ReadAllText(Path.Combine(world, "level.dat_old")) == "previous metadata" && File.ReadAllText(Path.Combine(world, "region", "r.0.0.mca")) == "important region bytes", "rename preserves old metadata and region data");
        byte[] current = File.ReadAllBytes(level);
        Reject<ArgumentException>(() => WorldManager.Rename(game, "World Space", "bad\nname"), "world rename rejects control characters");
        Check(File.ReadAllBytes(level).SequenceEqual(current), "invalid name leaves metadata unchanged");
        Reject<ArgumentException>(() => WorldManager.ResolveWorld(game, "../escape"), "world traversal rejected");
        string broken = Path.Combine(saves, "Broken"); Directory.CreateDirectory(broken); string brokenLevel = Path.Combine(broken, "level.dat"); Gzip(brokenLevel, Nbt("bad", true));
        byte[] brokenBefore = File.ReadAllBytes(brokenLevel);
        Reject<InvalidDataException>(() => WorldManager.Rename(game, "Broken", "new"), "duplicate NBT world name rejected");
        Check(File.ReadAllBytes(brokenLevel).SequenceEqual(brokenBefore), "invalid NBT preserved");
        Gzip(brokenLevel, Nbt("truncated")[..^5]); brokenBefore = File.ReadAllBytes(brokenLevel);
        Reject<InvalidDataException>(() => WorldManager.Rename(game, "Broken", "new"), "truncated NBT rejected");
        Check(File.ReadAllBytes(brokenLevel).SequenceEqual(brokenBefore), "truncated metadata preserved");
        string outside = Path.Combine(root, "outside-world"); Directory.CreateDirectory(outside);
        Gzip(Path.Combine(outside, "level.dat"), Nbt("Outside"));
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "keep");
        string link = Path.Combine(saves, "linked-world");
        if (OperatingSystem.IsWindows())
        {
            var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("/c"); info.ArgumentList.Add("mklink"); info.ArgumentList.Add("/J"); info.ArgumentList.Add(link); info.ArgumentList.Add(outside);
            using var process = Process.Start(info) ?? throw new Exception("Could not create world junction");
            process.WaitForExit(); Check(process.ExitCode == 0, "world junction created");
        }
        else Directory.CreateSymbolicLink(link, outside);
        Reject<InvalidDataException>(() => WorldManager.Rename(game, "linked-world", "changed"), "world junction rename rejected");
        Reject<InvalidDataException>(() => WorldManager.MoveToTrash(game, "linked-world"), "world junction delete rejected");
        Check(File.ReadAllText(Path.Combine(outside, "sentinel.txt")) == "keep", "world junction target untouched");
        Directory.Delete(link, false);
        if (OperatingSystem.IsWindows())
        {
            string session = Path.Combine(world, "session.lock"); File.WriteAllBytes(session, new byte[] { 1, 2, 3 });
            using (var locked = new FileStream(session, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Reject<IOException>(() => WorldManager.Rename(game, "World Space", "locked"), "locked world cannot be renamed");
                Reject<IOException>(() => WorldManager.MoveToTrash(game, "World Space"), "locked world cannot be removed");
            }
            using (var locked = new FileStream(session, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                locked.Lock(0, 1);
                Reject<IOException>(() => WorldManager.Rename(game, "World Space", "locked"), "byte-locked world cannot be renamed");
                Reject<IOException>(() => WorldManager.MoveToTrash(game, "World Space"), "byte-locked world cannot be removed");
                locked.Unlock(0, 1);
            }
        }
        long expectedBytes = Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Check(WorldManager.CalculateSize(game, "World Space") == expectedBytes, "on-demand world size counts files");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Reject<OperationCanceledException>(() => WorldManager.CalculateSize(game, "World Space", cancelled.Token), "world size scan supports cancellation");
            string modsDir = Path.Combine(game, "mods"); Directory.CreateDirectory(modsDir);
            File.WriteAllText(Path.Combine(modsDir, "test.jar"), "not a jar");
            Reject<OperationCanceledException>(() => ModScanner.Scan(modsDir, cancelled.Token), "mod scan supports cancellation");
        }
        string versions = Path.Combine(root, "world-versions");
        void WriteVersion(string id, string json) { string d = Path.Combine(versions, id); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, id + ".json"), json); }
        WriteVersion("1.20.1", "{\"id\":\"1.20.1\",\"mainClass\":\"Main\",\"arguments\":{\"game\":[{\"rules\":[{\"action\":\"allow\",\"features\":{\"is_quick_play_singleplayer\":true}}],\"value\":[\"--quickPlaySingleplayer\",\"${quickPlaySingleplayer}\"]}]}} ");
        WriteVersion("fabric-test", "{\"id\":\"fabric-test\",\"inheritsFrom\":\"1.20.1\",\"mainClass\":\"FabricMain\"}");
        WriteVersion("1.19.4", "{\"id\":\"1.19.4\",\"mainClass\":\"Main\"}");
        var supported = VersionResolver.Resolve("fabric-test", versions); var old = VersionResolver.Resolve("1.19.4", versions);
        Check(supported.SupportsQuickPlaySingleplayer && !old.SupportsQuickPlaySingleplayer, "quick play detected in inherited Minecraft metadata");
        var paths = new LauncherPaths(Path.Combine(root, "world-launch-root")); var instance = new InstanceConfig { Id="test", GameDirectory=game, VersionId="fabric-test" }; var java = new JavaInstall("java", 17, "17", "test");
        var normal = LaunchBuilder.Build(instance, supported, paths, java, Path.Combine(root, "natives"));
        Check(!normal.Arguments.Contains("--quickPlaySingleplayer"), "normal launch does not select a world");
        var quick = LaunchBuilder.Build(instance, supported, paths, java, Path.Combine(root, "natives"), null, "World Space");
        int index = quick.Arguments.IndexOf("--quickPlaySingleplayer");
        Check(index >= 0 && quick.Arguments[index + 1] == "World Space" && quick.Arguments.Count(a => a == "--quickPlaySingleplayer") == 1, "quick play passes exact world folder as one argument");
        Reject<InvalidOperationException>(() => LaunchBuilder.Build(instance, old, paths, java, Path.Combine(root, "natives"), null, "World Space"), "old versions reject direct world join");
        instance.GameArgs.Add("--quickPlaySingleplayer");
        Reject<InvalidOperationException>(() => LaunchBuilder.Build(instance, supported, paths, java, Path.Combine(root, "natives"), null, "World Space"), "conflicting custom quick play rejected");
        string trash = WorldManager.MoveToTrash(game, "World Space");
        Check(!Directory.Exists(world) && File.ReadAllText(Path.Combine(trash, "region", "r.0.0.mca")) == "important region bytes", "delete moves complete world to recoverable trash");
    }
}
