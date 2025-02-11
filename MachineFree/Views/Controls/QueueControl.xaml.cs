using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

using Uranus;

namespace MachineFree.Views.Controls
{
	/// <summary>
	/// QueueControl.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class QueueControl : UserControl
	{
		public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
			"Item",
			typeof(UranusQueueItem),
			typeof(QueueControl),
			new PropertyMetadata(null, OnItemChanged));

		public UranusQueueItem Item
		{
			get => (UranusQueueItem)GetValue(ItemProperty);
			set => SetValue(ItemProperty, value);
		}

		public QueueControl()
		{
			InitializeComponent();
			DataContext = this;

			Visibility = Visibility.Hidden;
		}

		private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is QueueControl control)
			{
				control.DataContext = e.NewValue;

				if (control.QueueItemControl1 != null && e.NewValue is UranusQueueItem _newItem)
				{
					control.QueueItemControl1.Item = _newItem;
				}

				if (e.OldValue is UranusQueueItem oldItem)
				{
					oldItem.PropertyChanged -= control.OnItemPropertyChanged;
				}

				if (e.NewValue == null)
				{
					control.MfProgressBar1.Maximum = 0;
					control.MfProgressBar1.Value = 0;
					control.Visibility = Visibility.Hidden;
				}
				else if (e.NewValue is UranusQueueItem newItem)
				{
					newItem.PropertyChanged += control.OnItemPropertyChanged;
				}
			}
		}

		private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(Item.TimeElapsed) || e.PropertyName == nameof(Item.Recipe))
			{
				DispatcherService.Invoke(() =>
				{
					MfProgressBar1.Maximum = (double)Item.Recipe.Time;
					MfProgressBar1.Value = (double)Item.TimeElapsed;
					Visibility = Visibility.Visible;
				});
			}
		}
	}
}
