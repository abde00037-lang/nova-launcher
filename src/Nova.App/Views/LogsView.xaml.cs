using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Nova.App.ViewModels;
using Nova.Core;

namespace Nova.App.Views;

public partial class LogsView : UserControl
{
	private MainViewModel? _hooked;




	private MainViewModel? ViewModel => ((FrameworkElement)this).DataContext as MainViewModel;

	public LogsView()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		InitializeComponent();
		((FrameworkElement)this).Loaded += (object _, RoutedEventArgs _) =>
		{
			Hook();
		};
		((FrameworkElement)this).DataContextChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			Hook();
		};
	}

	private void Hook()
	{
		MainViewModel viewModel = ViewModel;
		if (viewModel == null)
		{
			return;
		}
		if (_hooked != viewModel)
		{
			if (_hooked != null)
			{
				_hooked.PropertyChanged -= OnViewModelChanged;
			}
			_hooked = viewModel;
			_hooked.PropertyChanged += OnViewModelChanged;
		}
		Load();
	}

	private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "Selected")
		{
			Load();
		}
	}

	private void Load()
	{
		InstanceConfig instanceConfig = ViewModel?.Selected?.Config;
		if (instanceConfig == null)
		{
			LogPathText.Text = "no instance selected";
			LogBox.Text = "";
			return;
		}
		string text = Path.Combine(instanceConfig.GameDirectory, "logs", "latest.log");
		LogPathText.Text = text;
		if (!File.Exists(text))
		{
			LogBox.Text = "latest.log does not exist yet. Start the game once.";
			return;
		}
		try
		{
			List<string> list = (from line in File.ReadAllLines(text)
				where !line.Contains("Potentially Dangerous alternative prefix", StringComparison.OrdinalIgnoreCase) && !line.Contains("Missing sound for event", StringComparison.OrdinalIgnoreCase) && !line.TrimStart().StartsWith("at ")
				select line).TakeLast(400).ToList();
			LogBox.Text = ((list.Count == 0) ? "latest.log has no readable lines yet." : string.Join(Environment.NewLine, list));
			((TextBoxBase)LogBox).ScrollToEnd();
		}
		catch (Exception ex)
		{
			LogBox.Text = "cannot read the log: " + ex.Message;
		}
	}

	private void Refresh_Click(object sender, RoutedEventArgs e)
	{
		Load();
	}

	private async void Analyse_Click(object sender, RoutedEventArgs e)
	{
		MainViewModel viewModel = ViewModel;
		InstanceConfig instance = viewModel?.Selected?.Config;
		if (instance == null || viewModel == null)
		{
			return;
		}
		((UIElement)AnalyseButton).IsEnabled = false;
		try
		{
			LauncherPaths paths = viewModel.Paths;
			var (crashDiagnosis, text) = await Task.Run(() =>
			{
				CrashDiagnosis crashDiagnosis2 = CrashDiagnostics.AnalyzeLatest(instance);
				string item = ((crashDiagnosis2 == null) ? null : CrashDiagnostics.Write(paths, instance, crashDiagnosis2));
				return (found: crashDiagnosis2, written: item);
			});
			if (crashDiagnosis == null)
			{
				MessageBox.Show("No crash report or log found.", "Nova", (MessageBoxButton)0, (MessageBoxImage)64);
				return;
			}
			string text2 = ((crashDiagnosis.Suspects.Count == 0) ? "no installed mod appears in the stack trace" : string.Join(Environment.NewLine, from s in crashDiagnosis.Suspects.Take(5)
				select $"• {s.ModFile}  ({s.Hits} frames)"));
			MessageBox.Show("Source: " + Path.GetFileName(crashDiagnosis.SourceFile) + Environment.NewLine + Environment.NewLine + "Suspects:" + Environment.NewLine + text2 + Environment.NewLine + Environment.NewLine + "Report written to:" + Environment.NewLine + text, "Crash analysis", (MessageBoxButton)0, (MessageBoxImage)64);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Analysis failed: " + ex.Message, "Nova", (MessageBoxButton)0, (MessageBoxImage)48);
		}
		finally
		{
			((UIElement)AnalyseButton).IsEnabled = true;
		}
	}
}
