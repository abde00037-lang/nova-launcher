using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Nova Workbench Web Installer")]
[assembly: AssemblyDescription("Installs or updates Nova Workbench to the latest GitHub release")]
[assembly: AssemblyProduct("Nova Workbench")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace NovaSetup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false;
            string customDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--silent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[i], "/S", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                }
                else if (string.Equals(args[i], "--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    customDir = args[++i];
                }
            }

            if (silent)
            {
                string dir = string.IsNullOrWhiteSpace(customDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaWorkbench")
                    : customDir;
                try
                {
                    InstallerEngine.InstallLatestAsync(dir, false, false, delegate(int p, string m) { }).GetAwaiter().GetResult();
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "nova-web-setup-error.log"), ex.ToString()); } catch { }
                    Console.Error.WriteLine("Setup failed: " + ex.Message);
                    return 1;
                }
            }

            Application.Run(new SetupForm(customDir));
            return 0;
        }
    }

    internal sealed class ReleaseMetadata
    {
        public string TagName;
        public string Title;
        public string FullZipUrl;
        public long FullZipSize;
        public string BinZipUrl;
        public long BinZipSize;
    }

    internal static class InstallerEngine
    {
        public const string RepoSlug = "abde00037-lang/nova-launcher";

        public static Task<ReleaseMetadata> FetchLatestReleaseAsync()
        {
            return Task.Run(() =>
            {
                string api = "https://api.github.com/repos/" + RepoSlug + "/releases/latest";
                string sysCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
                string json = null;
                if (File.Exists(sysCurl))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = sysCurl,
                        Arguments = "-f -s -L --retry 3 -H \"Accept: application/vnd.github+json\" -A \"NovaWorkbench-Setup/2.1\" \"" + api + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psi))
                    {
                        json = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(20000);
                        if (p.ExitCode != 0) json = null;
                    }
                }
                if (string.IsNullOrWhiteSpace(json))
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(api);
                    req.UserAgent = "NovaWorkbench-Setup/2.1";
                    req.Accept = "application/vnd.github+json";
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    req.KeepAlive = false;
                    req.Timeout = 20000;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        json = sr.ReadToEnd();
                    }
                }

                ReleaseMetadata meta = new ReleaseMetadata();
                meta.TagName = MatchJsonString(json, "tag_name") ?? "latest";
                meta.Title = MatchJsonString(json, "name") ?? ("Nova Workbench " + meta.TagName);

                // Extract assets block items
                MatchCollection assetMatches = Regex.Matches(
                    json,
                    "\\{[^{}]*?\"name\"\\s*:\\s*\"(?<name>[^\"]+\\.zip)\"[^{}]*?\"size\"\\s*:\\s*(?<size>\\d+)[^{}]*?\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\"",
                    RegexOptions.Singleline);

                foreach (Match m in assetMatches)
                {
                    string name = m.Groups["name"].Value;
                    string url = m.Groups["url"].Value;
                    long size = 0;
                    long.TryParse(m.Groups["size"].Value, out size);

                    if (name.IndexOf("Portable-Full", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        meta.FullZipUrl = url;
                        meta.FullZipSize = size;
                    }
                    else if (name.IndexOf("nova-bin", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        meta.BinZipUrl = url;
                        meta.BinZipSize = size;
                    }
                }

                // Fallback regex if JSON property order differs
                if (string.IsNullOrEmpty(meta.FullZipUrl))
                {
                    Match mFull = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(https://[^\"]*Nova-Portable-Full[^\"]*\\.zip)\"");
                    if (mFull.Success) meta.FullZipUrl = mFull.Groups[1].Value;
                }
                if (string.IsNullOrEmpty(meta.BinZipUrl))
                {
                    Match mBin = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(https://[^\"]*nova-bin[^\"]*\\.zip)\"");
                    if (mBin.Success) meta.BinZipUrl = mBin.Groups[1].Value;
                }

                if (string.IsNullOrEmpty(meta.FullZipUrl) && string.IsNullOrEmpty(meta.BinZipUrl))
                {
                    throw new InvalidOperationException("No downloadable release .zip found on GitHub (" + RepoSlug + ").");
                }

                return meta;
            });
        }

        public static async Task<string> InstallLatestAsync(
            string targetRoot,
            bool createDesktopShortcut,
            bool launchAfter,
            Action<int, string> progress)
        {
            progress(3, "Checking latest release on GitHub…");
            ReleaseMetadata rel = await FetchLatestReleaseAsync();

            Directory.CreateDirectory(targetRoot);
            string targetBin = Path.Combine(targetRoot, "bin");
            Directory.CreateDirectory(targetBin);
            Directory.CreateDirectory(Path.Combine(targetRoot, "instances"));
            Directory.CreateDirectory(Path.Combine(targetRoot, "versions"));
            Directory.CreateDirectory(Path.Combine(targetRoot, "logs"));

            // Check if self-contained runtime (hostfxr.dll / coreclr.dll) already exists in target\bin or --lite flag passed
            bool hasSelfContainedRuntime = File.Exists(Path.Combine(targetBin, "coreclr.dll")) || HasSystemDotNet8Desktop();
            bool forceLite = Environment.CommandLine.IndexOf("--lite", StringComparison.OrdinalIgnoreCase) >= 0;
            bool useFastBinPack = forceLite || (hasSelfContainedRuntime && !string.IsNullOrEmpty(rel.BinZipUrl) && File.Exists(Path.Combine(targetBin, "Nova.exe")));

            string downloadUrl = useFastBinPack ? rel.BinZipUrl : (rel.FullZipUrl ?? rel.BinZipUrl);
            long expectedBytes = useFastBinPack ? rel.BinZipSize : (rel.FullZipSize > 0 ? rel.FullZipSize : rel.BinZipSize);

            string tempWork = Path.Combine(Path.GetTempPath(), "NovaWorkbenchSetup_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempWork);
            string zipFile = Path.Combine(tempWork, "package.zip");
            string extractDir = Path.Combine(tempWork, "extracted");

            try
            {
                // Close running Nova inside target folder if open
                StopNovaInDirectory(targetBin);

                await Task.Run(() =>
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(downloadUrl);
                    req.UserAgent = "NovaWorkbench-Setup/2.1";
                    req.Timeout = 30000;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream net = resp.GetResponseStream())
                    using (FileStream fs = new FileStream(zipFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long total = resp.ContentLength > 0 ? resp.ContentLength : expectedBytes;
                        byte[] buf = new byte[65536];
                        long readTotal = 0;
                        int n;
                        int lastPct = -1;
                        while ((n = net.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            readTotal += n;
                            int pct = total > 0 ? (int)(8 + (readTotal * 74 / total)) : 50;
                            if (pct > 82) pct = 82;
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                double mbDone = readTotal / (1024.0 * 1024.0);
                                double mbTotal = total > 0 ? total / (1024.0 * 1024.0) : mbDone;
                                progress(pct, string.Format("Downloading {0} ({1:0.0} / {2:0.0} MB)…", rel.TagName, mbDone, mbTotal));
                            }
                        }
                    }

                    progress(85, "Extracting Nova Workbench files…");
                    ZipFile.ExtractToDirectory(zipFile, extractDir);

                    // Locate Nova.Core.dll inside extracted tree
                    string[] foundCores = Directory.GetFiles(extractDir, "Nova.Core.dll", SearchOption.AllDirectories);
                    if (foundCores.Length == 0)
                    {
                        throw new InvalidDataException("Downloaded archive did not contain Nova.Core.dll.");
                    }
                    string extractedBin = Path.GetDirectoryName(foundCores[0]);

                    progress(93, "Installing into " + targetBin + " (preserving worlds & mods)…");
                    CopyDirectoryContents(extractedBin, targetBin);

                    // Create Start Nova.bat helper at root
                    string batPath = Path.Combine(targetRoot, "Start Nova.bat");
                    File.WriteAllText(batPath, "@echo off\r\nstart \"\" \"%~dp0bin\\Nova.exe\"\r\n", Encoding.ASCII);
                });

                string novaExe = Path.Combine(targetBin, "Nova.exe");
                if (createDesktopShortcut && File.Exists(novaExe))
                {
                    progress(97, "Creating shortcuts…");
                    TryCreateShortcut(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nova Workbench.lnk"),
                        novaExe,
                        targetBin,
                        "Nova Workbench — Minecraft Launcher");
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nova Workbench.lnk");
                    TryCreateShortcut(startMenu, novaExe, targetBin, "Nova Workbench — Minecraft Launcher");
                }

                progress(100, "Installed Nova Workbench " + rel.TagName + " successfully!");

                if (launchAfter && File.Exists(novaExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = novaExe,
                        WorkingDirectory = targetBin,
                        UseShellExecute = true
                    });
                }

                return novaExe;
            }
            finally
            {
                try { Directory.Delete(tempWork, true); } catch { }
            }
        }

        private static void DownloadResilient(string downloadUrl, string zipFile, long expectedBytes, string tagName, Action<int, string> progress)
        {
            string sysCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(sysCurl))
            {
                if (File.Exists(zipFile)) File.Delete(zipFile);
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = sysCurl,
                    Arguments = "-f -L --retry 4 --retry-delay 2 -A \"NovaWorkbench-Setup/2.1\" -o \"" + zipFile + "\" \"" + downloadUrl + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    int lastPct = -1;
                    while (!p.WaitForExit(200))
                    {
                        try
                        {
                            if (File.Exists(zipFile))
                            {
                                long cur = new FileInfo(zipFile).Length;
                                int pct = expectedBytes > 0 ? (int)(8 + (cur * 74 / expectedBytes)) : 50;
                                if (pct > 82) pct = 82;
                                if (pct != lastPct)
                                {
                                    lastPct = pct;
                                    double mbDone = cur / (1024.0 * 1024.0);
                                    double mbTotal = expectedBytes > 0 ? expectedBytes / (1024.0 * 1024.0) : mbDone;
                                    progress(pct, string.Format("Downloading {0} ({1:0.0} / {2:0.0} MB)…", tagName, mbDone, mbTotal));
                                }
                            }
                        }
                        catch { }
                    }
                    if (p.ExitCode == 0 && File.Exists(zipFile) && new FileInfo(zipFile).Length > 1024)
                    {
                        return;
                    }
                }
            }

            // Fallback to HttpWebRequest with retry
            Exception lastErr = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(downloadUrl);
                    req.UserAgent = "NovaWorkbench-Setup/2.1";
                    req.KeepAlive = false;
                    req.Timeout = 45000;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream net = resp.GetResponseStream())
                    using (FileStream fs = new FileStream(zipFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long total = resp.ContentLength > 0 ? resp.ContentLength : expectedBytes;
                        byte[] buf = new byte[65536];
                        long readTotal = 0;
                        int n;
                        int lastPct = -1;
                        while ((n = net.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            readTotal += n;
                            int pct = total > 0 ? (int)(8 + (readTotal * 74 / total)) : 50;
                            if (pct > 82) pct = 82;
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                double mbDone = readTotal / (1024.0 * 1024.0);
                                double mbTotal = total > 0 ? total / (1024.0 * 1024.0) : mbDone;
                                progress(pct, string.Format("Downloading {0} ({1:0.0} / {2:0.0} MB)…", tagName, mbDone, mbTotal));
                            }
                        }
                    }
                    return;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    System.Threading.Thread.Sleep(800);
                }
            }
            throw lastErr ?? new IOException("Download failed.");
        }

        private static bool HasSystemDotNet8Desktop()
        {
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string sharedDir = Path.Combine(pf, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(sharedDir)) return false;
                foreach (string d in Directory.GetDirectories(sharedDir, "8.*"))
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void StopNovaInDirectory(string targetBin)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("Nova"))
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (path != null && path.StartsWith(targetBin, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void CopyDirectoryContents(string srcDir, string dstDir)
        {
            Directory.CreateDirectory(dstDir);
            foreach (string file in Directory.GetFiles(srcDir))
            {
                string dest = Path.Combine(dstDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (string sub in Directory.GetDirectories(srcDir))
            {
                string destSub = Path.Combine(dstDir, Path.GetFileName(sub));
                CopyDirectoryContents(sub, destSub);
            }
        }

        private static void TryCreateShortcut(string lnkPath, string targetExe, string workDir, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe + ",0" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        private static string MatchJsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _shortcutCheck;
        private readonly CheckBox _launchCheck;
        private readonly Button _installBtn;
        private readonly Button _browseBtn;
        private readonly Label _statusLabel;
        private readonly Label _versionBadge;
        private readonly Panel _progressFill;
        private readonly Panel _progressTrack;
        private bool _installing;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public SetupForm(string initialDir)
        {
            Text = "Nova Workbench Setup";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 375);
            BackColor = Color.FromArgb(17, 16, 14);
            ForeColor = Color.FromArgb(236, 232, 224);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Color cardBg = Color.FromArgb(25, 24, 21);
            Color borderCol = Color.FromArgb(46, 44, 39);
            Color accent = Color.FromArgb(74, 222, 128);
            Color muted = Color.FromArgb(156, 150, 139);

            // Top header drag strip
            Panel topBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(21, 20, 18) };
            topBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };

            Label brandTitle = new Label
            {
                Text = "NOVA WORKBENCH  //  WEB INSTALLER",
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                ForeColor = muted,
                AutoSize = true,
                Location = new Point(22, 14)
            };
            brandTitle.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };

            Button closeBtn = new Button
            {
                Text = "✕",
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 30),
                Location = new Point(516, 7),
                ForeColor = muted,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(190, 55, 55);
            closeBtn.Click += (s, e) => { if (!_installing) Close(); };

            topBar.Controls.Add(brandTitle);
            topBar.Controls.Add(closeBtn);
            Controls.Add(topBar);

            // Hero Title + Latest Version Badge
            Label h1 = new Label
            {
                Text = "Install Nova Workbench",
                Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
                ForeColor = ForeColor,
                AutoSize = true,
                Location = new Point(26, 60)
            };
            Controls.Add(h1);

            _versionBadge = new Label
            {
                Text = "CHECKING GITHUB…",
                Font = new Font("Consolas", 9f, FontStyle.Bold),
                ForeColor = accent,
                BackColor = Color.FromArgb(24, 52, 36),
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 4),
                Location = new Point(392, 66)
            };
            Controls.Add(_versionBadge);

            Label sub = new Label
            {
                Text = "Downloads the newest official build directly from GitHub (Vanilla, Forge, Fabric & NeoForge).\r\nIncludes embedded .NET runtime — existing worlds and mods are never deleted.",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = muted,
                Size = new Size(508, 40),
                Location = new Point(28, 98)
            };
            Controls.Add(sub);

            // Install Folder Label + Box
            Label pathLbl = new Label
            {
                Text = "INSTALL LOCATION",
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = muted,
                AutoSize = true,
                Location = new Point(28, 152)
            };
            Controls.Add(pathLbl);

            string defaultPath = string.IsNullOrWhiteSpace(initialDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaWorkbench")
                : initialDir;

            Panel boxBorder = new Panel
            {
                Location = new Point(28, 173),
                Size = new Size(412, 34),
                BackColor = cardBg
            };
            boxBorder.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, boxBorder.ClientRectangle, borderCol, ButtonBorderStyle.Solid);

            _pathBox = new TextBox
            {
                Text = defaultPath,
                BorderStyle = BorderStyle.None,
                BackColor = cardBg,
                ForeColor = ForeColor,
                Font = new Font("Consolas", 9.5f),
                Location = new Point(10, 8),
                Width = 392
            };
            boxBorder.Controls.Add(_pathBox);
            Controls.Add(boxBorder);

            _browseBtn = new Button
            {
                Text = "BROWSE…",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9f, FontStyle.Bold),
                Location = new Point(448, 173),
                Size = new Size(84, 34),
                BackColor = cardBg,
                ForeColor = ForeColor,
                Cursor = Cursors.Hand
            };
            _browseBtn.FlatAppearance.BorderColor = borderCol;
            _browseBtn.Click += Browse_Click;
            Controls.Add(_browseBtn);

            // Checkboxes
            _shortcutCheck = new CheckBox
            {
                Text = "Create Desktop & Start Menu shortcuts",
                Checked = true,
                AutoSize = true,
                ForeColor = ForeColor,
                Location = new Point(30, 220)
            };
            Controls.Add(_shortcutCheck);

            _launchCheck = new CheckBox
            {
                Text = "Launch Nova Workbench when finished",
                Checked = true,
                AutoSize = true,
                ForeColor = ForeColor,
                Location = new Point(30, 246)
            };
            Controls.Add(_launchCheck);

            // Progress Track & Status
            _statusLabel = new Label
            {
                Text = "Ready to install.",
                Font = new Font("Consolas", 8.8f),
                ForeColor = muted,
                AutoEllipsis = true,
                Size = new Size(504, 18),
                Location = new Point(28, 282)
            };
            Controls.Add(_statusLabel);

            _progressTrack = new Panel
            {
                Location = new Point(28, 304),
                Size = new Size(504, 7),
                BackColor = Color.FromArgb(32, 31, 28)
            };
            _progressFill = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(0, 7),
                BackColor = accent
            };
            _progressTrack.Controls.Add(_progressFill);
            Controls.Add(_progressTrack);

            // Primary Install Button
            _installBtn = new Button
            {
                Text = "INSTALL LATEST VERSION",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                Location = new Point(312, 324),
                Size = new Size(220, 38),
                BackColor = accent,
                ForeColor = Color.FromArgb(14, 18, 14),
                Cursor = Cursors.Hand
            };
            _installBtn.FlatAppearance.BorderSize = 0;
            _installBtn.Click += Install_Click;
            Controls.Add(_installBtn);

            Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, borderCol, ButtonBorderStyle.Solid);
            };

            Shown += async (s, e) => await ProbeReleaseAsync();
        }

        private async Task ProbeReleaseAsync()
        {
            try
            {
                ReleaseMetadata rel = await InstallerEngine.FetchLatestReleaseAsync();
                _versionBadge.Text = "LATEST · " + rel.TagName.ToUpperInvariant();
                _statusLabel.Text = "Latest GitHub release ready: " + rel.Title;
            }
            catch
            {
                _versionBadge.Text = "ONLINE SETUP";
            }
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose where to install Nova Workbench";
                dlg.SelectedPath = _pathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    _pathBox.Text = dlg.SelectedPath;
                }
            }
        }

        private async void Install_Click(object sender, EventArgs e)
        {
            if (_installing) return;
            string target = _pathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, "Please select an installation folder.", "Nova Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _installing = true;
            _installBtn.Enabled = false;
            _browseBtn.Enabled = false;
            _pathBox.ReadOnly = true;
            _shortcutCheck.Enabled = false;
            _launchCheck.Enabled = false;
            _installBtn.Text = "INSTALLING…";

            try
            {
                await InstallerEngine.InstallLatestAsync(
                    target,
                    _shortcutCheck.Checked,
                    _launchCheck.Checked,
                    (pct, msg) =>
                    {
                        if (IsHandleCreated)
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _statusLabel.Text = msg;
                                _progressFill.Width = Math.Max(0, Math.Min(_progressTrack.Width, (_progressTrack.Width * pct) / 100));
                            }));
                        }
                    });

                _installBtn.Text = "INSTALLED ✓";
                await Task.Delay(900);
                Close();
            }
            catch (Exception ex)
            {
                _installing = false;
                _installBtn.Enabled = true;
                _browseBtn.Enabled = true;
                _pathBox.ReadOnly = false;
                _shortcutCheck.Enabled = true;
                _launchCheck.Enabled = true;
                _installBtn.Text = "RETRY INSTALL";
                _statusLabel.Text = "Error: " + ex.Message;
                MessageBox.Show(this, "Installation failed:\r\n" + ex.Message, "Nova Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
