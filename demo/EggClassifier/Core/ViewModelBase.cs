using CommunityToolkit.Mvvm.ComponentModel;

namespace EggClassifier.Core
{
    public class ViewModelBase : ObservableObject
    {
        public virtual void OnNavigatedTo() { }
        public virtual void OnNavigatedFrom() { }
    }
}
