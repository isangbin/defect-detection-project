using System.Windows;
using EggClassifier.ViewModels;

namespace EggClassifier
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += (s, e) =>
            {
                viewModel.NavigateToDetectionCommand.Execute(null);
            };
        }
    }
}
