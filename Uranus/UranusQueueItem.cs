using System.ComponentModel;

using Uranus.Extensions;
using Uranus.Interfaces;

namespace Uranus
{
	public class UranusQueueItem(UranusRecipe recipe, decimal count, decimal timeElapsed = 0m) : IBindable, IStackable, IEquatable<UranusQueueItem>
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public UranusRecipe Recipe
		{
			get => recipe;
			set
			{
				if (recipe != value)
				{
					recipe = value;
					OnPropertyChanged(nameof(Recipe));
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

		/// <summary>
		/// 생산 중인 레시피의 경과 시간
		/// </summary>
		public decimal TimeElapsed
		{
			get => timeElapsed;
			set
			{
				if (timeElapsed != value)
				{
					timeElapsed = value;
					OnPropertyChanged(nameof(TimeElapsed));
				}
			}
		}

		public override string ToString()
		{
			return $"{Count} ｘ {Recipe.Name}";
		}

		public bool Equals(UranusQueueItem? other)
		{
			if (other == null)
			{
				return false;
			}
			return Recipe == other.Recipe;
		}
	}
}
