using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nova.App;

public sealed class InverseBoolToVisibleConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		bool flag = default;
		int num;
		if (value is bool)
		{
			flag = (bool)value;
			num = 1;
		}
		else
		{
			num = 0;
		}
		return (object)(Visibility)((((uint)num & (flag ? 1u : 0u)) != 0) ? 2 : 0);
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
