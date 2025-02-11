using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;

using Uranus;

namespace MachineFree.Converters
{
	public class MaterialStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not UranusMaterial material)
			{
				return string.Empty;
			}

			var ownedCount = GameManager.Inventory.Items.FirstOrDefault(item => item.Item.Equals(material.Item))?.Count ?? 0;

			if (parameter is string param && param == "Foreground")
			{
				return new SolidColorBrush((Color)ColorConverter.ConvertFromString(ownedCount >= material.Count ? "#EEEEEE" : "Red"));
			}

			string ownedCountString = ownedCount == Math.Floor(ownedCount) ? ownedCount.ToString("0") : ownedCount.ToString("G");
			string requiredCountString = material.Count == Math.Floor(material.Count) ? material.Count.ToString("0") : material.Count.ToString("G");

			return $"{ownedCountString} / {requiredCountString} ｘ {material.Item.KoreanName}";
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
