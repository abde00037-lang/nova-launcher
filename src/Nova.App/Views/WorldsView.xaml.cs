using System.Windows;
using System.Windows.Controls;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class WorldsView : UserControl
{
	public WorldsView()
	{
		InitializeComponent();
	}

	private void OpenSaves_Click(object sender, RoutedEventArgs e)
	{
		(DataContext as MainViewModel)?.OpenWorldsFolder();
	}
}
