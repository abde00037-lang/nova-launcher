using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Nova.App;

public static class ThemeManager
{
	public static readonly (string Name, string Hex)[] Accents = new (string, string)[6]
	{
		("Amber", "#E5A93C"),
		("Green", "#52B788"),
		("Rose", "#E06D53"),
		("Blue", "#5B9BD5"),
		("Lime", "#95C653"),
		("Violet", "#9D86E9")
	};

	public static string Current { get; private set; } = "Amber";

	public static void Apply(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "Amber";
		}
		string mapped = (string.Equals(name, "mint", StringComparison.OrdinalIgnoreCase) ? "Amber" : name);
		(string Name, string Hex) tuple = Accents.FirstOrDefault(((string Name, string Hex) a) => string.Equals(a.Name, mapped, StringComparison.OrdinalIgnoreCase));
		if (tuple.Hex == null)
		{
			tuple = Accents[0];
		}
		Current = tuple.Name;
		if (Application.Current != null)
		{
			Color val = (Color)ColorConverter.ConvertFromString(tuple.Hex);
			Color val2 = Color.FromArgb(38, val.R, val.G, val.B);
			ResourceDictionary resources = Application.Current.Resources;
			resources["AccentColor"] = val;
			resources["AccentSoftColor"] = val2;
			resources["AccentBrush"] = new SolidColorBrush(val);
			resources["AccentSoftBrush"] = new SolidColorBrush(val2);
			resources["CyanBrush"] = new SolidColorBrush(val);
			resources["PurpleBrush"] = new SolidColorBrush(val);
		}
	}
}
