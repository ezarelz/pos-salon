using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MSPASalon.Core.Domain;

namespace MSPASalon.Desktop.Converters;

/// <summary>decimal → "28.000" (Indonesian grouping, whole Rupiah).</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal d ? Money.Format(d) : value is long l ? Money.Format(l) : value is int i ? Money.Format(i) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>decimal → "0,1" (decimal comma, no grouping, up to 4 decimals).</summary>
public sealed class QtyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal d ? Quantity.Format(d) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>bool → "✓" / "".</summary>
public sealed class CheckMarkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? "✓" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>null / empty string / empty collection → Collapsed, otherwise Visible.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            ICollection c => c.Count > 0,
            _ => true,
        };
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Navigation width in DIP: collapsed (icons only) or expanded.</summary>
public sealed class NavWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? 56d : 232d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0 → Visible, anything else → Collapsed (e.g. "all good" message when a list is empty).</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>DateTime → "31-10-2026 14:05".</summary>
public sealed class DateTimeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateTime d when d.TimeOfDay == TimeSpan.Zero && parameter as string == "date" => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Image bytes (PNG/JPEG/BMP) → ImageSource for previews; null or invalid bytes → null (nothing shown).</summary>
public sealed class ImageBytesConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is byte[] { Length: > 0 } bytes ? ImageLoader.Load(bytes) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class ImageLoader
{
    /// <summary>Decodes fully into memory (OnLoad) and freezes, so the stream can be closed and the image shared.</summary>
    public static System.Windows.Media.Imaging.BitmapImage? Load(byte[] bytes)
    {
        try
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            using var ms = new System.IO.MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception)
        {
            return null;   // corrupt image: print/preview without logo rather than failing
        }
    }
}

/// <summary>
/// Shared converter instances, referenced from XAML with <c>{x:Static conv:Conv.Name}</c>.
/// x:Static is resolved by type, never by resource-dictionary lookup, so these can never fail with
/// "Cannot find resource named ..." — regardless of where a DataTemplate is defined or merged.
/// </summary>
public static class Conv
{
    public static readonly BooleanToVisibilityConverter BoolToVisibility = new();
    public static readonly InverseBoolToVisibilityConverter InverseBoolToVisibility = new();
    public static readonly HasValueToVisibilityConverter HasValueToVisibility = new();
    public static readonly ZeroToVisibleConverter ZeroToVisible = new();
    public static readonly NavWidthConverter NavWidth = new();
    public static readonly MoneyConverter Money = new();
    public static readonly QtyConverter Qty = new();
    public static readonly DateTimeTextConverter DateTimeText = new();
    public static readonly ImageBytesConverter ImageBytes = new();
}
