using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessageHook);
		Rect workArea = SystemParameters.WorkArea;
		((FrameworkElement)this).Width = Math.Min(((FrameworkElement)this).Width, Math.Max(MinWidth, workArea.Width - 30.0));
		((FrameworkElement)this).Height = Math.Min(((FrameworkElement)this).Height, Math.Max(MinHeight, workArea.Height - 30.0));
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
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

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
		_viewModel.OnActivePageChanged(text);
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
            if (_isFullScreen) ToggleFullScreen();
			WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
		}
		else if ((int)e.ButtonState == 1 && !_isFullScreen)
		{
			((Window)this).DragMove();
		}
	}

	private void Minimize_Click(object sender, RoutedEventArgs e)
	{
		((Window)this).WindowState = (WindowState)1;
	}

	private bool _isFullScreen;
    private WindowState _preFullScreenState;
    private Rect _preFullScreenBounds;
    private ResizeMode _preFullScreenResizeMode;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x24;
        if (message != WmGetMinMaxInfo || _isFullScreen) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        bounds.MaxSize.X = info.Work.Right - info.Work.Left;
        bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            _isFullScreen = false;
            ResizeMode = _preFullScreenResizeMode;
            Left = _preFullScreenBounds.Left;
            Top = _preFullScreenBounds.Top;
            Width = _preFullScreenBounds.Width;
            Height = _preFullScreenBounds.Height;
            WindowState = _preFullScreenState;
            FullscreenButton.ToolTip = "Full screen (F11)";
            return;
        }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(handle, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
        _preFullScreenState = WindowState;
        _preFullScreenBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _preFullScreenResizeMode = ResizeMode;
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;
        var rect = info.Monitor;
        if (!SetWindowPos(handle, IntPtr.Zero, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0010 | 0x0004 | 0x0020))
        {
            ResizeMode = _preFullScreenResizeMode;
            WindowState = _preFullScreenState;
            return;
        }
        _isFullScreen = true;
        FullscreenButton.ToolTip = "Exit full screen (F11 or Esc)";
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void WorkspaceScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Keep the page bounded so its own scrolling/virtualized lists still work.
        if (e.ViewportWidth <= 0 || e.ViewportHeight <= 0) return;
        double width = Math.Max(ContentHost.MinWidth, e.ViewportWidth);
        double height = Math.Max(ContentHost.MinHeight, e.ViewportHeight);
        if (Math.Abs(ContentHost.Width - width) > 0.5 || double.IsNaN(ContentHost.Width)) ContentHost.Width = width;
        if (Math.Abs(ContentHost.Height - height) > 0.5 || double.IsNaN(ContentHost.Height)) ContentHost.Height = height;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen) ToggleFullScreen();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		((Window)this).Close();
	}
}
