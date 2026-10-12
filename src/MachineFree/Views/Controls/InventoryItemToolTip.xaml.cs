using System.Windows;
using System.Windows.Controls;

namespace MachineFree.Views.Controls
{
	/// <summary>
	/// InventoryItemToolTip.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class InventoryItemToolTip : UserControl
	{
		public InventoryItemToolTip()
		{
			InitializeComponent();
		}

		public string Title
		{
			get { return (string)GetValue(TitleProperty); }
			set { SetValue(TitleProperty, value); }
		}
		public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(InventoryItemToolTip), new PropertyMetadata(string.Empty));

		public string Count
		{
			get { return (string)GetValue(CountProperty); }
			set { SetValue(CountProperty, value); }
		}
		public static readonly DependencyProperty CountProperty = DependencyProperty.Register("Count", typeof(string), typeof(InventoryItemToolTip), new PropertyMetadata(string.Empty));

		public string Description
		{
			get { return (string)GetValue(DescriptionProperty); }
			set { SetValue(DescriptionProperty, value); }
		}
		public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register("Description", typeof(string), typeof(InventoryItemToolTip), new PropertyMetadata(string.Empty));
	}
}
