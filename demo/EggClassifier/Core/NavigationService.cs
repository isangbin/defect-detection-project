using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace EggClassifier.Core
{
    public class NavigationService : ObservableObject, INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase? _currentView;

        public ViewModelBase? CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void NavigateTo<T>() where T : ViewModelBase
        {
            var oldView = CurrentView;
            oldView?.OnNavigatedFrom();

            var newView = (T)_serviceProvider.GetService(typeof(T))!;
            newView.OnNavigatedTo();

            CurrentView = newView;
        }
    }
}
