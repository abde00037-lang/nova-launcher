using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class StoreItemVm : ObservableObject
{
	private bool _isBusy;

	private bool _isInstalled;

	private string? _iconPath;

	public StoreMod Model { get; }

	public string Name => Model.Name;

	public string Author => Model.Author;

	public string Summary => Model.Summary;

	public string DownloadsLabel => Model.DownloadsLabel;

	public string Initials => Model.Initials;

	public string SourceLabel
	{
		get
		{
			if (Model.Source != StoreSource.Modrinth)
			{
				return "CurseForge";
			}
			return "Modrinth";
		}
	}

	public string? IconPath => _iconPath;

	public bool HasIcon => !string.IsNullOrWhiteSpace(_iconPath);

	public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (Set(ref _isBusy, value, "IsBusy"))
			{
				Raise("InstallLabel");
				Raise("DownloadLabel");
				Raise("CanDownload");
			}
		}
	}

	public bool IsInstalled
	{
		get
		{
			return _isInstalled;
		}
		set
		{
			if (Set(ref _isInstalled, value, "IsInstalled"))
			{
				Raise("InstallLabel");
				Raise("CanDownload");
			}
		}
	}

	public string InstallLabel
	{
		get
		{
			if (!IsBusy)
			{
				if (!IsInstalled)
				{
					return "INSTALL";
				}
				return "INSTALLED";
			}
			return "…";
		}
	}

	public string DownloadLabel
	{
		get
		{
			if (!IsBusy)
			{
				return "DOWNLOAD";
			}
			return "…";
		}
	}

	public bool CanDownload
	{
		get
		{
			if (!IsBusy)
			{
				return !IsInstalled;
			}
			return false;
		}
	}

	public StoreItemVm(StoreMod model)
	{
		Model = model;
	}

	public void SetIcon(string? path)
	{
		_iconPath = (string.IsNullOrWhiteSpace(path) ? null : path);
		Raise("IconPath");
		Raise("HasIcon");
	}
}
