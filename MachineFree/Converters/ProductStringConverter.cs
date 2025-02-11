using System;
using System.Globalization;
using System.Windows.Data;

using Uranus;

namespace MachineFree.Converters
{
	public class ProductStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not UranusProduct product)
			{
				return string.Empty;
			}

			string productCountString = product.Count == Math.Floor(product.Count) ? product.Count.ToString("0") : product.Count.ToString("G");

			return $"{productCountString} ｘ {product.Item.KoreanName}";
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
