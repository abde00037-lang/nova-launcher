using Nova.Core;

static class UpdaterSwapperRegression
{
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); }
    static async Task<string> WaitResult(string path)
    {
        for (int i = 0; i < 300; i++)
        {
            if (File.Exists(path))
            {
                try
                {
                    string value = File.ReadAllText(path);
                    if (value.StartsWith("SUCCESS:") || value.StartsWith("ROLLED_BACK:") || value.StartsWith("ROLLBACK_FAILED:")) return value;
                }
                catch (IOException) { }
            }
            await Task.Delay(100);
        }
        throw new Exception("Updater swapper did not report a result");
    }
    public static async Task Run(string root)
    {
        string work = Path.Combine(root, "swapper");
        string stage = Path.Combine(work, "stage"); string target = Path.Combine(root, "test-bin");
        string status = Path.Combine(work, "last-update-result.txt");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(stage, "Nova.exe"), "new");
        File.WriteAllText(Path.Combine(target, "Nova.exe"), "old");
        Directory.CreateDirectory(Path.Combine(target, "data"));
        File.WriteAllText(Path.Combine(target, "data", "sentinel.txt"), "safe");
        string? oldModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        string result;
        try
        {
            // The parent can be PowerShell 7 with a PSModulePath unusable by Windows PowerShell 5.
            Environment.SetEnvironmentVariable("PSModulePath", Path.Combine(root, "missing-ps-modules"));
            LauncherUpdater.LaunchDetachedSwapper(stage, target, 0, false);
            result = await WaitResult(status);
        }
        finally { Environment.SetEnvironmentVariable("PSModulePath", oldModulePath); }
        if (!result.StartsWith("SUCCESS:")) throw new Exception("Swapper result: " + result);
        Check(File.ReadAllText(Path.Combine(target, "Nova.exe")) == "new" && File.ReadAllText(Path.Combine(target, "data", "sentinel.txt")) == "safe", "transactional swapper succeeds and preserves data");
        // An existing file at the parent of a later destination triggers a copy failure after the first file was changed.
        File.Delete(status); Directory.Delete(stage, true); Directory.CreateDirectory(Path.Combine(stage, "nested"));
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new-a"); File.WriteAllText(Path.Combine(stage, "nested", "z.txt"), "new-z");
        File.WriteAllText(Path.Combine(target, "a.txt"), "old-a"); File.WriteAllText(Path.Combine(target, "nested"), "parent-is-file");
        LauncherUpdater.LaunchDetachedSwapper(stage, target, 0, false);
        result = await WaitResult(status);
        Check(result.StartsWith("ROLLED_BACK:") && File.ReadAllText(Path.Combine(target, "a.txt")) == "old-a" && File.ReadAllText(Path.Combine(target, "nested")) == "parent-is-file", "failed update restores previously overwritten files");
        File.Delete(status); Directory.Delete(stage, true); Directory.CreateDirectory(Path.Combine(stage, "data"));
        File.WriteAllText(Path.Combine(stage, "data", "sentinel.txt"), "evil");
        LauncherUpdater.LaunchDetachedSwapper(stage, target, 0, false);
        result = await WaitResult(status);
        Check(result.StartsWith("ROLLED_BACK:") && File.ReadAllText(Path.Combine(target, "data", "sentinel.txt")) == "safe", "update refuses reserved user data paths");
        File.Delete(status); Directory.Delete(stage, true); Directory.CreateDirectory(Path.Combine(stage, "linked"));
        File.WriteAllText(Path.Combine(stage, "linked", "victim.txt"), "bad");
        string outside = Path.Combine(root, "swap-outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "victim.txt"), "safe");
        string junction = Path.Combine(target, "linked");
        var ps = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        ps.ArgumentList.Add("/c"); ps.ArgumentList.Add("mklink"); ps.ArgumentList.Add("/J"); ps.ArgumentList.Add(junction); ps.ArgumentList.Add(outside);
        using (var process = System.Diagnostics.Process.Start(ps) ?? throw new Exception("Cannot start junction test")) { await process.WaitForExitAsync(); Check(process.ExitCode == 0, "created updater junction test"); }
        LauncherUpdater.LaunchDetachedSwapper(stage, target, 0, false);
        result = await WaitResult(status);
        Check(result.StartsWith("ROLLED_BACK:") && File.ReadAllText(Path.Combine(outside, "victim.txt")) == "safe", "updater rejects target junction without touching outside data");
        Directory.Delete(junction, false);
    }
}
