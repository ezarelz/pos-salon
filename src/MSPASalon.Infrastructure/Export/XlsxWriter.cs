using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Reports;

namespace MSPASalon.Infrastructure.Export;

/// <summary>
/// Minimal Office Open XML (.xlsx) writer for reports, without third-party libraries (decision T3).
/// Writes real numbers and dates with number formats, so totals can be recalculated in Excel.
/// Sheet 1 = summary; one sheet per report section (header row frozen).
/// </summary>
public sealed class XlsxWriter : IReportExporter
{
    // cellXfs indexes in styles.xml
    private const int SDefault = 0, SHeader = 1, SMoney = 2, SInteger = 3, SDate = 4, SDateTime = 5, STime = 6,
        SPercent = 7, STitle = 8, SBoldMoney = 9, SBoldInteger = 10, SBold = 11, SMuted = 12;

    public void ToXlsx(ReportDocument document, string path)
    {
        // Write to a temp file first: a failure (or the file being open in Excel) never leaves a half-written workbook.
        var temp = path + ".tmp";
        using (var fs = File.Create(temp)) Write(document, fs);
        File.Move(temp, path, overwrite: true);
    }

    public static void Write(ReportDocument doc, Stream output)
    {
        var sheets = new List<(string Name, string Xml)> { ("Ringkasan", SummarySheet(doc)) };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ringkasan" };
        foreach (var section in doc.Sections)
            sheets.Add((UniqueSheetName(section.Title, used), SectionSheet(doc, section)));

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Add(zip, "[Content_Types].xml", ContentTypes(sheets.Count));
        Add(zip, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Add(zip, "xl/workbook.xml", Workbook(sheets.Select(s => s.Name).ToList()));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Count));
        Add(zip, "xl/styles.xml", Styles);
        for (var i = 0; i < sheets.Count; i++) Add(zip, $"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
    }

    // ------------------------------------------------------------------ sheets

    private static string SummarySheet(ReportDocument doc)
    {
        var rows = new SheetRows();
        rows.Add([Str(doc.Title, STitle)]);
        if (!string.IsNullOrWhiteSpace(doc.SalonName)) rows.Add([Str(doc.SalonName, SBold)]);
        rows.Add([Str($"Periode: {doc.PeriodText}", SDefault)]);
        rows.Add([Str(doc.GeneratedText, SMuted)]);
        rows.Add([]);
        foreach (var s in doc.Summary) rows.Add([Str(s.Label, SDefault), Str(s.Value, SBold)]);
        if (doc.Notes.Count > 0)
        {
            rows.Add([]);
            foreach (var n in doc.Notes) rows.Add([Str(n, SMuted)]);
        }
        return Sheet(rows, [34, 26], freezeRows: 0);
    }

    private static string SectionSheet(ReportDocument doc, ReportSection section)
    {
        var rows = new SheetRows();
        rows.Add([Str($"{doc.Title} — {section.Title}", STitle)]);
        rows.Add([Str($"Periode: {doc.PeriodText}", SDefault)]);
        rows.Add([]);
        rows.Add(section.Columns.Select(c => Str(c.Header, SHeader)).ToArray());
        foreach (var r in section.Rows)
            rows.Add(section.Columns.Select((c, i) => Value(i < r.Length ? r[i] : null, c.Kind, bold: false)).ToArray());
        if (section.Rows.Count == 0) rows.Add([Str(section.EmptyText, SMuted)]);
        if (section.Totals is { } totals && section.Rows.Count > 0)
            rows.Add(section.Columns.Select((c, i) => Value(i < totals.Length ? totals[i] : null, c.Kind, bold: true)).ToArray());
        return Sheet(rows, section.Columns.Select(c => c.Width).ToList(), freezeRows: 4);
    }

    private sealed class SheetRows : List<Cell?[]>;

    private sealed record Cell(string Xml);

    private static Cell Str(string text, int style) =>
        new($"<c t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>");

    private static Cell Num(double value, int style) =>
        new($"<c s=\"{style}\"><v>{value.ToString("R", CultureInfo.InvariantCulture)}</v></c>");

    private static Cell? Value(object? v, ReportValueKind kind, bool bold)
    {
        if (v is null) return null;
        if (v is string s) return Str(s, bold ? SBold : SDefault);
        if (v is DateTime d)
            return Num(d.ToOADate(), kind switch { ReportValueKind.Date => SDate, ReportValueKind.Time => STime, _ => SDateTime });
        var n = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        return kind switch
        {
            ReportValueKind.Money => Num(n, bold ? SBoldMoney : SMoney),
            ReportValueKind.Integer => Num(n, bold ? SBoldInteger : SInteger),
            ReportValueKind.Percent => Num(n, SPercent),
            _ => Num(n, bold ? SBold : SDefault),
        };
    }

    private static string Sheet(SheetRows rows, IReadOnlyList<double> widths, int freezeRows)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        if (freezeRows > 0)
            sb.Append($"""<sheetViews><sheetView workbookViewId="0"><pane ySplit="{freezeRows}" topLeftCell="A{freezeRows + 1}" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        sb.Append("<cols>");
        for (var i = 0; i < widths.Count; i++)
            sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{widths[i].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append("</cols><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (var col = 0; col < rows[r].Length; col++)
            {
                var cell = rows[r][col];
                if (cell is null) continue;
                sb.Append(cell.Xml.Insert(2, $" r=\"{ColumnName(col)}{r + 1}\""));   // "<c" + r="A1" + rest
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ package parts

    private static string ContentTypes(int sheetCount)
    {
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (var i = 1; i <= sheetCount; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(IReadOnlyList<string> names)
    {
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        for (var i = 0; i < names.Count; i++)
            sb.Append($"<sheet name=\"{Esc(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (var i = 1; i <= sheetCount; i++)
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        sb.Append($"<Relationship Id=\"rId{sheetCount + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    // numFmt 3 = "#,##0" (thousand separators follow the user's Excel locale), 1 = "0", 10 = "0.00%".
    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <numFmts count="3"><numFmt numFmtId="164" formatCode="dd\-mm\-yyyy"/><numFmt numFmtId="165" formatCode="dd\-mm\-yyyy\ hh:mm"/><numFmt numFmtId="166" formatCode="hh:mm"/></numFmts>
        <fonts count="4"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="14"/><name val="Calibri"/></font><font><i/><sz val="10"/><color rgb="FF6B6472"/><name val="Calibri"/></font></fonts>
        <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEDE7F0"/><bgColor indexed="64"/></patternFill></fill></fills>
        <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style="thin"><color rgb="FF9C93A6"/></bottom><diagonal/></border></borders>
        <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
        <cellXfs count="13">
        <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
        <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/>
        <xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="1" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="10" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
        <xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/>
        <xf numFmtId="3" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/>
        <xf numFmtId="1" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/>
        <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
        <xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1"/>
        </cellXfs>
        <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;

    // ------------------------------------------------------------------ helpers

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content.Trim());
    }

    private static string Esc(string s)
    {
        // XML 1.0 forbids most control characters; drop them instead of producing an unreadable file.
        var clean = new string(s.Where(ch => ch >= 0x20 || ch is '\t' or '\n' or '\r').ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }

    public static string ColumnName(int index)
    {
        var name = "";
        for (var i = index + 1; i > 0; i = (i - 1) / 26) name = (char)('A' + (i - 1) % 26) + name;
        return name;
    }

    /// <summary>Excel sheet names: max 31 chars, no : \ / ? * [ ], unique (case-insensitive).</summary>
    public static string UniqueSheetName(string title, ISet<string> used)
    {
        var baseName = new string(title.Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray()).Trim();
        if (baseName.Length == 0) baseName = "Data";
        if (baseName.Length > 31) baseName = baseName[..31];
        var name = baseName;
        for (var n = 2; used.Contains(name); n++)
        {
            var suffix = $" ({n})";
            name = (baseName.Length + suffix.Length > 31 ? baseName[..(31 - suffix.Length)] : baseName) + suffix;
        }
        used.Add(name);
        return name;
    }
}
