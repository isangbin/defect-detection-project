using CommunityToolkit.Mvvm.ComponentModel;
using EggClassifier.Core;

namespace EggClassifier.Features.Dashboard
{
    public partial class DashboardViewModel : ViewModelBase
    {
        [ObservableProperty]
        private int _totalInspections = 0;

        [ObservableProperty]
        private int _normalCount = 0;

        [ObservableProperty]
        private int _defectCount = 0;

        [ObservableProperty]
        private string _logMessage = "DB 연동 후 검사 로그가 표시됩니다.";
    }
}
