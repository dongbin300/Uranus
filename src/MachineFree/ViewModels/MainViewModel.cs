using System.ComponentModel;

namespace MachineFree.ViewModels
{
	public class MainViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		protected void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		public MainViewModel()
		{

		}
	}
}
