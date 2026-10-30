using System.Windows;
using MSPASalon.Desktop.Services;
using MSPASalon.Presentation.ViewModels;

namespace MSPASalon.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        DataContext = shell;
        // Preferred 1280×760; shrinks to the work area and maximizes on small/high-scaling screens.
        WindowPlacement.Apply(this, 1280, 760, maximizeWhenSmall: true);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => _shell.OnAvailableWidthChanged(ActualWidth);
}
