using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Nova.App;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		DispatcherUnhandledException += OnUnhandled;
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex)
			{
				LogException("AppDomain", ex);
			}
		};
	}

	private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogException("Dispatcher", e.Exception);
		MessageBox.Show(e.Exception.Message, "Nova Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
		e.Handled = true;
	}

	private static void LogException(string source, Exception ex)
	{
		try
		{
			string logDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "logs"));
			Directory.CreateDirectory(logDir);
			string logFile = Path.Combine(logDir, "nova-ui.log");
			File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
	}
}
