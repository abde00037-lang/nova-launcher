using System;
using System.Collections.Generic;
using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class VersionRowVm : ObservableObject
{
	private bool _isBusy;

	private bool _installed;

	private double _progressFraction;

	private string _progressStage = "";

	private string _progressDetail = "";

	public string Id { get; }

	public string ReleaseLabel { get; }

	public string? ForgeRecommended { get; }

	public string? ForgeLatest { get; }

	public bool HasFabric { get; }

	public string? NeoForgeLatest { get; }

	public bool HasNeoForge => !string.IsNullOrEmpty(NeoForgeLatest);

	public bool HasForge
	{
		get
		{
			if (ForgeRecommended == null)
			{
				return ForgeLatest != null;
			}
			return true;
		}
	}

	public string ForgeLabel
	{
		get
		{
			if (ForgeRecommended == null)
			{
				if (ForgeLatest == null)
				{
					return "no forge build";
				}
				return "Forge " + ForgeLatest;
			}
			return "Forge " + ForgeRecommended;
		}
	}

	public string Tile
	{
		get
		{
			if (Id.Length <= 6)
			{
				return Id;
			}
			return Id.Substring(0, 6);
		}
	}

	public string Subtitle
	{
		get
		{
			List<string> list = new List<string>();
			if (ReleaseLabel.Length > 0)
			{
				list.Add(ReleaseLabel);
			}
			if (ForgeRecommended != null)
			{
				list.Add("Forge " + ForgeRecommended);
			}
			else if (ForgeLatest != null)
			{
				list.Add("Forge " + ForgeLatest);
			}
			if (HasNeoForge)
			{
				list.Add("NeoForge " + NeoForgeLatest);
			}
			if (HasFabric)
			{
				list.Add("Fabric ready");
			}
			if (list.Count == 1)
			{
				list.Add("Vanilla release");
			}
			return string.Join("  ·  ", list);
		}
	}

	public bool Installed
	{
		get
		{
			return _installed;
		}
		set
		{
			if (Set(ref _installed, value, "Installed"))
			{
				Raise("StateLabel");
				Raise("NotInstalled");
			}
		}
	}

	public bool NotInstalled => !_installed;

	public string StateLabel
	{
		get
		{
			if (_isBusy)
			{
				return string.IsNullOrEmpty(_progressStage) ? "INSTALLING" : _progressStage.ToUpperInvariant();
			}
			if (!_installed)
			{
				return "AVAILABLE";
			}
			return "INSTALLED";
		}
	}

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
				Raise("CanInstall");
				Raise("StateLabel");
			}
		}
	}

	public bool CanInstall => !_isBusy;

	public double ProgressFraction
	{
		get
		{
			return _progressFraction;
		}
		set
		{
			if (Set(ref _progressFraction, Math.Clamp(value, 0.0, 1.0), "ProgressFraction"))
			{
				Raise("ProgressPercentLabel");
			}
		}
	}

	public string ProgressPercentLabel => $"{(int)Math.Round(_progressFraction * 100.0)}%";

	public string ProgressStage
	{
		get
		{
			return _progressStage;
		}
		set
		{
			if (Set(ref _progressStage, value, "ProgressStage"))
			{
				Raise("StateLabel");
			}
		}
	}

	public string ProgressDetail
	{
		get
		{
			return _progressDetail;
		}
		set
		{
			Set(ref _progressDetail, value, "ProgressDetail");
		}
	}

	public void UpdateProgress(string stage, double fraction, string detail)
	{
		ProgressStage = stage;
		ProgressFraction = fraction;
		ProgressDetail = detail;
	}

	public VersionRowVm(MinecraftVersion version)
	{
		Id = version.Id;
		ReleaseLabel = version.ReleaseLabel;
		ForgeRecommended = version.ForgeRecommended;
		ForgeLatest = version.ForgeLatest;
		HasFabric = version.HasFabric;
		NeoForgeLatest = version.NeoForgeLatest;
		_installed = version.Installed;
	}

	public bool Matches(string filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return true;
		}
		if (!Id.Contains(filter, StringComparison.OrdinalIgnoreCase) && !ForgeLabel.Contains(filter, StringComparison.OrdinalIgnoreCase))
		{
			return Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
