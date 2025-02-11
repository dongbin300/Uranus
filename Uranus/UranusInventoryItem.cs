using Newtonsoft.Json;

using Uranus.Extensions;

namespace Uranus
{
	public class UranusInventoryItem(UranusItem item, decimal initCount = 1m) : UranusItemStack(item, initCount)
	{
		private decimal count = initCount;
		public new decimal Count
		{
			get => count;
			set
			{
				if (count != value)
				{
					count = value;
					OnPropertyChanged(nameof(Count));
					OnPropertyChanged(nameof(FormatCount));
					UpdateItem();
				}
			}
		}

		public new string FormatCount => $"{Count.ToFormatLargeNumber():G}";

		private bool isUpdated = false;
		[JsonIgnore]
		public bool IsUpdated
		{
			get => isUpdated;
			set
			{
				isUpdated = value;
				OnPropertyChanged(nameof(IsUpdated));
			}
		}

		public string TitleString => $"{Item.KoreanName}";
		public string CountString => $"수량: {Count:G}";
		public string DescriptionString => $"{Item.KoreanComment}";

		private async void UpdateItem()
		{
			IsUpdated = true;
			await Task.Delay(150);
			IsUpdated = false;
		}
	}
}
