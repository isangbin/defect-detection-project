using System.ComponentModel;

namespace EggClassifier.Core
{
    public interface INavigationService : INotifyPropertyChanged
    {
        ViewModelBase? CurrentView { get; }
        void NavigateTo<T>() where T : ViewModelBase;
    }
}
