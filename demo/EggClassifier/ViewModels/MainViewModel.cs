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
        private string? _currentUserId;

        public INavigationService Navigation => _navigation;

        [ObservableProperty]
        private bool _isLoggedIn;

        [ObservableProperty]
        private bool _isDetectionSelected;

        [ObservableProperty]
        private bool _isLoginSelected = true;

        [ObservableProperty]
        private bool _isDashboardSelected;

        public MainViewModel(INavigationService navigationService)
        {
            _navigation = navigationService;
        }

        public void OnLoginSuccess(string userId)
        {
            _currentUserId = userId;
            IsLoggedIn = true;
            NavigateToDetection();
        }

        [RelayCommand]
        private void NavigateToDetection()
        {
            if (!IsLoggedIn) return;
            IsDetectionSelected = true;
            IsLoginSelected = false;
            IsDashboardSelected = false;
            _navigation.NavigateTo<DetectionViewModel>();
            if (_navigation.CurrentView is DetectionViewModel detectionVm && !string.IsNullOrEmpty(_currentUserId))
            {
                detectionVm.SetCurrentUser(_currentUserId);
            }
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
            if (!IsLoggedIn) return;
            IsDetectionSelected = false;
            IsLoginSelected = false;
            IsDashboardSelected = true;
            _navigation.NavigateTo<DashboardViewModel>();
        }

        [RelayCommand]
        private void Logout()
        {
            IsLoggedIn = false;
            NavigateToLogin();
        }
    }
}
