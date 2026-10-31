using System.IO.Compression;
using System.Xml.Linq;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Reports;
using MSPASalon.Infrastructure.Export;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.UnitTests;

public sealed class ReportUnitTests
{
    [Test("UNIT-17", "Periode laporan: pilihan cepat (minggu mulai Senin, 1–15 / 16–akhir, bulan lalu), teks periode, validasi")]
    public void Periods()
    {
        var sat = new DateTime(2026, 10, 31, 14, 0, 0);   // Saturday
        Assert.Equal((new DateTime(2026, 10, 31), new DateTime(2026, 10, 31)), ReportPeriods.Range(PeriodPreset.Today, sat));
        Assert.Equal((new DateTime(2026, 10, 26), new DateTime(2026, 10, 31)), ReportPeriods.Range(PeriodPreset.ThisWeek, sat), "week starts Monday");
        Assert.Equal((new DateTime(2026, 11, 2), new DateTime(2026, 11, 2)), ReportPeriods.Range(PeriodPreset.ThisWeek, new DateTime(2026, 11, 2)), "Monday itself");
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 15)), ReportPeriods.Range(PeriodPreset.FirstHalfThisMonth, sat));
        Assert.Equal((new DateTime(2026, 10, 16), new DateTime(2026, 10, 31)), ReportPeriods.Range(PeriodPreset.SecondHalfThisMonth, sat));
        Assert.Equal((new DateTime(2026, 9, 16), new DateTime(2026, 9, 30)), ReportPeriods.Range(PeriodPreset.SecondHalfLastMonth, sat));
        Assert.Equal((new DateTime(2026, 2, 1), new DateTime(2026, 2, 28)), ReportPeriods.Range(PeriodPreset.LastMonth, new DateTime(2026, 3, 5)));
        Assert.Equal((new DateTime(2025, 12, 1), new DateTime(2025, 12, 31)), ReportPeriods.Range(PeriodPreset.LastMonth, new DateTime(2026, 1, 10)), "year boundary");

        Assert.Equal("31 Oktober 2026", ReportPeriods.Text(sat, sat));
        Assert.Equal("1–15 Oktober 2026", ReportPeriods.Text(new DateTime(2026, 10, 1), new DateTime(2026, 10, 15)));
        Assert.Equal("28 September 2026 – 4 Oktober 2026", ReportPeriods.Text(new DateTime(2026, 9, 28), new DateTime(2026, 10, 4)));
        Assert.True(ReportPeriods.Validate(sat, sat.AddDays(-1)) is not null, "to < from");
        Assert.True(ReportPeriods.Validate(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)) is null, "365 days ok");
        Assert.True(ReportPeriods.Validate(new DateTime(2025, 1, 1), new DateTime(2026, 12, 31)) is not null, "> 366 days");

        Assert.Equal("28.000", ReportFormat.Format(28000m, ReportValueKind.Money));
        Assert.Equal("0,15", ReportFormat.Format(0.15m, ReportValueKind.Qty));
        Assert.Equal("25,5%", ReportFormat.Format(0.255m, ReportValueKind.Percent));
        Assert.Equal("1.250", ReportFormat.Format(1250, ReportValueKind.Integer));
        Assert.Equal("14:05", ReportFormat.Format(new DateTime(2026, 10, 31, 14, 5, 0), ReportValueKind.Time));
        Assert.Equal("", ReportFormat.Format(null, ReportValueKind.Money));
    }

    private static ReportDocument SampleDocument() => new()
    {
        Title = "Laporan Uji", PeriodText = "1–15 Oktober 2026", SalonName = "Salon <Contoh> & Co", FileName = "uji",
        GeneratedAt = new DateTime(2026, 10, 31, 9, 0, 0), GeneratedBy = "admin",
        Summary = [new("Total", "Rp 175.000")],
        Sections =
        [
            new ReportSection
            {
                Title = "Per hari: rincian [A/B]?",   // characters not allowed in sheet names
                Columns = [new("Tanggal", ReportValueKind.Date, 12), new("Customer", Width: 20), new("Total", ReportValueKind.Money, 14)],
                Rows = [[new DateTime(2026, 10, 1), "Budi \u0001 & \"Ani\"", 40000m], [new DateTime(2026, 10, 2), null, 135000m]],
                Totals = ["Total", null, 175000m],
            },
            new ReportSection { Title = "Per hari: rincian [A/B]?", Columns = [new("X")], Rows = [] },
        ],
        Notes = ["Catatan"],
    };

    [Test("UNIT-18", "Export Excel: .xlsx valid (zip + XML), sheet per bagian, angka & tanggal bertipe, karakter khusus aman")]
    public void XlsxExport()
    {
        using var ms = new MemoryStream();
        XlsxWriter.Write(SampleDocument(), ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml",
                     "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml", "xl/worksheets/sheet3.xml" })
            Assert.True(zip.GetEntry(part) is not null, $"missing {part}");

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument Load(string name) { using var s = zip.GetEntry(name)!.Open(); return XDocument.Load(s); }   // throws if not well-formed
        var names = Load("xl/workbook.xml").Descendants(ns + "sheet").Select(e => (string)e.Attribute("name")!).ToList();
        Assert.Equal("Ringkasan", names[0]);
        Assert.Equal("Per hari rincian AB", names[1]);
        Assert.Equal("Per hari rincian AB (2)", names[2], "unique sheet names");
        Assert.True(names.All(n => n.Length <= 31));

        var sheet = Load("xl/worksheets/sheet2.xml");
        var cells = sheet.Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!);
        Assert.Equal("Tanggal", cells["A4"].Value, "header row 4");
        Assert.Equal(new DateTime(2026, 10, 1).ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture), cells["A5"].Element(ns + "v")!.Value, "date as serial number");
        Assert.Equal("40000", cells["C5"].Element(ns + "v")!.Value, "money as number");
        Assert.True(cells["C5"].Attribute("t") is null, "numeric cell, not text");
        Assert.Equal("Budi  & \"Ani\"", cells["B5"].Value, "control char dropped, & and quotes escaped");
        Assert.False(cells.ContainsKey("B6"), "null cell omitted");
        Assert.Equal("175000", cells["C7"].Element(ns + "v")!.Value, "totals row");
        Assert.True(sheet.Descendants(ns + "pane").Any(), "header frozen");
        Assert.True(Load("xl/worksheets/sheet1.xml").Descendants(ns + "t").Any(t => t.Value == "Salon <Contoh> & Co"), "< > & round-trip");
        Assert.Equal("AA", XlsxWriter.ColumnName(26));
        Assert.Equal("Z", XlsxWriter.ColumnName(25));
    }

    [Test("UNIT-19", "Struk rekap kas 58 mm: per metode, total, tunai di laci, kolom paraf; semua baris ≤ 32 karakter")]
    public void CashReceipt()
    {
        var r = new CashDailyReport(new DateTime(2026, 10, 31),
            [new("Tunai", true, 2, 45000), new("QRIS Pembayaran Digital Sangat Panjang", false, 3, 135000)],
            [new(new DateTime(2026, 10, 31, 10, 0, 0), "2610/RG/0001", "Treatment", "Budi", "Tunai", null, 40000, true)], 1);
        var lines = ReportBuilders.CashDailyReceipt(r, new Branch { Name = "Salon Contoh" }, "Kasir", new DateTime(2026, 10, 31, 21, 0, 0), 32);
        Assert.True(lines.All(l => l.Length <= 32), lines.FirstOrDefault(l => l.Length > 32) ?? "");
        Assert.True(lines.Any(l => l.StartsWith("TOTAL") && l.EndsWith("180.000")));
        Assert.True(lines.Any(l => l.StartsWith("Tunai di laci") && l.EndsWith("45.000")));
        Assert.True(lines.Any(l => l.StartsWith("Dibatalkan")));
        Assert.True(lines.Any(l => l.StartsWith("Paraf")));
    }
}
