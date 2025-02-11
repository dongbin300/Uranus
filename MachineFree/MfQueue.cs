using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Uranus;
using Uranus.Interfaces;

namespace MachineFree
{
	public class MfQueue : IBindable
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		public void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private ObservableCollection<UranusQueueItem> items = [];
		public ObservableCollection<UranusQueueItem> Items
		{
			get => items;
			set
			{
				if (items != value)
				{
					items = value;
					OnPropertyChanged(nameof(Items));
				}
			}
		}

		public UranusQueueItem? CurrentItem => Items.FirstOrDefault();
		public ObservableCollection<UranusQueueItem> WaitingItems => new(Items.Skip(1));

		public MfQueue()
		{
			Items.CollectionChanged += (s, e) =>
			{
				OnPropertyChanged(nameof(CurrentItem));
				OnPropertyChanged(nameof(WaitingItems));
			};
		}

		public void Enqueue(UranusQueueItem item)
		{
			if (Items.Count > 0)
			{
				var lastItem = Items.Last();
				if (lastItem.Recipe == item.Recipe)
				{
					lastItem.Count += item.Count;
					return;
				}
			}
			Items.Add(item);
		}

		public void Dequeue()
		{
			if (Items.Count > 0)
			{
				Items.RemoveAt(0);
			}
		}
	}
}
