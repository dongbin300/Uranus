using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;

namespace TestProject
{
	public class MainViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		private void OnPropertyChanged(string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public ObservableCollection<TestRecipe> Recipes { get; } = [];

		private DispatcherTimer _timer;

		public MainViewModel()
		{
			Recipes.Add(new TestRecipe("a1", 1));
			Recipes.Add(new TestRecipe("a2", 2));
			Recipes.Add(new TestRecipe("a3", 3));
			Recipes.Add(new TestRecipe("a4", 4));
			Recipes.Add(new TestRecipe("a5", 5));
			Recipes.Add(new TestRecipe("a6", 6));
			Recipes.Add(new TestRecipe("a7", 7));
			Recipes.Add(new TestRecipe("a8", 8));
			Recipes.Add(new TestRecipe("a9", 9));
			Recipes.Add(new TestRecipe("a10", 10));

			_timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(100)
			};
			_timer.Tick += (sender, e) => UpdateTextBlocks();
			_timer.Start();
		}

		private void UpdateTextBlocks()
		{
			//var recipes2 = Recipes.ToList();
			//foreach (var recipe in recipes2)
			//{
			//	if (recipe.IsUpdated)
			//	{
			//		Recipes.Add(new TestRecipe("asd", 1));
			//		recipe.IsUpdated = false;
			//	}
			//}
		}
	}
}
