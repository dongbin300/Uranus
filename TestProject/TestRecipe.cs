using System.ComponentModel;

namespace TestProject
{
	public class TestRecipe(string name, int count, bool isUpdated = false) : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		protected virtual void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public string Name
		{
			get => name;
			set
			{
				if (name != value)
				{
					name = value;
					OnPropertyChanged(nameof(Name));
					IsUpdated = true;
				}
			}
		}

		public int Count
		{
			get => count;
			set
			{
				if (count != value)
				{
					count = value;
					OnPropertyChanged(nameof(Count));
					IsUpdated = true;
				}
			}
		}

		public bool IsUpdated
		{
			get => isUpdated;
			set
			{
				if (isUpdated != value)
				{
					isUpdated = value;
					OnPropertyChanged(nameof(IsUpdated));
				}
			}
		}
	}
}
