using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Microsoft.Win32;
using Nova.App.ViewModels;
using Nova.Core;

namespace Nova.App.Views;

public partial class InstancesView : UserControl
{
	private MainViewModel? ViewModel => ((FrameworkElement)this).DataContext as MainViewModel;

	public InstancesView()
	{
		InitializeComponent();
	}

	private void DeleteInstance_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel != null)
		{
			ViewModel.DeleteSelectedInstance();
		}
	}

	private async void Duplicate_Click(object sender, RoutedEventArgs e)
	{
		if (ViewModel != null)
		{
			await ViewModel.DuplicateSelectedAsync();
		}
	}

	private async void Import_Click(object sender, RoutedEventArgs e)
	{
		MainViewModel viewModel = ViewModel;
		if (viewModel == null)
		{
			return;
		}
		OpenFileDialog val = new OpenFileDialog
		{
			Title = "Choose a modpack",
			Filter = "Minecraft packs (*.mrpack;*.zip)|*.mrpack;*.zip|All files (*.*)|*.*",
			CheckFileExists = true
		};
		if (((CommonDialog)val).ShowDialog() != true)
		{
			return;
		}
		PackInfo packInfo;
		try
		{
			packInfo = MainViewModel.InspectPack(((FileDialog)val).FileName);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Cannot read this pack: " + ex.Message, "Nova", (MessageBoxButton)0, (MessageBoxImage)48);
			return;
		}
		if (packInfo.Format == PackFormat.Unknown)
		{
			MessageBox.Show("This file is not a modpack:\n\n" + string.Join("\n", packInfo.Notes), "Nova", (MessageBoxButton)0, (MessageBoxImage)48);
			return;
		}
		string text = $"Format:      {packInfo.FormatName}\nName:        {packInfo.Name}\nMinecraft:   {(string.IsNullOrWhiteSpace(packInfo.McVersion) ? "?" : packInfo.McVersion)}\nLoader:      {((packInfo.Loader == null) ? "vanilla" : (packInfo.Loader + " " + packInfo.LoaderVersion))}\nFiles:       {packInfo.ListedFiles} listed, {packInfo.OverrideFiles} override file(s)\nDownload:    {(packInfo.CanDownloadEverything ? "everything can be fetched automatically" : "some files need a manual download")}\n";
		if (packInfo.Notes.Count > 0)
		{
			text = text + "\n" + string.Join("\n", packInfo.Notes);
		}
		if ((int)MessageBox.Show(text + "\n\nNova will create a NEW instance folder for this pack. Your existing pack is never touched.\n\nImport now?", "Import pack", (MessageBoxButton)1, (MessageBoxImage)32) == 1)
		{
			string text2 = await viewModel.ImportPackAsync(((FileDialog)val).FileName);
			if (text2 != null)
			{
				MessageBox.Show("Import failed: " + text2, "Nova", (MessageBoxButton)0, (MessageBoxImage)48);
			}
		}
	}

	private async void Export_Click(object sender, RoutedEventArgs e)
	{
		MainViewModel viewModel = ViewModel;
		if (viewModel?.Selected == null)
		{
			return;
		}
		MessageBoxResult val = MessageBox.Show("Include the saved worlds in the archive?\n\nYes = mods + configs + worlds\nNo = mods + configs only", "Export " + viewModel.Selected.Name, (MessageBoxButton)3, (MessageBoxImage)32);
		if ((int)val == 2)
		{
			return;
		}
		SaveFileDialog val2 = new SaveFileDialog
		{
			Title = "Save pack archive",
			Filter = "Nova pack (*.zip)|*.zip",
			FileName = PackImporter.MakeId(viewModel.Selected.Name) + ".zip",
			OverwritePrompt = true
		};
		if (((CommonDialog)val2).ShowDialog() == true)
		{
			string text = await viewModel.ExportPackAsync(((FileDialog)val2).FileName, (int)val == 6);
			if (text != null)
			{
				MessageBox.Show("Export failed: " + text, "Nova", (MessageBoxButton)0, (MessageBoxImage)48);
			}
		}
	}
}
