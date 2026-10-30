using System.Windows;
using System.Windows.Input;
using MSPASalon.Desktop.Services;
using MSPASalon.Presentation.ViewModels;

namespace MSPASalon.Desktop.Views;

/// <summary>View-only code: sizing, focus and closing. All logic lives in the EditorViewModel.</summary>
public partial class EditorWindow : Window
{
    private bool _closingFromViewModel;

    public EditorWindow(EditorViewModel editor)
    {
        InitializeComponent();
        DataContext = editor;
        WindowPlacement.Apply(this, editor.PreferredWidth, editor.PreferredHeight);
        editor.CloseRequested += OnCloseRequested;
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Closed += (_, _) => editor.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        if (_closingFromViewModel) return;
        _closingFromViewModel = true;
        try
        {
            DialogResult = saved;   // closes the modal window
        }
        catch (InvalidOperationException)
        {
            // The window is already closing (e.g. WPF handled the IsCancel/Esc button itself). Nothing to do.
        }
    }
}
