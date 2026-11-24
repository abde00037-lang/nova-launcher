using System.Windows;
using System.Windows.Media;

namespace Nova.App.ViewModels;

public sealed class CheckRow
{
	public string Area { get; }

	public string Status { get; }

	public string Message { get; }

	public string Suggestion { get; }

	public Brush StatusBrush { get; }

	public Brush SuggestionBrush { get; }

	public bool HasSuggestion => !string.IsNullOrEmpty(Suggestion);

	public CheckRow(string area, string status, string message, string? suggestion)
	{
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Expected Obj, but got Unknown
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Expected Obj, but got Unknown
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		Area = area;
		Status = status;
		Message = message;
		Suggestion = suggestion ?? "";
		SolidColorBrush val = new SolidColorBrush(status switch
		{
			"OK" => Color.FromRgb((byte)52, (byte)211, (byte)153), 
			"WARN" => Color.FromRgb((byte)251, (byte)191, (byte)36), 
			"FAIL" => Color.FromRgb((byte)248, (byte)113, (byte)113), 
			_ => Color.FromRgb((byte)138, (byte)147, (byte)166), 
		});
		((Freezable)val).Freeze();
		StatusBrush = (Brush)(object)val;
		SuggestionBrush = (Brush)new SolidColorBrush(Color.FromRgb((byte)138, (byte)147, (byte)166));
		((Freezable)(SolidColorBrush)SuggestionBrush).Freeze();
	}
}
