using System.Windows;
using Nova.Core;

namespace Nova.App.Views;

public partial class AiApprovalWindow : Window
{
	public AiApprovalWindow(AiToolSpec spec, AiToolCall call)
	{
		InitializeComponent();
		ToolText.Text = spec.Name;
		ArgsText.Text = call.ArgumentsJson;
		NoteText.Text = spec.Name switch
		{
			"install_mod" or "update_mods" => "A verified snapshot of the current mod files is created first (mods\\.nova-mod-snapshots). You can roll back with the restore_mods tool or by hand. Denied actions change nothing.",
			"restore_mods" => "Restoring replaces current mod files with the verified snapshot copy. Denied actions change nothing.",
			"export_pack" => "Writes a new pack zip into the launcher packs folder; the profile itself is not modified.",
			_ => "This action changes launcher data. Denied actions change nothing."
		};
	}

	private void Allow_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
	private void Deny_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
