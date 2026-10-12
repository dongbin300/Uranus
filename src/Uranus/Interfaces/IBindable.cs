using System.ComponentModel;

namespace Uranus.Interfaces
{
	public interface IBindable : INotifyPropertyChanged
	{
		void OnPropertyChanged(string propertyName);
	}
}
