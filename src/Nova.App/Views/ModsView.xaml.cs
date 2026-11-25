using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Nova.App.ViewModels;
using Nova.Core;

namespace Nova.App.Views;

public partial class ModsView : UserControl
{
	public ModsView()
	{
		InitializeComponent();
	}

	private void Mode_Click(object sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { Tag: string text } && DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.ModsBrowseMode = text == "download";
		}
	}

	private void Target_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.OpenModsTargetPickerCommand.Execute(null);
		}
	}

	private async void Browse_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.SearchModBrowseAsync();
		}
	}

	private async void Download_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: StoreItemVm item } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.DownloadModAsync(item);
		}
	}

	private void State_Click(object sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { Tag: string modsState } && DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.ModsState = modsState;
		}
	}

	private void Sort_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.CycleModsSort();
		}
	}

	private void OpenStore_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.RequestNavigate("Store");
		}
	}

	private void ModSnapshots_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || vm.Selected == null)
		{
			MessageBox.Show("Select an instance first.", "Nova", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		new ModSnapshotsWindow(vm, vm.Selected) { Owner = Window.GetWindow(this) }.ShowDialog();
	}

	private void OpenFolder_Click(object sender, RoutedEventArgs e)
	{
		string? gameDir = (DataContext as MainViewModel)?.Selected?.GameDirectory;
		if (string.IsNullOrWhiteSpace(gameDir))
		{
			return;
		}
		string modsDir = Path.Combine(gameDir, "mods");
		string target = (Directory.Exists(modsDir) ? modsDir : gameDir);
		if (!Directory.Exists(target))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private async void AddJars_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || vm.Selected == null)
		{
			return;
		}
		OpenFileDialog dlg = new OpenFileDialog
		{
			Title = "Select mod .jar files to install into " + vm.Selected.Name,
			Filter = "Minecraft Mod JARs (*.jar;*.disabled)|*.jar;*.disabled|All files (*.*)|*.*",
			Multiselect = true
		};
		if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
		{
			await vm.InstallDroppedJarsAsync(dlg.FileNames);
		}
	}

	private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel vm)
		{
			await vm.CheckModUpdatesAsync();
		}
	}

	private async void UpdateAll_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel vm)
		{
			var instance = vm.Selected;
			if (instance == null || vm.IsGameRunning) { MessageBox.Show("Select an instance and close the game before updating mods.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
			if (!await vm.ConfirmAsync("Update all mods?", "Newer versions can break modpacks or multiplayer server compatibility. A snapshot of mod files and individual backups are created. These do not include your worlds or config; back up the whole instance first.\n\n" + vm.ReviewAllModUpdates(), "UPDATE MODS")) return;
			if (vm.Selected != instance || vm.IsGameRunning) { MessageBox.Show("Instance or game state changed; check updates again.", "Nova"); return; }
			await vm.UpdateAllModsAsync();
		}
	}

	private async void UpdateMod_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: ModRowVm row } && DataContext is MainViewModel vm)
		{
			var instance = vm.Selected;
			var pending = row.PendingUpdate;
			if (instance == null || pending == null || vm.IsGameRunning) { MessageBox.Show("Select an instance and close the game before updating mods.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
			if (!await vm.ConfirmAsync("Update this mod?", "A newer mod version can break modpack or server compatibility. The original is backed up under mods/.nova-update-backups. Back up your instance and worlds first.\n\n" + vm.ReviewModUpdate(pending), "UPDATE MOD")) return;
			if (vm.Selected != instance || vm.IsGameRunning || row.PendingUpdate != pending) { MessageBox.Show("Instance or update changed; check updates again.", "Nova"); return; }
			await vm.UpdateSingleModAsync(row);
		}
	}

	private void PinMod_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: ModRowVm row } || DataContext is not MainViewModel vm) return;
		try { vm.ToggleModPin(row); }
		catch (System.Exception ex) { MessageBox.Show("Could not change pin: " + ex.Message, "Nova", MessageBoxButton.OK, MessageBoxImage.Warning); }
	}

	private async void RestoreMod_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not MainViewModel vm || vm.Selected == null) return;
		if (vm.HasActiveModOperations) { MessageBox.Show("Wait for the current mod update to finish.", "Nova"); return; }
		if (vm.IsGameRunning) { MessageBox.Show("Close the game before restoring mods.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
		var selected = vm.Selected;
		string modsDir = Path.Combine(selected.GameDirectory, "mods");
		string backups = Path.Combine(modsDir, ".nova-update-backups");
		if (!Directory.Exists(backups)) { MessageBox.Show("No mod-update backups were found for this instance.", "Nova"); return; }
		var dlg = new OpenFileDialog { Title = "Choose backup.json inside a mod backup folder", InitialDirectory = backups, Filter = "Mod backup manifest (backup.json)|backup.json", CheckFileExists = true };
		if (dlg.ShowDialog() != true) return;
		if (!await vm.ConfirmAsync("Restore this mod backup?", "The current updated file must still match the recorded update. Nova keeps a copy of it and restores the original mod. Back up your instance and worlds first.", "RESTORE MOD")) return;
		if (vm.Selected != selected || vm.IsGameRunning || vm.HasActiveModOperations) { MessageBox.Show("Instance or game state changed. Please try again.", "Nova"); return; }
		try
		{
			await vm.RestoreModBackupAsync(selected, dlg.FileName);
			MessageBox.Show("Original mod restored. The replacement was kept in the backup folder.", "Nova");
		}
		catch (System.Exception ex) { MessageBox.Show("Restore was not completed: " + ex.Message + "\nThe original backup is still available.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning); }
	}

	private async void DeleteMod_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: ModRowVm row } && DataContext is MainViewModel vm)
		{
			await vm.DeleteModAsync(row);
		}
	}

	private void ModsSurface_DragOver(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
			if (files != null && files.Any((string f) => ModManager.IsModFile(Path.GetFileName(f))))
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
				return;
			}
		}
		e.Effects = DragDropEffects.None;
		e.Handled = true;
	}

	private async void ModsSurface_Drop(object sender, DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			return;
		}
		string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files != null && files.Length > 0 && DataContext is MainViewModel vm)
		{
			await vm.InstallDroppedJarsAsync(files);
		}
	}
}
