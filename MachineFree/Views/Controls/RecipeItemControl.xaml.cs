using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Uranus;

namespace MachineFree.Views.Controls
{
	/// <summary>
	/// RecipeItemControl.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class RecipeItemControl : UserControl
	{
		public static readonly DependencyProperty RecipeProperty = DependencyProperty.Register(
			"Recipe",
			typeof(UranusRecipe),
			typeof(RecipeItemControl),
			new PropertyMetadata(null));

		public UranusRecipe Recipe
		{
			get => (UranusRecipe)GetValue(RecipeProperty);
			set => SetValue(RecipeProperty, value);
		}

		public RecipeItemControl()
		{
			InitializeComponent();
		}

		private void MainGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (GameManager.GetProducableCount(Recipe) <= 0)
			{
				return;
			}

			GameManager.Produce(Recipe);
		}
	}
}
