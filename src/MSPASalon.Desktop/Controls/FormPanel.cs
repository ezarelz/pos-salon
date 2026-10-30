using System.Windows;
using System.Windows.Controls;

namespace MSPASalon.Desktop.Controls;

/// <summary>
/// Responsive form layout. Lays its children out in as many equal columns as fit
/// (each at least <see cref="ColumnMinWidth"/>, at most <see cref="MaxColumns"/>), so a form shows
/// two columns on a wide dialog and one column on a narrow one. Children marked with
/// <c>FormPanel.IsFullRow="True"</c> always take a whole row (addresses, notes, check-box groups).
/// Collapsed children take no space. Unlike a fixed Grid or a WrapPanel with fixed widths, fields
/// stretch to the available width and never overflow horizontally.
/// </summary>
public sealed class FormPanel : Panel
{
    public static readonly DependencyProperty ColumnMinWidthProperty = DependencyProperty.Register(
        nameof(ColumnMinWidth), typeof(double), typeof(FormPanel),
        new FrameworkPropertyMetadata(260d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(FormPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(FormPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(FormPanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty IsFullRowProperty = DependencyProperty.RegisterAttached(
        "IsFullRow", typeof(bool), typeof(FormPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public double ColumnMinWidth { get => (double)GetValue(ColumnMinWidthProperty); set => SetValue(ColumnMinWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => (double)GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }

    public static bool GetIsFullRow(DependencyObject element) => (bool)element.GetValue(IsFullRowProperty);
    public static void SetIsFullRow(DependencyObject element, bool value) => element.SetValue(IsFullRowProperty, value);

    private int ColumnCount(double width)
    {
        var fit = (int)Math.Floor((width + ColumnSpacing) / (ColumnMinWidth + ColumnSpacing));
        return Math.Max(1, Math.Min(Math.Max(1, MaxColumns), fit));
    }

    /// <summary>Runs the layout; measures children when <paramref name="measure"/> is true, otherwise arranges them.</summary>
    private Size Layout(double width, bool measure)
    {
        if (double.IsInfinity(width) || double.IsNaN(width))
            width = MaxColumns * ColumnMinWidth + (MaxColumns - 1) * ColumnSpacing;
        width = Math.Max(0, width);

        var columns = ColumnCount(width);
        var columnWidth = Math.Max(0, (width - (columns - 1) * ColumnSpacing) / columns);

        double y = 0, rowHeight = 0;
        var column = 0;
        var rowItems = new List<(UIElement Element, double X, double Width)>();

        void FlushRow()
        {
            if (rowItems.Count == 0) return;
            if (!measure)
                foreach (var (el, x, w) in rowItems)
                    el.Arrange(new Rect(x, y, w, rowHeight));
            y += rowHeight + RowSpacing;
            rowHeight = 0;
            column = 0;
            rowItems.Clear();
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child is null) continue;
            if (child.Visibility == Visibility.Collapsed)
            {
                if (measure) child.Measure(new Size(0, 0));
                else child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var full = GetIsFullRow(child);
            if (full && column > 0) FlushRow();

            var w = full ? width : columnWidth;
            var x = full ? 0 : column * (columnWidth + ColumnSpacing);
            if (measure) child.Measure(new Size(w, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            rowItems.Add((child, x, w));

            column = full ? columns : column + 1;
            if (column >= columns) FlushRow();
        }
        FlushRow();

        var height = Math.Max(0, y - RowSpacing);
        return new Size(width, height);
    }

    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize.Width, measure: true);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, measure: false);
        return finalSize;
    }
}
