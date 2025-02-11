using System;
using System.Windows;
using System.Windows.Threading;

namespace MachineFree
{
	/// <summary>
	/// TODO
	/// 대기열에서 생산취소 기능
	/// 
	/// </summary>
	public partial class MainWindow : Window
	{
		DispatcherTimer queueTimer = new();
		DispatcherTimer Timer1s = new();
		System.Timers.Timer saveTimer = new(1000);

		public MainWindow()
		{
			InitializeComponent();

			GameManager.Init();

			queueTimer.Interval = TimeSpan.FromMilliseconds(100);
			queueTimer.Tick += QueueTimer_Tick;
			queueTimer.Start();

			Timer1s.Interval = TimeSpan.FromSeconds(1);
			Timer1s.Tick += Timer1s_Tick;
			Timer1s.Start();

			saveTimer.Elapsed += SaveTimer_Elapsed;
			saveTimer.Start();
		}

		private void SaveTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
		{
			GameManager.Save();
		}

		private void QueueTimer_Tick(object? sender, EventArgs e)
		{
			GameManager.Process();
		}

		private void Timer1s_Tick(object? sender, EventArgs e)
		{
			GameManager.Process1s();
		}

		private void Window_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
		}
	}
}
