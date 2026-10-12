using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MachineFree.Converters
{
	public class VisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is double maxValue)
			{
				return maxValue > 0 ? Visibility.Visible : Visibility.Hidden;
			}
			return Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
