using System;
using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class ModRowVm : ObservableObject
{
	private readonly Action<ModRowVm, bool> _toggle;

	private bool _isEnabled;

	private bool _isBusy;

	private bool _isUpdating;

	private bool _isPinned;
	public bool IsPinned => _isPinned;
	public string PinLabel => _isPinned ? "UNPIN" : "PIN";
	public void SetPinned(bool pinned) { _isPinned = pinned; Raise("IsPinned"); Raise("PinLabel"); }

	private readonly string _baseName;

	private string? _title;

	private string _description = "";

	private string? _iconPath;

	private ModUpdateCandidate? _pendingUpdate;

	public string FileName { get; private set; }

	public string DisplayName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_title))
			{
				return _title;
			}
			return _baseName;
		}
	}

	public string Description => _description;

	public string Initials
	{
		get
		{
			string[] array = DisplayName.Split(new char[3] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
			if (array.Length == 0)
			{
				return "?";
			}
			if (array.Length != 1)
			{
				return string.Concat(new ReadOnlySpan<char>(char.ToUpperInvariant(array[0][0])), new ReadOnlySpan<char>(char.ToUpperInvariant(array[1][0])));
			}
			return char.ToUpperInvariant(array[0][0]).ToString();
		}
	}

	public string? IconPath => _iconPath;

	public bool HasIcon => !string.IsNullOrWhiteSpace(_iconPath);

	public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

	public string Meta { get; }

	public string SizeLabel { get; private set; }

	public long SizeBytes { get; private set; }

	public ModUpdateCandidate? PendingUpdate => _pendingUpdate;

	public bool HasUpdate => _pendingUpdate != null;

	public bool IsUpdating
	{
		get => _isUpdating;
		set
		{
			if (Set(ref _isUpdating, value, "IsUpdating"))
			{
				Raise("CanUpdate");
				Raise("UpdateLabel");
			}
		}
	}

	public bool CanUpdate => _pendingUpdate != null && !_isUpdating;

	public string UpdateLabel => _isUpdating
		? "UPDATING…"
		: ((_pendingUpdate == null) ? "" : ("UPDATE → " + _pendingUpdate.LatestVersionNumber));

	public string StatusLabel
	{
		get
		{
			if (!_isEnabled)
			{
				return "disabled";
			}
			return "enabled";
		}
	}

	public bool IsEnabled
	{
		get
		{
			return _isEnabled;
		}
		set
		{
			if (_isEnabled != value && !_isBusy)
			{
				_isBusy = true;
				_toggle(this, value);
				_isBusy = false;
			}
		}
	}

	public ModRowVm(string fileName, string displayName, string meta, bool enabled, long sizeBytes, Action<ModRowVm, bool> toggle)
	{
		FileName = fileName;
		_baseName = displayName;
		Meta = meta;
		_isEnabled = enabled;
		SizeBytes = sizeBytes;
		SizeLabel = FormatSize(sizeBytes);
		_toggle = toggle;
	}

	private static string FormatSize(long sizeBytes)
	{
		if (sizeBytes >= 1048576)
		{
			return ((double)sizeBytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
		}
		return Math.Max(1L, sizeBytes / 1024) + " KB";
	}

	public void SetFile(string fileName, long? newSizeBytes = null)
	{
		FileName = fileName;
		Raise("FileName");
		if (newSizeBytes.HasValue)
		{
			SizeBytes = newSizeBytes.Value;
			SizeLabel = FormatSize(SizeBytes);
			Raise("SizeBytes");
			Raise("SizeLabel");
		}
	}

	public void SetUpdate(ModUpdateCandidate? candidate)
	{
		_pendingUpdate = candidate;
		Raise("PendingUpdate");
		Raise("HasUpdate");
		Raise("CanUpdate");
		Raise("UpdateLabel");
	}

	public void ApplyMeta(string? title, string? description, string? iconPath)
	{
		string? newTitle = string.IsNullOrWhiteSpace(title) ? null : title;
		string newDescription = description ?? "";
		string? newIcon = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath;
		if (_title == newTitle && _description == newDescription && _iconPath == newIcon) return;
		_title = newTitle;
		_description = newDescription;
		_iconPath = newIcon;
		Raise("DisplayName");
		Raise("Initials");
		Raise("Description");
		Raise("HasDescription");
		Raise("IconPath");
		Raise("HasIcon");
	}

	public void SetEnabledSilently(bool enabled)
	{
		_isEnabled = enabled;
		Raise("IsEnabled");
		Raise("StatusLabel");
	}

	public void Refresh()
	{
		Raise("IsEnabled");
		Raise("StatusLabel");
	}

	public bool Matches(string filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return true;
		}
		if (!DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) && !FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
		{
			return Meta.Contains(filter, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
