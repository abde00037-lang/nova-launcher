using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class HomeView : UserControl
{


	public HomeView()
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		InitializeComponent();
		((FrameworkElement)this).Loaded += (object _, RoutedEventArgs _) =>
		{
			LoadHeroArt();
		};
	}

	private void LoadHeroArt()
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected Obj, but got Unknown
		if (HeroImage.Source != null)
		{
			return;
		}
		string text = Path.Combine(AppContext.BaseDirectory, "Assets", "hero.jpg");
		if (!File.Exists(text))
		{
			return;
		}
		try
		{
			BitmapImage val = new BitmapImage();
			val.BeginInit();
			val.CacheOption = (BitmapCacheOption)1;
			val.UriSource = new Uri(text, UriKind.Absolute);
			val.EndInit();
			((Freezable)val).Freeze();
			HeroImage.Source = (ImageSource)(object)val;
		}
		catch
		{
		}
	}

	private void ClearConsole_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)this).DataContext is MainViewModel mainViewModel)
		{
			mainViewModel.ConsoleLines.Clear();
		}
	}
}
