using System.Windows;
using System.Windows.Controls;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class BackupsView : UserControl
{
	public BackupsView()
	{
		InitializeComponent();
	}

	private async void Restore_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: SimpleRow row } && DataContext is MainViewModel vm)
		{
			await vm.RestoreBackupAsync(row);
		}
	}

	private void OpenFolder_Click(object sender, RoutedEventArgs e)
	{
		(DataContext as MainViewModel)?.OpenBackupsFolder();
	}
}
