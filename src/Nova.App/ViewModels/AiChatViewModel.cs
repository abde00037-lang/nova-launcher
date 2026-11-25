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

/// <summary>Bring-your-own-key chat over Nova.Core with supervised launcher tools; every write tool requires explicit user approval.</summary>
public sealed class AiChatViewModel : INotifyPropertyChanged
{
	private const string SystemPrompt = "You are the Nova Workbench assistant inside Nova Launcher, a portable Minecraft launcher for Windows (Vanilla/Forge/Fabric/NeoForge). Help with modpacks, mods, loaders, crash reports, performance tuning and launcher features. Be concise and practical. Read-only tools list_instances, instance_detail, analyze_crash, mod_conflicts, read_log and check_mod_updates may run automatically. Every write tool (toggle_mod, set_memory_mb, update_mods, install_mod, export_pack and restore_mods) requires the user's explicit approval; never imply a write happened before approval. update_mods and install_mod create a verified mod snapshot before changing files; restore_mods also saves a recovery snapshot before restoring. Mod snapshots cover mod files only, not worlds or configs. Prefer tool output over guessing, and never ask for API keys, passwords or tokens.";

	private readonly LauncherPaths _paths;
	private AiProvider? _selectedProvider;
	private string _inputText = string.Empty;
	private bool _isBusy;

	public ObservableCollection<AiChatMessage> Messages { get; } = new ObservableCollection<AiChatMessage>();
	public ObservableCollection<AiProvider> Providers { get; } = new ObservableCollection<AiProvider>();
	public ICommand SendCommand { get; }
	public ICommand ManageCommand { get; }
	public event EventHandler? RequestManageProviders;

	/// <summary>Asked before any write tool executes. Null denies every write tool.</summary>
	public AiApprovalGate? ApprovalGate { get; set; }
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
			AiAgent agent = new AiAgent(client);
			AiApprovalGate gate = ApprovalGate ?? ((spec, call, ct) => Task.FromResult(false));
			AiAgentResult result = await agent.RunAsync(provider, key, SystemPrompt, text, (call, ct) => AiToolbox.HandleAsync(_paths, call, ct), gate).ConfigureAwait(true);
			foreach (string line in result.Trace) Messages.Add(new AiChatMessage("system", "TOOL " + line));
			Messages.Add(new AiChatMessage("assistant", result.Text, provider.Models.FirstOrDefault()));
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
