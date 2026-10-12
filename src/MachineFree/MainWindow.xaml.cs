using System.Windows;

namespace MachineFree
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();

			// 화면 왼쪽 아래 구석에 띄운다
			var area = SystemParameters.WorkArea;
			Left = area.Left;
			Top = Math.Max(area.Top, area.Bottom - Height);
		}
	}
}
