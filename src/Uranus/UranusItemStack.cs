using System.ComponentModel;

using Uranus.Extensions;
using Uranus.Interfaces;

namespace Uranus
{
	public class UranusItemStack(UranusItem item, decimal count) : IBindable, IStackable, IEquatable<UranusItemStack>
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public UranusItem Item
		{
			get => item;
			set
			{
				if (item != value)
				{
					item = value;
					OnPropertyChanged(nameof(Item));
				}
			}
		}

		public decimal Count
		{
			get => count;
			set
			{
				if (count != value)
				{
					count = value;
					OnPropertyChanged(nameof(Count));
					OnPropertyChanged(nameof(FormatCount));
				}
			}
		}

		public string FormatCount => $"{Count.ToFormatLargeNumber():G}";


		public bool Equals(UranusItemStack? other)
		{
			if (other == null)
			{
				return false;
			}
			return Item == other.Item;
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as UranusItemStack);
		}

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public override string ToString()
		{
			return $"{Count} ｘ {Item.KoreanName}";
		}
	}
}
