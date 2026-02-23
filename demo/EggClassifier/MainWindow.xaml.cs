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
                viewModel.NavigateToLoginCommand.Execute(null);
            };
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
