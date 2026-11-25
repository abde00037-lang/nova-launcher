using System;
using System.Windows;
using System.Windows.Controls;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class WorldsView : UserControl
{
    public WorldsView() { InitializeComponent(); }
    private void OpenSaves_Click(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.OpenWorldsFolder();
    private static void Error(Exception ex) => MessageBox.Show(ex.Message, "Nova worlds", MessageBoxButton.OK, MessageBoxImage.Warning);

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SimpleRow row } || DataContext is not MainViewModel vm) return;
        try { await vm.JoinWorldAsync(row); } catch (Exception ex) { Error(ex); }
    }

    private void OpenWorld_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SimpleRow row } || DataContext is not MainViewModel vm) return;
        try { vm.OpenWorldFolder(row); } catch (Exception ex) { Error(ex); }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SimpleRow row } || DataContext is not MainViewModel vm || vm.Selected == null) return;
        var selected = vm.Selected;
        if (vm.IsRunning) { Error(new InvalidOperationException("Close Minecraft before renaming a world.")); return; }
        string? name = AskName(row.Title);
        if (name == null || name == row.Title) return;
        try { await vm.RenameWorldAsync(selected, row, name); }
        catch (Exception ex) { Error(ex); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SimpleRow row } || DataContext is not MainViewModel vm || vm.Selected == null) return;
        var selected = vm.Selected;
        if (vm.IsRunning) { Error(new InvalidOperationException("Close Minecraft before removing a world.")); return; }
        if (!await vm.ConfirmAsync("Remove world “" + row.Title + "”?", "Close Minecraft first. The world will be moved out of saves into this profile's .nova-world-trash folder, not permanently deleted. Keep a separate backup.", "MOVE TO TRASH")) return;
        try { string path = await vm.DeleteWorldAsync(selected, row); MessageBox.Show("World moved to Nova trash:\n" + path, "Nova worlds"); }
        catch (Exception ex) { Error(ex); }
    }

    private string? AskName(string current)
    {
        var dialog = new Window { Title = "Rename world", Owner = Window.GetWindow(this), Width = 460, Height = 185, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "World name shown in Minecraft (folder name stays the same):", TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { Text = current, Margin = new Thickness(0, 8, 0, 12), MaxLength = 120 };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Width = 85, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Rename", Width = 85, IsDefault = true };
        save.Click += (_, _) => { dialog.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.Content = panel;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
