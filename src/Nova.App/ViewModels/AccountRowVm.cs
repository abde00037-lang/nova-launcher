using System;
using Nova.Core;

namespace Nova.App.ViewModels;

public sealed class AccountRowVm : ObservableObject
{
	private bool _isActive;

	public NovaAccount Model { get; }

	public string Name => Model.Name;

	public string KindLabel => Model.KindLabel;

	public string Subtitle => Model.Subtitle;

	public string Uuid => Model.Uuid;

	public bool IsMicrosoft => Model.IsMicrosoft;

	public bool IsActive
	{
		get
		{
			return _isActive;
		}
		set
		{
			if (Set(ref _isActive, value, "IsActive"))
			{
				Raise("ActionLabel");
			}
		}
	}

	public string ActionLabel
	{
		get
		{
			if (!Model.IsMicrosoft)
			{
				return "OFFLINE";
			}
			return "ONLINE";
		}
	}

	public string Initials
	{
		get
		{
			if (Name.Length != 0)
			{
				return Name.Substring(0, Math.Min(2, Name.Length)).ToUpperInvariant();
			}
			return "?";
		}
	}

	public AccountRowVm(NovaAccount account)
	{
		Model = account;
	}
}
