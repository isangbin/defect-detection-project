using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EggClassifier.Core;

namespace EggClassifier.Features.Login
{
    public partial class LoginViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [RelayCommand]
        private void Login()
        {
            StatusMessage = "로그인 기능은 아직 구현되지 않았습니다.";
        }
    }
}
