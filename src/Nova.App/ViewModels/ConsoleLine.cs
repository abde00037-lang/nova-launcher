using System.Windows;
using System.Windows.Media;

namespace Nova.App.ViewModels;

public sealed class ConsoleLine
{
	public string Time { get; }

	public string Level { get; }

	public string Text { get; }

	public Brush LevelBrush { get; }

	public Brush TextBrush { get; }

	public ConsoleLine(string time, string level, string text)
	{
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected Obj, but got Unknown
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected Obj, but got Unknown
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		Time = time;
		Level = level;
		Text = text;
		Color val;
		switch (level)
		{
		case "ERROR":
		case "FATAL":
			val = Color.FromRgb((byte)248, (byte)113, (byte)113);
			break;
		case "WARN":
			val = Color.FromRgb((byte)251, (byte)191, (byte)36);
			break;
		case "OK":
			val = Color.FromRgb((byte)52, (byte)211, (byte)153);
			break;
		default:
			val = Color.FromRgb((byte)138, (byte)147, (byte)166);
			break;
		}
		SolidColorBrush val2 = new SolidColorBrush(val);
		((Freezable)val2).Freeze();
		LevelBrush = (Brush)(object)val2;
		SolidColorBrush val3 = new SolidColorBrush(Color.FromRgb((byte)201, (byte)210, (byte)224));
		((Freezable)val3).Freeze();
		TextBrush = (Brush)(object)val3;
	}
}
