using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nova.App.ViewModels;
using Nova.Core;

namespace Nova.App.Views;

public partial class SettingsView : UserControl
{
	private MainViewModel? _hooked;

	private bool _building;

	private const char MaskChar = '•';

	private MainViewModel? ViewModel => DataContext as MainViewModel;

	public SettingsView()
	{
		InitializeComponent();
		BuildAccentList();
		Loaded += (_, _) => Hook();
		DataContextChanged += (_, _) => Hook();
	}

	private void BuildAccentList()
	{
		_building = true;
		AccentList.Items.Clear();
		(string Name, string Hex)[] accents = ThemeManager.Accents;
		for (int i = 0; i < accents.Length; i++)
		{
			(string name, string hex) = accents[i];
			StackPanel sp = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			sp.Children.Add(new Border
			{
				Width = 14,
				Height = 14,
				CornerRadius = new CornerRadius(3),
				Background = (Brush)new BrushConverter().ConvertFromString(hex)!,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 8, 0)
			});
			sp.Children.Add(new TextBlock
			{
				Text = name.ToUpperInvariant(),
				FontSize = 10.5,
				FontWeight = FontWeights.SemiBold,
				Foreground = (Brush)Application.Current.Resources["TextBrush"],
				VerticalAlignment = VerticalAlignment.Center
			});
			AccentList.Items.Add(sp);
		}
		_building = false;
	}

	private void Hook()
	{
		MainViewModel? viewModel = ViewModel;
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
		MainViewModel? viewModel = ViewModel;
		if (viewModel == null)
		{
			return;
		}
		_building = true;
		int num = Array.FindIndex(ThemeManager.Accents, a => string.Equals(a.Name, viewModel.AccentName, StringComparison.OrdinalIgnoreCase));
		AccentList.SelectedIndex = ((num >= 0) ? num : 0);
		_building = false;
		CurseKeyBox.Text = Mask(viewModel.CurseForgeKey);
		KeyStatus.Text = viewModel.Settings.CurseForgeKeyUnlockFailed ? "key could not be unlocked for this Windows user; enter it again" :
			viewModel.Settings.CurseForgeKeyNeedsSave ? "key could not be protected and saved; check permissions" :
			(viewModel.CurseForgeConfigured ? "key saved · CurseForge catalog unlocked" : "Modrinth active · CurseForge optional");
		MsClientBox.Text = viewModel.MicrosoftClientId;
		MsClientStatus.Text = (MicrosoftAuth.LooksLikeClientId(viewModel.MicrosoftClientId) ? "client id saved · Microsoft OAuth2 enabled" : "not configured · using local identities");
		InstanceConfig? instanceConfig = viewModel.Selected?.Config;
		if (instanceConfig != null)
		{
			MemoryBox.Text = instanceConfig.MemoryMb.ToString();
			PlayerBox.Text = instanceConfig.PlayerName;
			JavaPathBox.Text = instanceConfig.JavaPath ?? "";
			JvmArgsBox.Text = ((instanceConfig.JvmArgs == null || instanceConfig.JvmArgs.Count == 0) ? "" : string.Join(" ", instanceConfig.JvmArgs));
			BackupBox.IsChecked = instanceConfig.BackupWorldsBeforeLaunch;
			SavedText.Text = "";
		}
	}

	private void Accent_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (!_building && AccentList.SelectedIndex >= 0 && ViewModel != null)
		{
			ViewModel.AccentName = ThemeManager.Accents[AccentList.SelectedIndex].Name;
		}
	}

	private static string Mask(string? key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return "";
		}
		return new string(MaskChar, 12);
	}

	private void SaveKey_Click(object sender, RoutedEventArgs e)
	{
		if (CurseKeyBox.Text.Contains(MaskChar))
		{
			KeyStatus.Text = "key saved · CurseForge catalog unlocked";
			return;
		}
		MainViewModel? viewModel = ViewModel;
		if (viewModel != null)
		{
			viewModel.CurseForgeKey = CurseKeyBox.Text;
			KeyStatus.Text = viewModel.CurseForgeKeySaveFailed ? "could not protect or save key; check permissions" :
				(viewModel.CurseForgeConfigured ? "key saved · CurseForge catalog unlocked" : "Modrinth active · CurseForge optional");
		}
	}

	private void SaveMsClient_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel != null)
		{
			ViewModel.MicrosoftClientId = MsClientBox.Text.Trim();
			MsClientStatus.Text = (MicrosoftAuth.LooksLikeClientId(ViewModel.MicrosoftClientId) ? "client id saved · Microsoft OAuth2 enabled" : "invalid GUID · paste the Application (client) ID from Azure");
		}
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		MainViewModel? viewModel = ViewModel;
		InstanceConfig? instanceConfig = viewModel?.Selected?.Config;
		if (viewModel != null && instanceConfig != null)
		{
			if (int.TryParse(MemoryBox.Text.Trim(), out var result) && result >= 512)
			{
				instanceConfig.MemoryMb = result;
			}
			if (!string.IsNullOrWhiteSpace(PlayerBox.Text))
			{
				instanceConfig.PlayerName = PlayerBox.Text.Trim();
			}
			string javaText = JavaPathBox.Text.Trim();
			instanceConfig.JavaPath = (string.IsNullOrWhiteSpace(javaText) ? null : javaText);
			string jvmRaw = JvmArgsBox.Text.Trim();
			instanceConfig.JvmArgs = (string.IsNullOrWhiteSpace(jvmRaw)
				? new System.Collections.Generic.List<string>()
				: jvmRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
			instanceConfig.BackupWorldsBeforeLaunch = BackupBox.IsChecked == true;
			new InstanceStore(viewModel.Paths).Save(instanceConfig);
			SavedText.Text = "PROFILE CONFIGURATION SAVED";
			viewModel.RefreshAsync();
		}
	}
}
