using System.Windows;
using MSPASalon.Presentation.Layout;

namespace MSPASalon.Desktop.Services;

/// <summary>
/// Applies the tested sizing rules (<see cref="WindowSizing"/>) to a real window using the current work area.
/// SystemParameters.WorkArea is already in device-independent pixels, i.e. it accounts for 100/125/150% scaling
/// and excludes the taskbar.
/// </summary>
public static class WindowPlacement
{
    public static void Apply(Window window, double preferredWidth, double preferredHeight, bool maximizeWhenSmall = false)
    {
        var work = SystemParameters.WorkArea;
        var p = WindowSizing.Initial(new WindowSizing.Size(work.Width, work.Height),
            new WindowSizing.Size(preferredWidth, preferredHeight), maximizeWhenSmall: maximizeWhenSmall);

        window.MinWidth = p.MinWidth;
        window.MinHeight = p.MinHeight;
        window.MaxWidth = double.PositiveInfinity;
        window.MaxHeight = double.PositiveInfinity;
        window.Width = p.Width;
        window.Height = p.Height;
        if (p.Maximize) window.WindowState = WindowState.Maximized;
    }
}
