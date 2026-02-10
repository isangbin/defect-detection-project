using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using EggClassifier.Core;
using EggClassifier.Features.Dashboard;
using EggClassifier.Features.Detection;
using EggClassifier.Features.Login;
using EggClassifier.Services;
using EggClassifier.ViewModels;

namespace EggClassifier
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();

            // Core
            services.AddSingleton<INavigationService, NavigationService>();

            // Services
            services.AddSingleton<IWebcamService, WebcamService>();
            services.AddSingleton<IDetectorService, DetectorService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<DetectionViewModel>();
            services.AddTransient<LoginViewModel>();
            services.AddTransient<DashboardViewModel>();

            // Views
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
        }

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

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}
