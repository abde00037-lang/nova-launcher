using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class StoreView : UserControl
{
	private MainViewModel? _hooked;





	public StoreView()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		InitializeComponent();
		((FrameworkElement)this).Loaded += async (object _, RoutedEventArgs _) =>
		{
			Hook();
			SyncSource();
			await MaybeSearchAsync();
		};
		((FrameworkElement)this).DataContextChanged += (object _, DependencyPropertyChangedEventArgs _) =>
		{
			Hook();
		};
	}

	private void Hook()
	{
		if (((FrameworkElement)this).DataContext is MainViewModel mainViewModel && _hooked != mainViewModel)
		{
			if (_hooked != null)
			{
				_hooked.PropertyChanged -= OnViewModelChanged;
			}
			_hooked = mainViewModel;
			_hooked.PropertyChanged += OnViewModelChanged;
		}
	}

	private async void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		string propertyName = e.PropertyName;
		if ((propertyName == "Selected" || propertyName == "StoreTargetLabel") ? true : false)
		{
			await MaybeSearchAsync();
		}
	}

	private void SyncSource()
	{
		if (((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			((ToggleButton)SegCurse).IsChecked = mainViewModel.StoreSourceIsCurse;
			((ToggleButton)SegModrinth).IsChecked = !mainViewModel.StoreSourceIsCurse;
		}
	}

	private async Task MaybeSearchAsync()
	{
		if (((FrameworkElement)this).DataContext is MainViewModel { Selected: not null, IsStoreBusy: false } mainViewModel && mainViewModel.StoreItems.Count <= 0)
		{
			await mainViewModel.SearchStoreAsync();
		}
	}

	private void Target_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.OpenStoreTargetPickerCommand.Execute(null);
		}
	}

	private async void Sort_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.CycleStoreSort();
		}
		await Task.CompletedTask;
	}

	private async void LoadMore_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			await mainViewModel.LoadMoreStoreAsync();
		}
	}

	private async void Results_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		ScrollViewer val = (ScrollViewer)((sender is ScrollViewer) ? sender : null);
		if (val != null && ((FrameworkElement)this).DataContext is MainViewModel { CanLoadMore: not false } mainViewModel && !(val.ScrollableHeight - val.VerticalOffset > 220.0))
		{
			await mainViewModel.LoadMoreStoreAsync();
		}
	}

	private async void Query_KeyDown(object sender, KeyEventArgs e)
	{
		if ((int)e.Key == 6 && ((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			((RoutedEventArgs)e).Handled = true;
			await mainViewModel.SearchStoreAsync();
		}
	}

	private async void Source_Click(object sender, RoutedEventArgs e)
	{
		RadioButton val = (RadioButton)((sender is RadioButton) ? sender : null);
		if (val != null && ((FrameworkElement)val).Tag is string sourceName && ((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.SourceName = sourceName;
			mainViewModel.PersistStoreSource();
			await mainViewModel.SearchStoreAsync();
		}
	}
}
