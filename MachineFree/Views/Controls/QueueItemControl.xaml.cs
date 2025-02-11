using System.Windows;
using System.Windows.Controls;

using Uranus;

namespace MachineFree.Views.Controls
{
	/// <summary>
	/// QueueItemControl.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class QueueItemControl : UserControl
	{
		public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
			"Item",
			typeof(UranusQueueItem),
			typeof(QueueItemControl),
			new PropertyMetadata(null, OnItemChanged));

		public UranusQueueItem Item
		{
			get => (UranusQueueItem)GetValue(ItemProperty);
			set => SetValue(ItemProperty, value);
		}

		public QueueItemControl()
		{
			InitializeComponent();
		}

		private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is QueueItemControl control)
			{
				control.DataContext = e.NewValue;
			}
		}
	}
}
