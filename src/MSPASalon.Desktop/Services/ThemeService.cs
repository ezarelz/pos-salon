using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Desktop.Services;

/// <summary>
/// Light/dark mode. Swaps the palette dictionary (Themes/Palette.Light.xaml ↔ Palette.Dark.xaml) in the application
/// resources; every view references colours with DynamicResource, so open windows repaint immediately.
/// The choice is stored per Windows user in %LOCALAPPDATA%\MSPASalon\ui-settings.json (not in the database:
/// it is a preference of this computer, not business data). Also asks Windows 10 (1809+) / 11 to draw dark title bars.
/// </summary>
public sealed class ThemeService : IThemeService
{
    // Root-relative pack URIs ("/Themes/..." = this application's assembly), so renaming the exe never breaks the theme.
    private const string LightPalette = "/Themes/Palette.Light.xaml";
    private const string DarkPalette = "/Themes/Palette.Dark.xaml";

    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MSPASalon", "ui-settings.json");

    private sealed class UiSettings
    {
        public string Theme { get; set; } = "light";
    }

    public bool IsDark { get; private set; }

    /// <summary>Reads the saved choice and applies it. Call once at startup, before the first window is shown.</summary>
    public static ThemeService LoadAndApply()
    {
        var service = new ThemeService();
        // Every window created later gets the matching title bar as soon as it has a handle.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) ApplyTitleBar(w, service.IsDark); }));
        service.Apply(ReadSetting());
        return service;
    }

    public void SetDark(bool dark)
    {
        Apply(dark);
        Save(dark);
    }

    private void Apply(bool dark)
    {
        IsDark = dark;
        var merged = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri(dark ? DarkPalette : LightPalette, UriKind.Relative) };
        var index = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source?.OriginalString.Contains("Palette.", StringComparison.OrdinalIgnoreCase) == true) { index = i; break; }
        }
        if (index >= 0) merged[index] = palette;
        else merged.Insert(0, palette);

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w, dark);
    }

    private static bool ReadSetting()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return false;
            var s = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsFile));
            return string.Equals(s?.Theme, "dark", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;   // unreadable settings file: fall back to light mode, never block startup
        }
    }

    private static void Save(bool dark)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new UiSettings { Theme = dark ? "dark" : "light" }));
        }
        catch (Exception)
        {
            // Preference only: the theme is already applied for this session.
        }
    }

    // ---------------------------------------------------------------- title bar (DWM)

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;          // Windows 10 20H1+ and Windows 11
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19; // Windows 10 1809–1909

    private static void ApplyTitleBar(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows without dwmapi support: keep the default title bar.
        }
    }
}
