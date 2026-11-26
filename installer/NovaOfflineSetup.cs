using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Nova Workbench Full Offline Setup")]
[assembly: AssemblyDescription("Full offline installer for Nova Workbench v2.1.7 (includes embedded .NET 8 runtime)")]
[assembly: AssemblyProduct("Nova Workbench")]
[assembly: AssemblyVersion("2.1.7.0")]
[assembly: AssemblyFileVersion("2.1.7.0")]

namespace NovaOfflineSetup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
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
                    OfflineInstaller.ExtractEmbeddedPayloadAsync(dir, false, false, delegate(int p, string m) { }).GetAwaiter().GetResult();
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "nova-full-setup-error.log"), ex.ToString()); } catch { }
                    Console.Error.WriteLine("Setup failed: " + ex.Message);
                    return 1;
                }
            }

            Application.Run(new FullSetupForm(customDir));
            return 0;
        }
    }

    internal static class OfflineInstaller
    {
        public const string ResourceName = "NovaFullPayload.zip";
        public const string EmbeddedVersion = "v2.1.7";

        public static Task<string> ExtractEmbeddedPayloadAsync(
            string targetRoot,
            bool createShortcuts,
            bool launchAfter,
            Action<int, string> progress)
        {
            return Task.Run(() =>
            {
                progress(4, "Preparing destination folder…");
                Directory.CreateDirectory(targetRoot);
                string targetBin = Path.Combine(targetRoot, "bin");
                Directory.CreateDirectory(targetBin);
                Directory.CreateDirectory(Path.Combine(targetRoot, "instances"));
                Directory.CreateDirectory(Path.Combine(targetRoot, "versions"));
                Directory.CreateDirectory(Path.Combine(targetRoot, "logs"));

                StopNovaInDirectory(targetBin);

                string stage = Path.Combine(Path.GetTempPath(), "nova-full-stage-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream payloadStream = asm.GetManifestResourceStream(ResourceName))
                    {
                        if (payloadStream == null) throw new InvalidOperationException("Embedded payload not found.");
                        using (ZipArchive archive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
                        {
                            progress(10, "Validating and extracting package…");
                            SafeInstallerZip.Extract(archive, stage, delegate(string raw)
                            {
                                string rel = raw.Replace('\\', '/');
                                if (rel.StartsWith("Nova/", StringComparison.OrdinalIgnoreCase))
                                {
                                    rel = rel.Substring(5);
                                    if (!rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && !string.Equals(rel, "Start Nova.bat", StringComparison.OrdinalIgnoreCase))
                                        throw new InvalidDataException("Unexpected file in portable payload: " + raw);
                                }
                                else if (!rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && !string.Equals(rel, "Start Nova.bat", StringComparison.OrdinalIgnoreCase))
                                    rel = "bin/" + rel;
                                return rel;
                            });
                        }
                    }
                    progress(90, "Installing verified package files…");
                    SafeInstallerZip.CopyFiles(stage, targetRoot);
                }
                finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }

                string batPath = SafeInstallerZip.PathWithin(targetRoot, "Start Nova.bat");
                File.WriteAllText(batPath, "@echo off\r\nstart \"\" \"%~dp0bin\\Nova.exe\"\r\n", Encoding.ASCII);

                string novaExe = Path.Combine(targetBin, "Nova.exe");
                if (!File.Exists(novaExe))
                {
                    throw new FileNotFoundException("Extraction completed, but Nova.exe was not found at " + novaExe);
                }

                // v2.1.11 is the final pre-AI edition. Remove only Nova's deprecated AI settings and protected keys.
                SafeInstallerZip.RemoveDeprecatedAiData(targetRoot);

                if (createShortcuts)
                {
                    progress(96, "Creating Desktop & Start Menu shortcuts…");
                    TryCreateShortcut(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nova Workbench.lnk"),
                        novaExe,
                        targetBin,
                        "Nova Workbench — Portable Minecraft Launcher");
                    TryCreateShortcut(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nova Workbench.lnk"),
                        novaExe,
                        targetBin,
                        "Nova Workbench — Portable Minecraft Launcher");
                }

                progress(100, "Installed Nova Workbench " + EmbeddedVersion + " (Full Offline Edition)!");

                if (launchAfter)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = novaExe,
                        WorkingDirectory = targetBin,
                        UseShellExecute = true
                    });
                }

                return novaExe;
            });
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
    }

    internal sealed class FullSetupForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _shortcutCheck;
        private readonly CheckBox _launchCheck;
        private readonly Button _installBtn;
        private readonly Button _browseBtn;
        private readonly Label _statusLabel;
        private readonly Panel _progressFill;
        private readonly Panel _progressTrack;
        private bool _installing;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public FullSetupForm(string initialDir)
        {
            Text = "Nova Workbench Setup (Full Offline)";
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

            Panel topBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(21, 20, 18) };
            topBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };

            Label brandTitle = new Label
            {
                Text = "NOVA WORKBENCH  //  FULL OFFLINE INSTALLER",
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

            Label h1 = new Label
            {
                Text = "Nova Workbench " + OfflineInstaller.EmbeddedVersion,
                Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
                ForeColor = ForeColor,
                AutoSize = true,
                Location = new Point(26, 60)
            };
            Controls.Add(h1);

            Label badge = new Label
            {
                Text = "FULL OFFLINE · .NET 8 INCLUDED",
                Font = new Font("Consolas", 8.8f, FontStyle.Bold),
                ForeColor = accent,
                BackColor = Color.FromArgb(24, 52, 36),
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 4),
                Location = new Point(332, 66)
            };
            Controls.Add(badge);

            Label sub = new Label
            {
                Text = "Complete standalone package with Nova Workbench, CLI, and embedded .NET 8 runtime.\r\nInstalls in seconds with zero internet required — and auto-updates via GitHub afterwards!",
                Font = new Font("Segoe UI", 9.2f),
                ForeColor = muted,
                Size = new Size(508, 40),
                Location = new Point(28, 98)
            };
            Controls.Add(sub);

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
                Text = "Launch Nova Workbench immediately",
                Checked = true,
                AutoSize = true,
                ForeColor = ForeColor,
                Location = new Point(30, 246)
            };
            Controls.Add(_launchCheck);

            _statusLabel = new Label
            {
                Text = "Ready to extract full standalone package.",
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

            _installBtn = new Button
            {
                Text = "INSTALL NOVA WORKBENCH",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                Location = new Point(302, 324),
                Size = new Size(230, 38),
                BackColor = accent,
                ForeColor = Color.FromArgb(14, 18, 14),
                Cursor = Cursors.Hand
            };
            _installBtn.FlatAppearance.BorderSize = 0;
            _installBtn.Click += Install_Click;
            Controls.Add(_installBtn);

            Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, ClientRectangle, borderCol, ButtonBorderStyle.Solid);
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
            _installBtn.Text = "EXTRACTING…";

            try
            {
                await OfflineInstaller.ExtractEmbeddedPayloadAsync(
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
                await Task.Delay(800);
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
