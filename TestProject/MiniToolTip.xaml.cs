using System.Windows;
using System.Windows.Controls;

namespace TestProject
{
	/// <summary>
	/// MiniToolTip.xaml에 대한 상호 작용 논리
	/// </summary>
	public partial class MiniToolTip : UserControl
	{
		public MiniToolTip()
		{
			InitializeComponent();
		}

		public string Title
		{
			get { return (string)GetValue(TitleProperty); }
			set { SetValue(TitleProperty, value); }
		}
		public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(MiniToolTip), new PropertyMetadata(string.Empty));

		public string Description1
		{
			get { return (string)GetValue(Description1Property); }
			set { SetValue(Description1Property, value); }
		}
		public static readonly DependencyProperty Description1Property = DependencyProperty.Register("Description1", typeof(string), typeof(MiniToolTip), new PropertyMetadata("AVXCX"));

		public string Description2
		{
			get { return (string)GetValue(Description2Property); }
			set { SetValue(Description2Property, value); }
		}
		public static readonly DependencyProperty Description2Property = DependencyProperty.Register("Description2", typeof(string), typeof(MiniToolTip), new PropertyMetadata(string.Empty));
	}
}
