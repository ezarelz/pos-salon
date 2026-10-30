using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MSPASalon.Core.Domain;
using MSPASalon.Desktop.Converters;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Desktop.Services;

/// <summary>
/// Prints receipt text (already laid out to 32/48 characters by ReceiptBuilder) on a thermal printer through the
/// normal Windows printer driver. The font size is derived from the printer's printable width so one receipt line
/// always fits on one printed line.
/// </summary>
public sealed class PrintService : IPrintService
{
    private const double DipPerMm = 96 / 25.4;
    private const double ConsolasAdvancePerEm = 0.55;   // character width of Consolas relative to its font size

    /// <summary>Logo width as a share of the printable width (centred, aspect ratio kept).</summary>
    private const double LogoWidthRatio = 0.6;

    public bool PrintReceipt(IReadOnlyList<string> lines, int paperWidthMm, string jobName, byte[]? logo = null)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return false;

        var paperWidth = paperWidthMm * DipPerMm;
        var printable = dialog.PrintableAreaWidth > 0 ? Math.Min(dialog.PrintableAreaWidth, paperWidth) : paperWidth;
        const double padding = 4;
        var chars = ReceiptBuilder.CharsPerLine(paperWidthMm);
        var fontSize = Math.Clamp((printable - 2 * padding) / (chars * ConsolasAdvancePerEm), 6, 12);
        var lineHeight = Math.Ceiling(fontSize * 1.2);

        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = lineHeight };
        foreach (var line in lines)
        {
            paragraph.Inlines.Add(new Run(line));
            paragraph.Inlines.Add(new LineBreak());
        }

        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = fontSize,
            PagePadding = new Thickness(padding),
            PageWidth = printable,
            ColumnWidth = double.PositiveInfinity,
        };

        // Logo centred above the salon name.
        double logoHeight = 0;
        var image = logo is { Length: > 0 } ? ImageLoader.Load(logo) : null;
        if (image is not null && image.PixelWidth > 0)
        {
            var width = Math.Min((printable - 2 * padding) * LogoWidthRatio, image.PixelWidth);
            logoHeight = width * image.PixelHeight / image.PixelWidth;
            document.Blocks.Add(new BlockUIContainer(new Image
            {
                Source = image, Width = width, Height = logoHeight, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            })
            { Margin = new Thickness(0, 0, 0, lineHeight / 2) });
            logoHeight += lineHeight / 2;
        }
        document.Blocks.Add(paragraph);
        // Roll paper: page height = content, so the printer does not feed a full A4 page.
        document.PageHeight = (lines.Count + 3) * lineHeight + logoHeight + 2 * padding;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
        return true;
    }
}
