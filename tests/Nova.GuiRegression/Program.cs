using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Nova.App.ViewModels;
using Nova.Core;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL " + message);
        Console.WriteLine("PASS " + message);
    }

    private static void WriteNbt(string path, string name)
    {
        static void Text(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.WriteByte((byte)(bytes.Length >> 8)); stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Fastest);
        gz.WriteByte(10); Text(gz, ""); gz.WriteByte(10); Text(gz, "Data");
        gz.WriteByte(8); Text(gz, "LevelName"); Text(gz, name); gz.WriteByte(0); gz.WriteByte(0);
    }

    private static async Task WaitFor(Func<bool> predicate, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(40);
        }
        throw new Exception("Timeout waiting for " + label);
    }

    private static async Task Run(string root)
    {
        var paths = new LauncherPaths(root); paths.EnsureCreated();
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"AutoCheckUpdates\":false}");
        var meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        InstanceVm Create(string id, string title, int count)
        {
            string game = Path.Combine(root, "instances", id, "game");
            string world = Path.Combine(game, "saves", "Shared");
            string mods = Path.Combine(game, "mods");
            Directory.CreateDirectory(world); Directory.CreateDirectory(mods);
            WriteNbt(Path.Combine(world, "level.dat"), title);
            for (int i = 0; i < count; i++)
            {
                string name = $"{id}-{i:D3}.jar";
                File.WriteAllText(Path.Combine(mods, name), "isolated synthetic mod");
                meta[name.ToLowerInvariant()] = new { title = name, description = "fixture", source = "fixture" };
            }
            return new InstanceVm(new InstanceConfig { Id = id, Name = id, GameDirectory = game, VersionId = "1.19.4" }, count, "test", null);
        }
        var a = Create("A", "World A", 200);
        var b = Create("B", "World B", 1);
        File.WriteAllText(Path.Combine(paths.Cache, "mod-meta.json"), JsonSerializer.Serialize(meta));
        var vm = new MainViewModel(paths);
        vm.OnActivePageChanged("worlds"); vm.Selected = a;
        await WaitFor(() => vm.Worlds.Count == 1 && vm.Worlds[0].Title == "World A", "world A");
        await Task.Delay(200);
        Check(vm.Mods.Count == 0, "world page does not scan or populate mods");
        SimpleRow stale = vm.Worlds[0];
        vm.Selected = b;
        try { await vm.JoinWorldAsync(stale); throw new Exception("FAIL stale world row accepted"); }
        catch (InvalidOperationException e) when (e.Message.Contains("selected instance changed", StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine("PASS stale world row rejected"); }
        vm.OnActivePageChanged("mods");
        for (int i = 0; i < 20; i++) { vm.Selected = a; await Task.Delay(5); vm.Selected = b; await Task.Delay(5); }
        vm.OnActivePageChanged("worlds");
        await WaitFor(() => vm.Worlds.Count == 1 && vm.Worlds[0].Title == "World B", "final world B");
        await Task.Delay(500);
        Check(vm.Worlds.Count == 1 && vm.Worlds[0].Title == "World B", "rapid switching does not show old worlds");
        vm.OnActivePageChanged("mods");
        await WaitFor(() => vm.Mods.Count == 1 && vm.Mods[0].FileName == "B-000.jar", "final mods B");
        await Task.Delay(500);
        Check(vm.Mods.Count == 1 && vm.Mods[0].FileName == "B-000.jar", "rapid switching does not show old mods");
        vm.OnActivePageChanged("worlds"); vm.Selected = null;
        await Task.Delay(500); // Let cancelled, isolated scans release their file handles.
    }

    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "nova-gui-regression-" + Guid.NewGuid().ToString("N"));
        int exit = 1;
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try { await Run(root); exit = 0; }
            catch (Exception e) { Console.Error.WriteLine(e); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        return exit;
    }
}
