using System.Windows;
using System.Windows.Controls;

namespace MSPASalon.Desktop.Controls;

/// <summary>
/// PasswordBox.Password is not a dependency property (by design, for security), so it cannot be data-bound.
/// This attached property mirrors it: <c>controls:PasswordBoxAssistant.BoundPassword="{Binding Password, Mode=TwoWay}"</c>.
/// </summary>
/// <remarks>
/// The default value MUST be null (not string.Empty). The PasswordChanged handler is attached inside the
/// property-changed callback, and that callback only runs when the bound value differs from the default.
/// With an empty-string default, a ViewModel that starts with Password = "" never triggers the callback,
/// the handler is never attached, and typed passwords never reach the ViewModel (login always fails).
/// </remarks>
public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordBoxAssistant),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

    private static readonly DependencyProperty IsAttachedProperty = DependencyProperty.RegisterAttached(
        "IsAttached", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject d) => (string?)d.GetValue(BoundPasswordProperty) ?? string.Empty;
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (!(bool)box.GetValue(IsAttachedProperty))
        {
            box.SetValue(IsAttachedProperty, true);
            box.PasswordChanged += OnPasswordChanged;
        }
        if ((bool)box.GetValue(IsUpdatingProperty)) return;
        var value = e.NewValue as string ?? string.Empty;
        if (box.Password != value) box.Password = value;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
