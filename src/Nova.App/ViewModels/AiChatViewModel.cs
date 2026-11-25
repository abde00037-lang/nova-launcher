using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class AiChatMessage
{
	public string Role { get; }
	public string Text { get; }
	public string? Model { get; }

	public AiChatMessage(string role, string text, string? model = null)
	{
		Role = role;
		Text = text;
		Model = model;
	}

	public bool IsUser => Role == "user";
	public bool IsAssistant => Role == "assistant";
	public bool IsSystem => Role == "system";
	public string Label => IsUser ? "YOU" : (IsAssistant ? "AI" + (string.IsNullOrEmpty(Model) ? string.Empty : " · " + Model) : "NOVA");
}

/// <summary>Bring-your-own-key chat over the Nova.Core AI layer. Chat only: no launcher action executes from here yet.</summary>
public sealed class AiChatViewModel : INotifyPropertyChanged
{
	private const string SystemPrompt = "You are the Nova Workbench assistant inside Nova Launcher, a portable Minecraft launcher for Windows (Vanilla/Forge/Fabric/NeoForge). Help with modpacks, mods, loaders, crash reports, performance tuning and launcher features. Be concise and practical. You cannot execute launcher actions yet; when an action is needed, describe the exact UI steps or nova-cli command. Never ask for API keys, passwords or tokens.";

	private readonly LauncherPaths _paths;
	private AiProvider? _selectedProvider;
	private string _inputText = string.Empty;
	private bool _isBusy;

	public ObservableCollection<AiChatMessage> Messages { get; } = new ObservableCollection<AiChatMessage>();
	public ObservableCollection<AiProvider> Providers { get; } = new ObservableCollection<AiProvider>();
	public ICommand SendCommand { get; }
	public ICommand ManageCommand { get; }
	public event EventHandler? RequestManageProviders;
	public event PropertyChangedEventHandler? PropertyChanged;

	public AiChatViewModel(LauncherPaths paths)
	{
		_paths = paths;
		SendCommand = new RelayCommand(() => _ = SendAsync(), () => !IsBusy);
		ManageCommand = new RelayCommand(() => RequestManageProviders?.Invoke(this, EventArgs.Empty));
		RefreshProviders();
		Messages.Add(new AiChatMessage("system", "Ask anything about your modpacks, crashes, loaders or performance. Replies come from your own AI provider; nothing is executed automatically. Add a provider with MANAGE PROVIDERS or `nova-cli ai add`."));
	}

	public AiProvider? SelectedProvider
	{
		get { return _selectedProvider; }
		set { if (_selectedProvider != value) { _selectedProvider = value; OnPropertyChanged(); } }
	}

	public string InputText
	{
		get { return _inputText; }
		set { if (_inputText != value) { _inputText = value; OnPropertyChanged(); } }
	}

	public bool IsBusy
	{
		get { return _isBusy; }
		private set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
	}

	public void RefreshProviders()
	{
		Providers.Clear();
		foreach (AiProvider provider in AiStore.LoadProviders(_paths)) Providers.Add(provider);
		string? current = _selectedProvider?.Name;
		SelectedProvider = Providers.FirstOrDefault(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase)) ?? Providers.FirstOrDefault();
	}

	public async Task SendAsync()
	{
		if (IsBusy) return;
		string text = (InputText ?? string.Empty).Trim();
		if (text.Length == 0) return;
		InputText = string.Empty;
		Messages.Add(new AiChatMessage("user", text));
		AiProvider? provider = SelectedProvider;
		if (provider == null)
		{
			Messages.Add(new AiChatMessage("system", "No AI provider configured yet. Open MANAGE PROVIDERS, or run: nova-cli ai add <name> <openai|anthropic> <baseUrl> <model1,model2>. Your API key is encrypted with Windows DPAPI and never leaves this PC except to the provider you choose."));
			return;
		}
		string? key;
		try
		{
			key = AiStore.GetKey(_paths, provider.Name);
		}
		catch (PlatformNotSupportedException)
		{
			key = null;
		}
		if (string.IsNullOrEmpty(key))
		{
			Messages.Add(new AiChatMessage("system", "No API key stored for provider " + provider.Name + ". Set one with MANAGE PROVIDERS or: nova-cli ai key " + provider.Name));
			return;
		}
		IsBusy = true;
		try
		{
			using AiClient client = new AiClient(120);
			AiReply reply = await client.ChatAsync(provider, key, SystemPrompt, text, 700).ConfigureAwait(true);
			Messages.Add(new AiChatMessage("assistant", string.IsNullOrWhiteSpace(reply.Text) ? "(empty reply)" : reply.Text, reply.Model));
		}
		catch (Exception ex)
		{
			Messages.Add(new AiChatMessage("system", "AI request failed: " + ex.Message));
		}
		finally
		{
			IsBusy = false;
		}
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
