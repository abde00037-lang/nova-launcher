using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Nova.Core;

namespace Nova.App.Views;

public partial class AiProvidersWindow : Window
{
	private readonly LauncherPaths _paths;

	public AiProvidersWindow(LauncherPaths paths)
	{
		_paths = paths;
		InitializeComponent();
		ProtocolCombo.Items.Add(AiProtocol.OpenAiCompatible);
		ProtocolCombo.Items.Add(AiProtocol.Anthropic);
		ProtocolCombo.SelectedIndex = 0;
		Reload();
	}

	private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (PresetCombo?.SelectedItem is not ComboBoxItem item || item.Content is not string preset) return;
		switch (preset)
		{
			case "AgentRouter":
				// Official OpenAI-compatible API base from AgentRouter's documentation.
				ApplyPreset("AgentRouter", AiProtocol.OpenAiCompatible, "https://agentrouter.org/v1", string.Empty);
				break;
			case "OneAIKey":
				// Official OpenAI-compatible API base from OneAIKey's quickstart.
				ApplyPreset("OneAIKey", AiProtocol.OpenAiCompatible, "https://api.oneaiapikey.com/v1", string.Empty);
				break;
		}
	}

	private void ApplyPreset(string name, AiProtocol protocol, string baseUrl, string model)
	{
		NameBox.Text = name;
		ProtocolCombo.SelectedItem = protocol;
		BaseUrlBox.Text = baseUrl;
		ModelsBox.Text = model;
	}

	private void Reload()
	{
		ProviderList.Items.Clear();
		foreach (AiProvider provider in AiStore.LoadProviders(_paths)) ProviderList.Items.Add(provider);
		if (ProviderList.Items.Count > 0) ProviderList.SelectedIndex = 0;
		else ClearForm();
	}

	private void ClearForm()
	{
		NameBox.Text = string.Empty;
		BaseUrlBox.Text = string.Empty;
		ModelsBox.Text = string.Empty;
		KeyBox.Password = string.Empty;
		ProtocolCombo.SelectedIndex = 0;
	}

	private void ProviderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ProviderList.SelectedItem is not AiProvider provider) return;
		NameBox.Text = provider.Name;
		BaseUrlBox.Text = provider.BaseUrl;
		ModelsBox.Text = string.Join(",", provider.Models);
		ProtocolCombo.SelectedItem = provider.Protocol;
		KeyBox.Password = string.Empty;
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			AiProvider provider = new AiProvider
			{
				Name = NameBox.Text.Trim(),
				Protocol = ProtocolCombo.SelectedItem is AiProtocol proto ? proto : AiProtocol.OpenAiCompatible,
				BaseUrl = BaseUrlBox.Text.Trim(),
				Models = ModelsBox.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).Where(m => m.Length > 0).ToList()
			};
			AiStore.SaveProvider(_paths, provider);
			string key = KeyBox.Password.Trim();
			if (key.Length > 0) AiStore.SetKey(_paths, provider.Name, key);
			KeyBox.Password = string.Empty;
			Reload();
			ProviderList.SelectedItem = ProviderList.Items.Cast<object>().FirstOrDefault(i => (i as AiProvider)?.Name == provider.Name);
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "AI provider", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}

	private void Remove_Click(object sender, RoutedEventArgs e)
	{
		if (ProviderList.SelectedItem is not AiProvider provider) return;
		MessageBoxResult answer = MessageBox.Show("Remove provider " + provider.Name + " and its stored key?", "AI provider", MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (answer != MessageBoxResult.Yes) return;
		AiStore.RemoveProvider(_paths, provider.Name);
		Reload();
	}

	private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
