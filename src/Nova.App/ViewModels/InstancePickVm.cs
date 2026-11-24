using System;

namespace Nova.App.ViewModels;

public sealed class InstancePickVm
{
	public InstanceVm Model { get; }

	public string Name => Model.Name;

	public string Detail => $"{Model.VersionId}  ·  {Model.ModCount} mods";

	public bool IsActive { get; }

	public bool IsSelected => IsActive;

	public string Initials
	{
		get
		{
			string text = Model.Name.Trim();
			if (text.Length == 0)
			{
				return "?";
			}
			string[] array = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (array.Length >= 2)
			{
				return string.Concat(new ReadOnlySpan<char>(array[0][0]), new ReadOnlySpan<char>(array[1][0])).ToUpperInvariant();
			}
			return text.Substring(0, Math.Min(2, text.Length)).ToUpperInvariant();
		}
	}

	public InstancePickVm(InstanceVm model, bool isActive)
	{
		Model = model;
		IsActive = isActive;
	}
}
