using System.Windows;
using MSPASalon.Presentation.ViewModels;

namespace MSPASalon.Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Never taller than the screen; the content scrolls if needed.
        MaxHeight = SystemParameters.WorkArea.Height;
        viewModel.LoggedIn += (_, _) => DialogResult = true;
        Loaded += (_, _) => UsernameBox.Focus();
    }
}
