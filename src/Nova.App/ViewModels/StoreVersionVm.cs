namespace Nova.App.ViewModels;

public sealed class StoreVersionVm
{
	public string Version { get; }

	public string Label { get; }

	public string Detail { get; }

	public bool IsSelected { get; }

	public bool IsAuto => string.IsNullOrEmpty(Version);

	public StoreVersionVm(string version, string label, string detail, bool isSelected)
	{
		Version = version;
		Label = label;
		Detail = detail;
		IsSelected = isSelected;
	}
}
