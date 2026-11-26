using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Nova.Core;

namespace Nova.App.ViewModels;

// The updater remains a part of the existing view model to preserve all XAML bindings.
public sealed partial class MainViewModel
{
	public async Task CheckLauncherUpdateAsync(bool manual)
	{
		try { LauncherUpdater.CleanupStaleUpdateCache(Path.Combine(Paths.Cache, "launcher-update")); } catch { }
		if (IsLauncherUpdateBusy)
		{
			return;
		}
		IsLauncherUpdateBusy = true;
		if (manual)
		{
			LauncherUpdateStatus = "Checking GitHub Releases (" + UpdateRepoSlug + ")…";
		}
		try
		{
			string repo = UpdateRepoSlug;
			LauncherUpdateInfo? info = await Task.Run(() => LauncherUpdater.CheckForUpdateAsync(repo, includeSameOrOlder: false).GetAwaiter().GetResult());
			_pendingLauncherUpdate = info;
			Raise("HasLauncherUpdate");
			Raise("LauncherUpdateTitle");
			Raise("LauncherUpdateDetail");
			Raise("CanApplyLauncherUpdate");
			Raise("LauncherUpdateActionLabel");
			(ApplyLauncherUpdateCommand as RelayCommand)?.Refresh();

			if (info != null)
			{
				LauncherUpdateStatus = $"Update v{info.LatestVersion} ready ({info.Title}) — worlds & mods stay intact.";
				LauncherUpdateBannerVisible = true;
				AppendConsole("OK", $"updater: new release v{info.LatestVersion} available on {repo}");
			}
			else if (manual)
			{
				LauncherUpdateStatus = $"Up to date · running latest v{LauncherUpdater.CurrentVersion} ({repo})";
				AppendConsole("OK", $"updater: v{LauncherUpdater.CurrentVersion} is already the latest release");
			}
		}
		catch (Exception ex)
		{
			if (manual)
			{
				LauncherUpdateStatus = "Update check failed: " + ex.Message;
				AppendConsole("WARN", "updater check failed: " + ex.Message);
			}
			Log("launcher update check failed: " + ex);
		}
		finally
		{
			IsLauncherUpdateBusy = false;
			FlushConsole();
		}
	}

	public async Task ApplyLauncherUpdateAsync()
	{
		if (_pendingLauncherUpdate == null || IsLauncherUpdateBusy)
		{
			return;
		}
		if (IsRunning)
		{
			LauncherUpdateStatus = "Close the running Minecraft session before updating Nova.";
			return;
		}
		IsLauncherUpdateBusy = true;
		LauncherUpdateInfo update = _pendingLauncherUpdate;
		LauncherUpdateStatus = $"Downloading Nova v{update.LatestVersion}…";
		AppendConsole("INFO", $"updater: downloading Nova v{update.LatestVersion} ({update.AssetName})");
		FlushConsole();
		try
		{
			IProgress<string> prog = new Progress<string>((string line) =>
			{
				RunOnUi(() => LauncherUpdateStatus = line);
				AppendConsole("INFO", "updater: " + line);
			});
			string stagedDir = await Task.Run(() => LauncherUpdater.StageUpdateAsync(Paths, update, prog).GetAwaiter().GetResult());
			string targetBin = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
			string uiLog = Path.Combine(Paths.Root, "logs", "nova-ui.log");
			int currentPid = Environment.ProcessId;

			Log($"updater staged v{update.LatestVersion}; spawning swapper into {targetBin} (pid={currentPid})");
			LauncherUpdater.LaunchDetachedSwapper(stagedDir, targetBin, currentPid, relaunchGui: true, uiLog);
			Application.Current?.Shutdown();
		}
		catch (Exception ex)
		{
			LauncherUpdateStatus = "Update failed: " + ex.Message;
			AppendConsole("ERROR", "updater failed: " + ex.Message);
			Log("launcher update apply failed: " + ex);
			IsLauncherUpdateBusy = false;
			FlushConsole();
		}
	}

	private void OpenLauncherReleaseUrl()
	{
		string url = _pendingLauncherUpdate?.HtmlUrl ?? ("https://github.com/" + UpdateRepoSlug + "/releases");
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

}
