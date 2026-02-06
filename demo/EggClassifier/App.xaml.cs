using System.Windows;

namespace EggClassifier
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 전역 예외 처리
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    $"예기치 않은 오류가 발생했습니다:\n\n{args.Exception.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                args.Handled = true;
            };
        }
    }
}
