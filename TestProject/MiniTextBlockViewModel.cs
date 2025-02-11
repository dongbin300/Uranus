using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;

namespace TestProject
{
	public class MiniTextBlockViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		private void OnPropertyChanged(string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private TestRecipe _recipe;
		public TestRecipe Recipe
		{
			get => _recipe;
			set
			{
				if (_recipe != value)
				{
					_recipe = value;
					OnPropertyChanged(nameof(Recipe));
				}
			}
		}

		public MiniTextBlockViewModel(TestRecipe recipe)
		{
			Recipe = recipe;
		}
	}
}
