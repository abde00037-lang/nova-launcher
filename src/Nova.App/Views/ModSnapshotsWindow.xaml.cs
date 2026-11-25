using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Nova.App.ViewModels;
using Nova.Core;

namespace Nova.App.Views;

public partial class ModSnapshotsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly InstanceVm _instance;
    private readonly ObservableCollection<SnapshotRow> _snapshots = new();

    public ModSnapshotsWindow(MainViewModel viewModel, InstanceVm instance)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _instance = instance;
        HeadingText.Text = "Mod snapshots · " + instance.Name;
        SnapshotsList.ItemsSource = _snapshots;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadSnapshotsAsync();

    private async Task LoadSnapshotsAsync()
    {
        try
        {
            StatusText.Text = "Reading snapshot manifests…";
            var snapshots = await ModSnapshot.ListAsync(System.IO.Path.Combine(_instance.GameDirectory, "mods"));
            _snapshots.Clear();
            foreach (var snapshot in snapshots)
            {
                _snapshots.Add(new SnapshotRow(
                    snapshot.Id,
                    snapshot.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    snapshot.FileCount == 1 ? "1 mod file" : snapshot.FileCount + " mod files",
                    snapshot.FileCount + " files",
                    "size: " + FormatSize(snapshot.TotalBytes) + (snapshot.IsComplete ? string.Empty : " · INCOMPLETE — files missing"),
                    snapshot.IsComplete));
            }
            StatusText.Text = _snapshots.Count == 0 ? "No valid mod snapshots found." : _snapshots.Count + " snapshot(s) · select one to restore";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not read snapshots: " + ex.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadSnapshotsAsync();

    private static string FormatSize(long bytes)
    {
        double size = Math.Max(0, bytes);
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return size.ToString(unit == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSafeState()) return;
        try
        {
            StatusText.Text = "Creating a verified copy of the current mod files…";
            string id = await _viewModel.CreateModSnapshotAsync(_instance);
            await LoadSnapshotsAsync();
            SnapshotsList.SelectedValue = id;
            StatusText.Text = "Snapshot created · " + id;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Snapshot was not created: " + ex.Message;
            MessageBox.Show("Could not create the mod snapshot.\n\n" + ex.Message, "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (SnapshotsList.SelectedItem is not SnapshotRow snapshot)
        {
            MessageBox.Show("Select a mod snapshot first.", "Nova", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!snapshot.IsComplete)
        {
            MessageBox.Show("This snapshot is missing one or more files and cannot be restored. Keep it until you have checked the snapshot folder.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!EnsureSafeState()) return;
        string summary = "Nova verifies every snapshot file using SHA-256 before changing mods and saves a recovery snapshot of the current mod files first. Matching files are copied back; mod files added later are renamed to .disabled and are not deleted.\n\nThis does not restore worlds or configs. Back up the full instance first if you need those.\n\nSnapshot: " + snapshot.Id;
        if (MessageBox.Show(this, summary, "Restore this mod snapshot?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!EnsureSafeState()) return;
        try
        {
            StatusText.Text = "Verifying and restoring selected snapshot…";
            string result = await _viewModel.RestoreModSnapshotAsync(_instance, snapshot.Id);
            await LoadSnapshotsAsync();
            StatusText.Text = result;
            MessageBox.Show(result, "Nova", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Restore was not completed: " + ex.Message;
            MessageBox.Show("Could not restore the mod snapshot.\n\n" + ex.Message, "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SnapshotsList.SelectedItem is not SnapshotRow snapshot)
        {
            MessageBox.Show("Select a mod snapshot first.", "Nova", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EnsureSafeState()) return;
        string warning = "Permanently delete this archived mod snapshot? This cannot be undone. The currently installed mod files will not be changed.\n\nSnapshot: " + snapshot.Id + "\nSize: " + snapshot.SizeLabel;
        if (MessageBox.Show(this, warning, "Delete mod snapshot?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!EnsureSafeState()) return;
        try
        {
            StatusText.Text = "Deleting selected snapshot…";
            await _viewModel.DeleteModSnapshotAsync(_instance, snapshot.Id);
            await LoadSnapshotsAsync();
            StatusText.Text = "Snapshot permanently deleted.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Snapshot was not deleted: " + ex.Message;
            MessageBox.Show("Could not delete the selected snapshot.\n\n" + ex.Message, "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool EnsureSafeState()
    {
        if (_viewModel.Selected != _instance)
        {
            MessageBox.Show("The selected instance changed. Close this window and reopen Mod Snapshots.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_viewModel.IsGameRunning)
        {
            MessageBox.Show("Close Minecraft before changing mod files.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_viewModel.HasActiveModOperations)
        {
            MessageBox.Show("Wait for the current mod operation to finish.", "Nova", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private sealed record SnapshotRow(string Id, string CreatedLabel, string Details, string FileCountLabel, string SizeLabel, bool IsComplete);
}
