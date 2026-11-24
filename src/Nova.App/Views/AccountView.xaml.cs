using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class AccountView : UserControl
{
	public AccountView()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		InitializeComponent();
		((FrameworkElement)this).Loaded += (object _, RoutedEventArgs _) =>
		{
			(((FrameworkElement)this).DataContext as MainViewModel)?.RefreshAccounts();
		};
	}

	private void CopyCode_Click(object sender, RoutedEventArgs e)
	{
		if (!(((FrameworkElement)this).DataContext is MainViewModel mainViewModel) || string.IsNullOrWhiteSpace(mainViewModel.DeviceCode))
		{
			return;
		}
		try
		{
			Clipboard.SetText(mainViewModel.DeviceCode);
			mainViewModel.MicrosoftStatus = "code copied - paste it on microsoft.com/link";
		}
		catch
		{
		}
	}

	private void OpenLink_Click(object sender, RoutedEventArgs e)
	{
		if (!(((FrameworkElement)this).DataContext is MainViewModel mainViewModel))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(mainViewModel.DeviceLink)
			{
				UseShellExecute = true
			});
		}
		catch
		{
			mainViewModel.MicrosoftStatus = "could not open the browser - go to " + mainViewModel.DeviceLink;
		}
	}

	private void Use_Click(object sender, RoutedEventArgs e)
	{
		Button val = (Button)((sender is Button) ? sender : null);
		if (val != null && ((FrameworkElement)val).Tag is AccountRowVm row && ((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.UseAccount(row);
		}
	}

	private void Remove_Click(object sender, RoutedEventArgs e)
	{
		Button val = (Button)((sender is Button) ? sender : null);
		if (val != null && ((FrameworkElement)val).Tag is AccountRowVm row && ((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.RemoveAccount(row);
		}
	}
}
