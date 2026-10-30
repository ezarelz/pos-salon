namespace MSPASalon.Presentation.Layout;

/// <summary>
/// Responsive sizing rules, kept free of WPF so they are unit-tested.
///
/// All values are device-independent pixels (DIP) = what WPF reports after Windows scaling.
/// Worst supported case: 1280×720 at 150% scaling = 853×480 DIP, minus the taskbar ≈ 853×440 DIP.
///   1280×720 @100% → 1280×680 work area     1366×768 @125% → 1093×574     1280×720 @150% → 853×440
/// The legacy transaction window was a fixed 1160×780 and did not fit any of these (defect: Save button cut off).
/// </summary>
public static class WindowSizing
{
    /// <summary>Smallest size every window is designed for; all essential buttons stay visible at this size.</summary>
    public const double DesignMinWidth = 800;
    public const double DesignMinHeight = 440;

    /// <summary>Below this content width, side panels move underneath / navigation collapses to icons.</summary>
    public const double CompactBreakpoint = 1000;

    public readonly record struct Size(double Width, double Height);

    public readonly record struct Placement(double Width, double Height, double MinWidth, double MinHeight, bool Maximize);

    /// <summary>
    /// Initial window placement: the preferred size, shrunk to at most <paramref name="maxFraction"/> of the work area,
    /// never below the design minimum unless the screen itself is smaller. Minimum size never exceeds the work area,
    /// so a window can never be larger than the screen (a resize grip is always reachable).
    /// </summary>
    public static Placement Initial(Size workArea, Size preferred, double maxFraction = 0.92, bool maximizeWhenSmall = false)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) throw new ArgumentOutOfRangeException(nameof(workArea));
        if (maxFraction is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(maxFraction));

        var minW = Math.Min(DesignMinWidth, workArea.Width);
        var minH = Math.Min(DesignMinHeight, workArea.Height);
        var w = Math.Clamp(Math.Min(preferred.Width, workArea.Width * maxFraction), minW, workArea.Width);
        var h = Math.Clamp(Math.Min(preferred.Height, workArea.Height * maxFraction), minH, workArea.Height);
        var maximize = maximizeWhenSmall && (workArea.Width < preferred.Width || workArea.Height < preferred.Height);
        return new Placement(Math.Floor(w), Math.Floor(h), Math.Floor(minW), Math.Floor(minH), maximize);
    }

    public static bool IsCompact(double availableWidth) => availableWidth < CompactBreakpoint;
}
