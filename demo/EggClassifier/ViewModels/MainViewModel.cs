using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;
using EggClassifier.Features.Dashboard;
using EggClassifier.Features.Detection;
using EggClassifier.Features.Login;

namespace EggClassifier.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly INavigationService _navigation;

        public INavigationService Navigation => _navigation;

        [ObservableProperty]
        private bool _isDetectionSelected = true;

        [ObservableProperty]
        private bool _isLoginSelected;

        [ObservableProperty]
        private bool _isDashboardSelected;

        public MainViewModel(INavigationService navigationService)
        {
            _navigation = navigationService;
        }

        [RelayCommand]
        private void NavigateToDetection()
        {
            IsDetectionSelected = true;
            IsLoginSelected = false;
            IsDashboardSelected = false;
            _navigation.NavigateTo<DetectionViewModel>();
        }

        [RelayCommand]
        private void NavigateToLogin()
        {
            IsDetectionSelected = false;
            IsLoginSelected = true;
            IsDashboardSelected = false;
            _navigation.NavigateTo<LoginViewModel>();
        }

        [RelayCommand]
        private void NavigateToDashboard()
        {
            IsDetectionSelected = false;
            IsLoginSelected = false;
            IsDashboardSelected = true;
            _navigation.NavigateTo<DashboardViewModel>();
        }
    }
}
