using System.Windows;
using System.Windows.Controls;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class VersionsView : UserControl
{
	public VersionsView()
	{
		InitializeComponent();
		Loaded += async (_, _) =>
		{
			if (DataContext is MainViewModel mainViewModel)
			{
				await mainViewModel.LoadVersionsAsync();
			}
			VersionScroll.ScrollToTop();
		};
	}

	private async void Mode_Click(object sender, RoutedEventArgs e)
	{
		if (sender is RadioButton { Tag: string mode } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.SetVersionsModeAsync(mode);
		}
	}

	private async void GetVanilla_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: VersionRowVm row } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.InstallVersionAsync(row, "vanilla");
		}
	}

	private async void GetForge_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: VersionRowVm row } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.InstallVersionAsync(row, "forge");
		}
	}

	private async void GetFabric_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: VersionRowVm row } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.InstallVersionAsync(row, "fabric");
		}
	}

	private async void GetNeoForge_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: VersionRowVm row } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.InstallVersionAsync(row, "neoforge");
		}
	}

	private void Folder_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: VersionRowVm row } && DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.OpenVersionFolder(row);
		}
	}

	private async void Delete_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: VersionRowVm row } && DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.DeleteVersionAsync(row);
		}
	}
}
