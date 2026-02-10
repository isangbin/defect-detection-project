using System.Windows.Controls;

namespace EggClassifier.Features.Login
{
    public partial class SignUpView : UserControl
    {
        public SignUpView()
        {
            InitializeComponent();
        }

        private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SignUpViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }

        private void PasswordConfirmBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is SignUpViewModel vm)
            {
                vm.PasswordConfirm = PasswordConfirmBox.Password;
            }
        }
    }
}
