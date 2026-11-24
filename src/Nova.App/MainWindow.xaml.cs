using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Nova.App.ViewModels;
using Nova.App.Views;
using Nova.Core;

namespace Nova.App;

public partial class MainWindow : Window
{
	private readonly MainViewModel _viewModel;

	private string _currentView = "Home";




















	public MainWindow()
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Expected Obj, but got Unknown
		InitializeComponent();
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Width = Math.Min(((FrameworkElement)this).Width, Math.Max(1060.0, workArea.Width - 30.0));
		((FrameworkElement)this).Height = Math.Min(((FrameworkElement)this).Height, Math.Max(620.0, workArea.Height - 30.0));
		((Window)this).Left = workArea.Left + (workArea.Width - ((FrameworkElement)this).Width) / 2.0;
		((Window)this).Top = workArea.Top + (workArea.Height - ((FrameworkElement)this).Height) / 2.0;
		_viewModel = new MainViewModel(ResolveRoot());
		ThemeManager.Apply(_viewModel.AccentName);
		_viewModel.Navigate += (string page) =>
		{
			SelectNav(page);
			ShowView(page);
		};
		((FrameworkElement)this).DataContext = _viewModel;
		RootPathText.Text = _viewModel.Paths.Root;
		string text = ReadStartPage();
		SelectNav(text);
		ShowView(text);
		string instanceId = ReadArg("--instance");
		((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
		{
			await _viewModel.RefreshAsync();
			await Dispatcher.Yield((DispatcherPriority)4);
			if (!string.IsNullOrWhiteSpace(instanceId))
			{
				InstanceVm instanceVm = _viewModel.Instances.FirstOrDefault((InstanceVm i) => i.Id.Equals(instanceId, StringComparison.OrdinalIgnoreCase));
				if (instanceVm != null)
				{
					_viewModel.Selected = instanceVm;
				}
			}
			if (Environment.GetCommandLineArgs().Any((string a) => a.Equals("--autoplay", StringComparison.OrdinalIgnoreCase)))
			{
				_viewModel.LaunchCommand.Execute(null);
			}
		};
		_viewModel.PropertyChanged += (object? _, PropertyChangedEventArgs e) =>
		{
			if (e.PropertyName == "PlayLabel")
			{
				UpdatePlayState();
			}
			if (e.PropertyName == "Selected")
			{
				TryAutoCheck();
			}
		};
	}

	private static LauncherPaths ResolveRoot()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("NOVA_ROOT");
		if (!string.IsNullOrWhiteSpace(environmentVariable) && Directory.Exists(environmentVariable))
		{
			return new LauncherPaths(environmentVariable);
		}
		string path = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
		string text = Directory.GetParent(path)?.FullName;
		if (text != null && Path.GetFileName(path).Equals("bin", StringComparison.OrdinalIgnoreCase))
		{
			return new LauncherPaths(text);
		}
		return LauncherPaths.Default();
	}

	private static string ReadStartPage()
	{
		return ReadArg("--page") ?? "Home";
	}

	private static string? ReadArg(string name)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length - 1; i++)
		{
			if (string.Equals(commandLineArgs[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return commandLineArgs[i + 1];
			}
		}
		return null;
	}

	private void SelectNav(string page)
	{
		((ToggleButton)(page.ToLowerInvariant() switch
		{
			"instances" => (object)NavInstances, 
			"versions" => NavVersions, 
			"account" => NavAccount, 
			"mods" => NavMods, 
			"store" => NavStore, 
			"worlds" => NavWorlds, 
			"backups" => NavBackups, 
			"logs" => NavLogs, 
			"settings" => NavSettings, 
			_ => NavHome, 
		})).IsChecked = true;
	}

	private void ToggleInstanceFlyout_Click(object sender, RoutedEventArgs e)
	{
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		_viewModel.RefreshInstancePicker();
		((UIElement)FlyoutLayer).Visibility = (Visibility)(((int)((UIElement)FlyoutLayer).Visibility == 0) ? 2 : 0);
	}

	private void CloseFlyout_Click(object sender, RoutedEventArgs e)
	{
		((UIElement)FlyoutLayer).Visibility = (Visibility)2;
	}

	private void CloseModsTarget_Click(object sender, RoutedEventArgs e)
	{
		_viewModel.ModsTargetPickerVisible = false;
	}

	private void PickModsTarget_Click(object sender, RoutedEventArgs e)
	{
		Button val = (Button)((sender is Button) ? sender : null);
		if (val != null && ((FrameworkElement)val).Tag is ForgeTargetVm row)
		{
			_viewModel.PickModsTarget(row);
		}
	}

	private void CloseStoreTarget_Click(object sender, RoutedEventArgs e)
	{
		_viewModel.StoreTargetPickerVisible = false;
	}

	private void PickStoreTarget_Click(object sender, RoutedEventArgs e)
	{
		Button val = (Button)((sender is Button) ? sender : null);
		if (val != null && ((FrameworkElement)val).Tag is StoreVersionVm row)
		{
			_viewModel.PickStoreTarget(row);
		}
	}

	private void PickInstance_Click(object sender, RoutedEventArgs e)
	{
		Button val = (Button)((sender is Button) ? sender : null);
		if (val != null && ((FrameworkElement)val).Tag is InstancePickVm row)
		{
			((UIElement)FlyoutLayer).Visibility = (Visibility)2;
			_viewModel.PickInstance(row);
		}
	}

	private void OpenInstancesPage_Click(object sender, RoutedEventArgs e)
	{
		((UIElement)FlyoutLayer).Visibility = (Visibility)2;
		SelectNav("Instances");
		ShowView("Instances");
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Invalid comparison between Unknown and I4
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		if ((int)e.Key == 13 && (int)((UIElement)FlyoutLayer).Visibility == 0)
		{
			((UIElement)FlyoutLayer).Visibility = (Visibility)2;
			((RoutedEventArgs)e).Handled = true;
		}
		else
		{
			base.OnPreviewKeyDown(e);
		}
	}

	private void Nav_Click(object sender, RoutedEventArgs e)
	{
		FrameworkElement val = (FrameworkElement)((sender is FrameworkElement) ? sender : null);
		if (val != null && val.Tag is string view)
		{
			ShowView(view);
		}
	}

	private void ShowView(string view)
	{
		((UIElement)FlyoutLayer).Visibility = (Visibility)2;
		string text = (view ?? "").Trim().ToLowerInvariant();
		UserControl val;
		switch (text)
		{
		case "instances":
		{
			InstancesView instancesView = new InstancesView();
			((FrameworkElement)instancesView).DataContext = _viewModel;
			val = (UserControl)(object)instancesView;
			break;
		}
		case "versions":
		{
			VersionsView versionsView = new VersionsView();
			((FrameworkElement)versionsView).DataContext = _viewModel;
			val = (UserControl)(object)versionsView;
			break;
		}
		case "account":
		{
			AccountView accountView = new AccountView();
			((FrameworkElement)accountView).DataContext = _viewModel;
			val = (UserControl)(object)accountView;
			break;
		}
		case "mods":
		{
			ModsView modsView = new ModsView();
			((FrameworkElement)modsView).DataContext = _viewModel;
			val = (UserControl)(object)modsView;
			break;
		}
		case "store":
		{
			StoreView storeView = new StoreView();
			((FrameworkElement)storeView).DataContext = _viewModel;
			val = (UserControl)(object)storeView;
			break;
		}
		case "worlds":
		{
			WorldsView worldsView = new WorldsView();
			((FrameworkElement)worldsView).DataContext = _viewModel;
			val = (UserControl)(object)worldsView;
			break;
		}
		case "backups":
		{
			BackupsView backupsView = new BackupsView();
			((FrameworkElement)backupsView).DataContext = _viewModel;
			val = (UserControl)(object)backupsView;
			break;
		}
		case "logs":
		{
			LogsView logsView = new LogsView();
			((FrameworkElement)logsView).DataContext = _viewModel;
			val = (UserControl)(object)logsView;
			break;
		}
		case "settings":
		{
			SettingsView settingsView = new SettingsView();
			((FrameworkElement)settingsView).DataContext = _viewModel;
			val = (UserControl)(object)settingsView;
			break;
		}
		default:
		{
			HomeView homeView = new HomeView();
			((FrameworkElement)homeView).DataContext = _viewModel;
			val = (UserControl)(object)homeView;
			break;
		}
		}
		UserControl content = val;
		ContentHost.Content = content;
		_currentView = text;
		TextBlock pageTitle = PageTitle;
		pageTitle.Text = (string.IsNullOrEmpty(text) || text == "home") ? "OVERVIEW" : text.ToUpperInvariant();
		TryAutoCheck();
	}

	private void TryAutoCheck()
	{
		_viewModel.AutoCheckEnabled = string.Equals(_currentView, "instances", StringComparison.OrdinalIgnoreCase);
		_viewModel.RequestAutoCheck();
	}

	private void UpdatePlayState()
	{
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Invalid comparison between Unknown and I4
		if (_viewModel.IsGameRunning && (int)MessageBox.Show("Minecraft is still running. Close it and exit Nova?", "Nova Launcher", (MessageBoxButton)4, (MessageBoxImage)32) != 6)
		{
			e.Cancel = true;
			return;
		}
		_viewModel.ShutdownGame();
		base.OnClosing(e);
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Invalid comparison between Unknown and I4
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Invalid comparison between Unknown and I4
		if (e.ClickCount == 2)
		{
			((Window)this).WindowState = (WindowState)(((int)((Window)this).WindowState != 2) ? 2 : 0);
		}
		else if ((int)e.ButtonState == 1)
		{
			((Window)this).DragMove();
		}
	}

	private void Minimize_Click(object sender, RoutedEventArgs e)
	{
		((Window)this).WindowState = (WindowState)1;
	}

	private void Maximize_Click(object sender, RoutedEventArgs e)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Invalid comparison between Unknown and I4
		((Window)this).WindowState = (WindowState)(((int)((Window)this).WindowState != 2) ? 2 : 0);
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		((Window)this).Close();
	}
}
