using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using Nova.App;
using Nova.App.ViewModels;
using Nova.App.Views;
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

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static Rect Bounds(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));


    private static async Task AiChatRegression(string root)
    {
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)) { probe.Start(); port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() =>
                {
                    string body = "{\"choices\":[{\"message\":{\"content\":\"HELLO-FROM-FAKE-PROVIDER\"}}]}";
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                });
            }
        });
        var paths = new LauncherPaths(Path.Combine(root, "ai-chat"));
        Directory.CreateDirectory(paths.Root);
        var vm = new Nova.App.ViewModels.AiChatViewModel(paths);
        vm.InputText = "hello";
        await vm.SendAsync();
        Check(vm.Messages.Last().IsSystem && vm.Messages.Last().Text.Contains("nova-cli ai add"), "chat guides when no provider configured");
        AiStore.SaveProvider(paths, new AiProvider { Name = "fake", Protocol = AiProtocol.OpenAiCompatible, BaseUrl = $"http://127.0.0.1:{port}/oai/v1", Models = new List<string> { "m1" } });
        AiStore.SetKey(paths, "fake", "sk-fake-key");
        vm.RefreshProviders();
        Check(vm.Providers.Count == 1 && vm.SelectedProvider?.Name == "fake", "chat picks configured provider");
        vm.InputText = "hello";
        await vm.SendAsync();
        Check(vm.Messages.Last().IsAssistant && vm.Messages.Last().Text == "HELLO-FROM-FAKE-PROVIDER", "chat reply rendered from provider");
        Check(!File.ReadAllText(AiStore.KeysFile(paths)).Contains("sk-fake-key"), "chat key stays encrypted");
        listener.Stop();
        try { await server.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
    }

    private static bool? RunApprovalDialog(Window owner, string buttonAutomationId)
    {
        var spec = new AiToolSpec("install_mod", "Install a mod", "{}", false);
        var call = new AiToolCall("call-1", "install_mod", "{\"instance_id\":\"fixture\",\"query\":\"example\"}");
        var dialog = new AiApprovalWindow(spec, call) { Owner = owner };
        Exception? callbackError = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var buttons = Descendants<Button>(dialog).ToArray();
                var target = buttons.FirstOrDefault(button =>
                    System.Windows.Automation.AutomationProperties.GetAutomationId(button) == buttonAutomationId);
                Check(target != null, "AI approval card exposes " + buttonAutomationId);
                Check(buttons.Any(button => Equals(button.Content, "ALLOW")) &&
                      buttons.Any(button => Equals(button.Content, "DENY")),
                      "AI approval card shows explicit allow and deny actions");
                Check(Descendants<TextBlock>(dialog).Any(text => text.Text.Contains("install_mod")) &&
                      Descendants<TextBlock>(dialog).Any(text => text.Text.Contains("fixture")),
                      "AI approval card displays tool and arguments");
                target!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex)
            {
                callbackError = ex;
                dialog.Close();
            }
        }), DispatcherPriority.ContextIdle);
        bool? result = dialog.ShowDialog();
        if (callbackError != null) throw callbackError;
        return result;
    }

    private static void AiApprovalRegression(Window owner)
    {
        Check(RunApprovalDialog(owner, "AiApproveDeny") == false, "AI write approval dialog denies on explicit deny");
        Check(RunApprovalDialog(owner, "AiApproveAllow") == true, "AI write approval dialog allows on explicit allow");
    }

    private static async Task LayoutRegression(string root)
    {
        Environment.SetEnvironmentVariable("NOVA_ROOT", root);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow { Width = 1280, Height = 700, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Task.Delay(700);
            AiApprovalRegression(window);
            var workspace = (ScrollViewer)window.FindName("WorkspaceScroll");
            var host = (ContentControl)window.FindName("ContentHost");
            var showView = typeof(MainWindow).GetMethod("ShowView", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new Exception("ShowView missing");
            foreach (var page in new[] { "Home", "Instances", "Versions", "Account", "Ai", "Mods", "Store", "Worlds", "Backups", "Logs", "Settings" })
            {
                showView.Invoke(window, new object[] { page });
                await Task.Delay(250);
                Check(Math.Abs(host.Width - Math.Max(host.MinWidth, workspace.ViewportWidth)) < 2 &&
                      Math.Abs(host.Height - Math.Max(host.MinHeight, workspace.ViewportHeight)) < 2,
                      page + " fills the available workspace");
                var nested = Descendants<ScrollViewer>(host).FirstOrDefault();
                if (nested != null)
                {
                    // Resizing an inner list/text viewer raises a real routed
                    // ScrollChanged event that bubbles to the outer workspace.
                    nested.Width = 100;
                    nested.Height = 100;
                    nested.UpdateLayout();
                    await Task.Delay(100);
                    Check(Math.Abs(host.Width - Math.Max(host.MinWidth, workspace.ViewportWidth)) < 2 &&
                          Math.Abs(host.Height - Math.Max(host.MinHeight, workspace.ViewportHeight)) < 2,
                          page + " inner scrolling does not shrink workspace");
                }
            }
            window.Width = 900; window.Height = 600;
            showView.Invoke(window, new object[] { "Mods" });
            await Task.Delay(400);
            Check(Math.Abs(host.Width - Math.Max(host.MinWidth, workspace.ViewportWidth)) < 2 &&
                  Math.Abs(host.Height - Math.Max(host.MinHeight, workspace.ViewportHeight)) < 2,
                  "workspace sizes correctly in a smaller window");
            var versionButton = Descendants<Button>(host).First(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "ModsVersionButton");
            var searchBox = Descendants<TextBox>(host).First(x => x.IsVisible);
            Check(!Bounds(searchBox, window).IntersectsWith(Bounds(versionButton, window)), "mod filter and version controls do not overlap");
            var title = Descendants<TextBlock>(host).First(x => x.Text == "Installed Mods");
            var add = Descendants<Button>(host).First(x => Equals(x.Content, "+ ADD JARS"));
            Check(!Bounds(title, window).IntersectsWith(Bounds(add, window)), "mod heading and actions do not overlap");
            window.Width = 1280; window.Height = 700;
            await Task.Delay(400);
            Check(Math.Abs(host.Width - Math.Max(host.MinWidth, workspace.ViewportWidth)) < 2 &&
                  Math.Abs(host.Height - Math.Max(host.MinHeight, workspace.ViewportHeight)) < 2,
                  "workspace expands again after resizing larger");
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("NOVA_ROOT", null);
        }
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
            try { await Run(root); await LayoutRegression(root); await AiChatRegression(root); exit = 0; }
            catch (Exception e) { Console.Error.WriteLine(e); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        return exit;
    }
}
