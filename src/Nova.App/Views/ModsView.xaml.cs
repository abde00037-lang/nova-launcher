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
			await vm.UpdateAllModsAsync();
		}
	}

	private async void UpdateMod_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: ModRowVm row } && DataContext is MainViewModel vm)
		{
			await vm.UpdateSingleModAsync(row);
		}
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
