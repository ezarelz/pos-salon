using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MSPASalon.Core.Reports;
using MSPASalon.Desktop.Converters;
using MSPASalon.Presentation.ViewModels.Reports;

namespace MSPASalon.Desktop.Controls;

/// <summary>
/// Attached property that builds a DataGrid's columns from a report section (columns vary per report).
/// Cells bind to the row indexer ("[0]", "[1]", ...) and are formatted by <see cref="ReportCellConverter"/>.
/// Sorting is off so the bold total row always stays last.
/// </summary>
public static class ReportGrid
{
    public static readonly DependencyProperty SectionProperty = DependencyProperty.RegisterAttached(
        "Section", typeof(ReportGridSection), typeof(ReportGrid), new PropertyMetadata(null, OnSectionChanged));

    public static ReportGridSection? GetSection(DependencyObject d) => (ReportGridSection?)d.GetValue(SectionProperty);
    public static void SetSection(DependencyObject d, ReportGridSection? value) => d.SetValue(SectionProperty, value);

    private static void OnSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        grid.Columns.Clear();
        if (e.NewValue is not ReportGridSection section) return;

        var right = grid.TryFindResource("RightCell") as Style;
        // The widest text column takes the remaining width; the others size to their content.
        var star = section.Columns
            .Select((c, i) => (c, i))
            .Where(x => x.c.Kind == ReportValueKind.Text)
            .OrderByDescending(x => x.c.Width)
            .Select(x => x.i)
            .DefaultIfEmpty(-1)
            .First();
        for (var i = 0; i < section.Columns.Count; i++)
        {
            var col = section.Columns[i];
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = col.Header,
                Binding = new Binding($"[{i}]") { Mode = BindingMode.OneWay, Converter = Conv.ReportCell, ConverterParameter = col.Kind },
                ElementStyle = col.IsNumeric ? right : null,
                Width = i == star ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = Math.Max(48, col.Width * 6),
                CanUserSort = false,
            });
        }
    }
}
