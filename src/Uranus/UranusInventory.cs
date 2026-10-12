using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Uranus.Maths;

namespace Uranus
{
	public class UranusInventory
	{
		public ObservableCollection<UranusInventoryItem> Items { get; set; } = [];
		SmartRandom random = new();

		public UranusInventory()
		{
			Items.CollectionChanged += (s, e) =>
			{
				if (e.Action == NotifyCollectionChangedAction.Add)
				{
					if (e.NewItems != null)
					{
						foreach (UranusInventoryItem newItem in e.NewItems)
						{
							newItem.IsUpdated = true;
							Task.Delay(150).ContinueWith(_ => newItem.IsUpdated = false, TaskScheduler.FromCurrentSynchronizationContext());
						}
					}
				}
			};
		}

		public void Plus(UranusItem item, decimal value = 1)
		{
			var baseCount = (int)value;
			decimal fractionalPart = value - baseCount;

			var inventoryItem = Items.FirstOrDefault(x => x.Item.Equals(item));

			if (baseCount > 0)
			{
				if (inventoryItem == null)
				{
					Items.Add(new UranusInventoryItem(item, baseCount));
				}
				else
				{
					inventoryItem.Count += baseCount;
				}
			}

			// 소수점이 있을 경우 확률적으로 획득
			if (fractionalPart > 0)
			{
				if (random.NextDecimal() <= fractionalPart)
				{
					if (inventoryItem == null)
					{
						Items.Add(new UranusInventoryItem(item));
					}
					else
					{
						inventoryItem.Count++;
					}
				}
			}
		}

		public void Minus(UranusItem item, decimal value = 1)
		{
			var inventoryItem = Items.FirstOrDefault(x => x.Item.Equals(item));

			if (inventoryItem == null)
			{
				throw new Exception("no Item");
			}
			else
			{
				inventoryItem.Count -= value;
			}

			if (inventoryItem.Count == 0)
			{
				Items.Remove(inventoryItem);
			}
		}

		public bool Has(UranusItem item)
		{
			return Items.Any(x => x.Item.Equals(item));
		}
	}
}
