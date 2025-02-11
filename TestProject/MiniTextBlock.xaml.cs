using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TestProject
{
	/// <summary>
	/// MiniTextBlock.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class MiniTextBlock : UserControl
    {
		public static readonly DependencyProperty RecipeProperty = DependencyProperty.Register(
			"Recipe",
			typeof(TestRecipe),
			typeof(MiniTextBlock));

		public TestRecipe Recipe
		{
			get => (TestRecipe)GetValue(RecipeProperty);
			set => SetValue(RecipeProperty, value);
		}

		public MiniTextBlock()
        {
            InitializeComponent();
        }

		private static void OnRecipeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var control = (MiniTextBlock)d;
			if (e.NewValue is TestRecipe newRecipe)
			{
				control.DataContext = new MiniTextBlockViewModel(newRecipe);
			}
		}

		private void MainGrid_MouseEnter(object sender, MouseEventArgs e)
		{
			MainGrid.Background = Brushes.LightGray;
		}

		private void MainGrid_MouseLeave(object sender, MouseEventArgs e)
		{
			MainGrid.Background = Brushes.Gray;
		}

		private void MainGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			Recipe.Count++;
		}
	}
}
