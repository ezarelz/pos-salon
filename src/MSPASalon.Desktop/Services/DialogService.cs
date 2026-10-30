using System.Windows;
using Microsoft.Win32;
using MSPASalon.Desktop.Views;
using MSPASalon.Presentation.Services;
using MSPASalon.Presentation.ViewModels;

namespace MSPASalon.Desktop.Services;

public sealed class DialogService : IDialogService
{
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public void Info(string message, string title = "POS Salon") => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "POS Salon") => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title = "Konfirmasi") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
    }

    public bool ShowEditor(EditorViewModel editor)
    {
        var window = new EditorWindow(editor);
        var owner = Owner;
        if (owner is not null && owner.IsVisible) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true;
    }

    public string? PickOpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title)
    {
        // OpenFolderDialog is available in WPF since .NET 8.
        var dlg = new OpenFolderDialog { Title = title, Multiselect = false };
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }
}
