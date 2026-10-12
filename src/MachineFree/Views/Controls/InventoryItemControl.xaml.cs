using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Uranus;

namespace MachineFree.Views.Controls
{
	/// <summary>
	/// InventoryItemControl.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class InventoryItemControl : UserControl
	{
		public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
			"Item",
			typeof(UranusInventoryItem),
			typeof(InventoryItemControl),
			new PropertyMetadata(null));

		public UranusInventoryItem Item
		{
			get => (UranusInventoryItem)GetValue(ItemProperty);
			set => SetValue(ItemProperty, value);
		}

		public InventoryItemControl()
		{
			InitializeComponent();
		}

		private void MainGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			MessageBox.Show(Item.ToString());
		}
	}
}
