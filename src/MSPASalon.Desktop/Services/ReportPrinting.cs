using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MSPASalon.Core.Reports;

namespace MSPASalon.Desktop.Services;

/// <summary>
/// A4 printing of a <see cref="ReportDocument"/> (also used for PDF via "Microsoft Print to PDF", decision T4).
/// Uses explicit black/grey colours: the printout never follows the dark theme.
/// </summary>
internal static class ReportPrinting
{
    private const double A4Width = 793.7, A4Height = 1122.5;   // DIP (1/96 inch)
    private static readonly Brush Ink = Brushes.Black;
    private static readonly Brush Muted = Frozen(0x55, 0x55, 0x55);
    private static readonly Brush HeaderFill = Frozen(0xED, 0xE7, 0xF0);
    private static readonly Brush Line = Frozen(0x9C, 0x93, 0xA6);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static bool Print(ReportDocument doc, bool toPdf)
    {
        var dialog = new PrintDialog();
        if (toPdf && FindPdfQueue() is { } pdf) dialog.PrintQueue = pdf;
        if (dialog.ShowDialog() != true) return false;

        var width = dialog.PrintableAreaWidth > 0 ? dialog.PrintableAreaWidth : A4Width;
        var height = dialog.PrintableAreaHeight > 0 ? dialog.PrintableAreaHeight : A4Height;
        var flow = Build(doc, width, height);
        var paginator = new FooterPaginator(((IDocumentPaginatorSource)flow).DocumentPaginator, $"{doc.Title} · {doc.GeneratedText}");
        dialog.PrintDocument(paginator, doc.Title);
        return true;
    }

    /// <summary>"Microsoft Print to PDF" when installed (Windows 10/11 default); null otherwise (user picks a printer).</summary>
    private static PrintQueue? FindPdfQueue()
    {
        try
        {
            var server = new LocalPrintServer();   // not disposed: the returned queue keeps using it
            return server.GetPrintQueues().FirstOrDefault(q => q.Name.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;   // print spooler unavailable: fall back to the normal dialog
        }
    }

    public static FlowDocument Build(ReportDocument doc, double pageWidth, double pageHeight)
    {
        var flow = new FlowDocument
        {
            PageWidth = pageWidth, PageHeight = pageHeight, ColumnWidth = pageWidth,
            PagePadding = new Thickness(48, 40, 48, 60),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 10, Foreground = Ink, Background = Brushes.White,
        };

        if (!string.IsNullOrWhiteSpace(doc.SalonName))
            flow.Blocks.Add(new Paragraph(new Run(doc.SalonName)) { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0) });
        flow.Blocks.Add(new Paragraph(new Run(doc.Title)) { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) });
        flow.Blocks.Add(new Paragraph(new Run($"Periode: {doc.PeriodText}")) { Margin = new Thickness(0, 2, 0, 10), Foreground = Muted });

        if (doc.Summary.Count > 0) flow.Blocks.Add(SummaryTable(doc.Summary));

        foreach (var section in doc.Sections)
        {
            flow.Blocks.Add(new Paragraph(new Run(section.Title)) { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
            flow.Blocks.Add(section.Rows.Count == 0
                ? new Paragraph(new Run(section.EmptyText)) { FontStyle = FontStyles.Italic, Foreground = Muted }
                : (Block)SectionTable(section));
        }

        foreach (var note in doc.Notes)
            flow.Blocks.Add(new Paragraph(new Run(note)) { FontSize = 8.5, FontStyle = FontStyles.Italic, Foreground = Muted, Margin = new Thickness(0, 6, 0, 0) });

        if (doc.Signatures.Count > 0) flow.Blocks.Add(SignatureTable(doc.Signatures));
        return flow;
    }

    private static Table SummaryTable(IReadOnlyList<ReportSummaryItem> items)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < 4; i++) table.Columns.Add(new TableColumn { Width = new GridLength(i % 2 == 0 ? 1.3 : 1, GridUnitType.Star) });
        var group = new TableRowGroup();
        for (var i = 0; i < items.Count; i += 2)
        {
            var row = new TableRow();
            for (var j = i; j < Math.Min(i + 2, items.Count); j++)
            {
                row.Cells.Add(Cell(items[j].Label, TextAlignment.Left, muted: true));
                row.Cells.Add(Cell(items[j].Value, TextAlignment.Left, bold: true));
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static Table SectionTable(ReportSection section)
    {
        var table = new Table { CellSpacing = 0, FontSize = 9 };
        foreach (var c in section.Columns) table.Columns.Add(new TableColumn { Width = new GridLength(c.Width, GridUnitType.Star) });

        var header = new TableRowGroup();
        var hr = new TableRow { Background = HeaderFill };
        foreach (var c in section.Columns)
            hr.Cells.Add(Cell(c.Header, c.IsNumeric ? TextAlignment.Right : TextAlignment.Left, bold: true, border: new Thickness(0, 0, 0, 1)));
        header.Rows.Add(hr);
        table.RowGroups.Add(header);

        var body = new TableRowGroup();
        foreach (var r in section.Rows) body.Rows.Add(Row(section, r, bold: false));
        if (section.Totals is { } totals) body.Rows.Add(Row(section, totals, bold: true));
        table.RowGroups.Add(body);
        return table;
    }

    private static TableRow Row(ReportSection section, object?[] values, bool bold)
    {
        var row = new TableRow();
        for (var i = 0; i < section.Columns.Count; i++)
        {
            var c = section.Columns[i];
            row.Cells.Add(Cell(ReportFormat.Format(i < values.Length ? values[i] : null, c.Kind),
                c.IsNumeric ? TextAlignment.Right : TextAlignment.Left, bold, border: bold ? new Thickness(0, 1, 0, 0) : default));
        }
        return row;
    }

    private static Table SignatureTable(IReadOnlyList<string> labels)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 28, 0, 0) };
        foreach (var _ in labels) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var row = new TableRow();
        foreach (var label in labels)
        {
            var p = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0) };
            p.Inlines.Add(new Run(label));
            p.Inlines.Add(new LineBreak()); p.Inlines.Add(new LineBreak()); p.Inlines.Add(new LineBreak()); p.Inlines.Add(new LineBreak());
            p.Inlines.Add(new Run("(______________________)"));
            row.Cells.Add(new TableCell(p));
        }
        var group = new TableRowGroup();
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        return table;
    }

    private static TableCell Cell(string text, TextAlignment align, bool bold = false, bool muted = false, Thickness border = default) =>
        new(new Paragraph(new Run(text)) { Margin = new Thickness(0), TextAlignment = align })
        {
            Padding = new Thickness(4, 2, 4, 2),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = muted ? Muted : Ink,
            BorderBrush = Line,
            BorderThickness = border,
        };

    /// <summary>Adds "title · printed at · Halaman x dari y" at the bottom of every page.</summary>
    private sealed class FooterPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        private readonly string _text;

        public FooterPaginator(DocumentPaginator inner, string text)
        {
            _inner = inner;
            _text = text;
            _inner.ComputePageCount();
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = _inner.GetPage(pageNumber);
            var container = new ContainerVisual();
            container.Children.Add(page.Visual);
            var footer = new DrawingVisual();
            using (var dc = footer.RenderOpen())
            {
                var text = new FormattedText($"{_text}   ·   Halaman {pageNumber + 1} dari {_inner.PageCount}",
                    CultureInfo.GetCultureInfo("id-ID"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 8, Muted, 1.0);
                dc.DrawText(text, new Point(48, page.Size.Height - 40));
            }
            container.Children.Add(footer);
            return new DocumentPage(container, page.Size, page.BleedBox, page.ContentBox);
        }

        public override bool IsPageCountValid => _inner.IsPageCountValid;
        public override int PageCount => _inner.PageCount;
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;
    }
}
