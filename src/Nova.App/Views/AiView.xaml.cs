using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class AiView : UserControl
{
	public AiView()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += (_, _) => ScrollToEnd();
	}

	private AiChatViewModel? _chat;

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_chat != null)
		{
			_chat.RequestManageProviders -= OnRequestManageProviders;
			_chat.Messages.CollectionChanged -= OnMessagesChanged;
			_chat.ApprovalGate = null;
		}
		_chat = (e.NewValue as MainViewModel)?.AiChat;
		if (_chat != null)
		{
			_chat.RequestManageProviders += OnRequestManageProviders;
			_chat.Messages.CollectionChanged += OnMessagesChanged;
			_chat.ApprovalGate = OnApprovalGate;
		}
	}

	private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

	private void ScrollToEnd()
	{
		ChatScroll.UpdateLayout();
		ChatScroll.ScrollToVerticalOffset(ChatScroll.ScrollableHeight);
	}

	private async Task<bool> OnApprovalGate(AiToolSpec spec, AiToolCall call, System.Threading.CancellationToken ct)
	{
		return await Dispatcher.InvokeAsync(() => MessageBox.Show(Window.GetWindow(this), "The AI agent wants to run a changing action:\n\n" + spec.Name + "\n" + call.ArgumentsJson + "\n\nAllow it? A denied action does nothing.", "AI agent approval", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
	}

	private void OnRequestManageProviders(object? sender, System.EventArgs e)
	{
		MainViewModel? vm = DataContext as MainViewModel;
		if (vm == null) return;
		AiProvidersWindow window = new AiProvidersWindow(vm.Paths) { Owner = Window.GetWindow(this) };
		window.ShowDialog();
		_chat?.RefreshProviders();
	}

	private void InputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
		{
			e.Handled = true;
			_chat?.SendCommand.Execute(null);
		}
	}
}
