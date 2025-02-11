using System.Windows;
using System.Windows.Controls;

namespace MachineFree.Views.Controls
{
	/// <summary>
	/// MfProgressBar.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class MfProgressBar : UserControl
	{
		public MfProgressBar()
		{
			InitializeComponent();
		}

		public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(MfProgressBar), new PropertyMetadata(0.0, OnProgressChanged));
		public double Minimum
		{
			get => (double)GetValue(MinimumProperty);
			set => SetValue(MinimumProperty, value);
		}

		public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(MfProgressBar), new PropertyMetadata(100.0, OnProgressChanged));
		public double Maximum
		{
			get => (double)GetValue(MaximumProperty);
			set => SetValue(MaximumProperty, value);
		}

		public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(MfProgressBar), new PropertyMetadata(0.0, OnProgressChanged));
		public double Value
		{
			get => (double)GetValue(ValueProperty);
			set => SetValue(ValueProperty, value);
		}

		public static readonly DependencyProperty ProgressWidthProperty = DependencyProperty.Register(nameof(ProgressWidth), typeof(double), typeof(MfProgressBar), new PropertyMetadata(0.0));
		public double ProgressWidth
		{
			get => (double)GetValue(ProgressWidthProperty);
			set => SetValue(ProgressWidthProperty, value);
		}

		private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is MfProgressBar progressBar)
			{
				progressBar.UpdateProgress();
			}
		}

		private void UpdateProgress()
		{
			progressBar.Minimum = Minimum;
			progressBar.Maximum = Maximum;
			progressBar.Value = Value;

			//textBlock.Text = $"{Value:F1} / {Maximum:F1}";
		}
	}
}
