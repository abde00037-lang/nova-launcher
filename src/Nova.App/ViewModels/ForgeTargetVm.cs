namespace Nova.App.ViewModels;

public sealed class ForgeTargetVm
{
	public string VersionId { get; }

	public string Label { get; }

	public string Detail { get; }

	public bool IsSelected { get; }

	public ForgeTargetVm(string versionId, string label, string detail, bool isSelected)
	{
		VersionId = versionId;
		Label = label;
		Detail = detail;
		IsSelected = isSelected;
	}
}
