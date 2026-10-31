using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;

namespace MSPASalon.Core.Reports;

/// <summary>Turns report data into a <see cref="ReportDocument"/> (screen, Excel, print use the same document).</summary>
public static class ReportBuilders
{
    private static string Rp(decimal v) => ReportFormat.Rp(v);
    private static string Safe(string s) => new(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
    private static string Stamp(DateTime from, DateTime to) =>
        from.Date == to.Date ? from.ToString("yyyyMMdd") : $"{from:yyyyMMdd}-{to:yyyyMMdd}";

    // ------------------------------------------------------------------ 1. Rekap Kas Harian

    public static ReportDocument CashDaily(CashDailyReport r, string salon, string user, DateTime now) => new()
    {
        Title = "Rekap Kas Harian",
        PeriodText = ReportPeriods.Text(r.Day, r.Day),
        SalonName = salon, GeneratedAt = now, GeneratedBy = user,
        FileName = $"Rekap-Kas-{Stamp(r.Day, r.Day)}",
        Summary =
        [
            new("Total penerimaan", Rp(r.Total)),
            new("Tunai (seharusnya di laci)", Rp(r.CashTotal)),
            new("Non-tunai (QRIS/transfer)", Rp(r.NonCashTotal)),
            new("Jumlah transaksi", r.Documents.Count.ToString()),
        ],
        Sections =
        [
            new ReportSection
            {
                Title = "Per metode bayar",
                Columns = [new("Metode", Width: 22), new("Transaksi", ReportValueKind.Integer, 12), new("Jumlah", ReportValueKind.Money, 16)],
                Rows = r.Methods.Select(m => new object?[] { m.Method, m.Count, m.Amount }).ToList(),
                Totals = ["Total", r.Methods.Sum(m => m.Count), r.Total],
            },
            new ReportSection
            {
                Title = "Daftar nota",
                Columns =
                [
                    new("Jam", ReportValueKind.Time, 8), new("No. Nota", Width: 16), new("Jenis", Width: 12), new("Customer", Width: 24),
                    new("Metode", Width: 14), new("Referensi", Width: 16), new("Jumlah", ReportValueKind.Money, 14),
                ],
                Rows = r.Documents.Select(d => new object?[] { d.At, d.DocNo, d.Kind, d.Customer, d.Method, d.Reference, d.Amount }).ToList(),
                Totals = ["Total", null, null, null, null, null, r.Total],
            },
        ],
        Notes = r.CancelledCount > 0
            ? [$"{r.CancelledCount} dokumen dibatalkan pada tanggal ini dan tidak termasuk dalam rekap (lihat Laporan Pembatalan)."]
            : [],
    };

    /// <summary>Cash recap for the 58/80 mm thermal printer (end of day, pasted into the cash book).</summary>
    public static IReadOnlyList<string> CashDailyReceipt(CashDailyReport r, Branch branch, string user, DateTime now, int width)
    {
        var o = new List<string>();
        var rule = new string('-', width);
        foreach (var l in ReceiptBuilder.Wrap(branch.Name.ToUpperInvariant(), width)) o.Add(ReceiptBuilder.Center(l, width));
        o.Add(rule);
        o.Add(ReceiptBuilder.Center("REKAP KAS HARIAN", width));
        o.Add(ReceiptBuilder.Center(r.Day.ToString("dd-MM-yyyy"), width));
        o.Add(rule);
        foreach (var m in r.Methods)
            o.Add(ReceiptBuilder.LeftRight($"{m.Method} ({m.Count}x)", Money.Format(m.Amount), width));
        if (r.Methods.Count == 0) o.Add("Belum ada transaksi.");
        o.Add(rule);
        o.Add(ReceiptBuilder.LeftRight("TOTAL", Money.Format(r.Total), width));
        o.Add(ReceiptBuilder.LeftRight("Tunai di laci", Money.Format(r.CashTotal), width));
        o.Add(ReceiptBuilder.LeftRight("Non-tunai", Money.Format(r.NonCashTotal), width));
        o.Add(ReceiptBuilder.LeftRight("Jumlah nota", r.Documents.Count.ToString(), width));
        if (r.CancelledCount > 0) o.Add(ReceiptBuilder.LeftRight("Dibatalkan", r.CancelledCount.ToString(), width));
        o.Add(rule);
        o.Add($"Dicetak {now:dd-MM-yyyy HH:mm}");
        o.Add($"Oleh {user}");
        o.Add("");
        o.Add("Uang fisik: ______________");
        o.Add("Paraf     : ______________");
        return o.Select(l => l.Length <= width ? l : l[..width]).ToList();
    }

    // ------------------------------------------------------------------ 2. Pendapatan

    public static ReportDocument Revenue(RevenueReport r, string salon, string user, DateTime now) => new()
    {
        Title = "Laporan Pendapatan",
        PeriodText = ReportPeriods.Text(r.From, r.To),
        SalonName = salon, GeneratedAt = now, GeneratedBy = user,
        FileName = $"Pendapatan-{Stamp(r.From, r.To)}",
        Summary =
        [
            new("Total pendapatan", Rp(r.Total)),
            new("Pendapatan jasa (neto)", Rp(r.ServiceNet)),
            new("Penjualan produk (neto)", Rp(r.ProductNet)),
            new("Total diskon", Rp(r.Discount)),
            new("Client selesai", r.Clients.ToString()),
            new("Rata-rata jasa / client", Rp(r.Clients == 0 ? 0 : Money.Round(r.ServiceNet / r.Clients))),
        ],
        Sections =
        [
            new ReportSection
            {
                Title = "Per hari",
                Columns =
                [
                    new("Tanggal", ReportValueKind.Date, 12), new("Client", ReportValueKind.Integer, 9),
                    new("Jasa bruto", ReportValueKind.Money, 14), new("Diskon jasa", ReportValueKind.Money, 13),
                    new("Jasa neto", ReportValueKind.Money, 14), new("Nota produk", ReportValueKind.Integer, 11),
                    new("Produk neto", ReportValueKind.Money, 14), new("Total", ReportValueKind.Money, 15),
                ],
                Rows = r.Days.Select(d => new object?[]
                    { d.Day, d.Clients, d.ServiceGross, d.ServiceDiscount, d.ServiceNet, d.SalesCount, d.ProductNet, d.Total }).ToList(),
                Totals =
                [
                    "Total", r.Days.Sum(d => d.Clients), r.Days.Sum(d => d.ServiceGross), r.Days.Sum(d => d.ServiceDiscount),
                    r.ServiceNet, r.Days.Sum(d => d.SalesCount), r.ProductNet, r.Total,
                ],
            },
            new ReportSection
            {
                Title = "Per metode bayar",
                Columns = [new("Metode", Width: 22), new("Transaksi", ReportValueKind.Integer, 12), new("Jumlah", ReportValueKind.Money, 16)],
                Rows = r.Methods.Select(m => new object?[] { m.Method, m.Count, m.Amount }).ToList(),
                Totals = ["Total", r.Methods.Sum(m => m.Count), r.Methods.Sum(m => m.Amount)],
            },
            new ReportSection
            {
                Title = "Rincian nota",
                Columns =
                [
                    new("Waktu", ReportValueKind.DateTime, 17), new("No. Nota", Width: 16), new("Jenis", Width: 12),
                    new("Customer", Width: 24), new("Bruto", ReportValueKind.Money, 13), new("Diskon", ReportValueKind.Money, 12),
                    new("Neto", ReportValueKind.Money, 13), new("Metode", Width: 14),
                ],
                Rows = r.Documents.Select(d => new object?[] { d.At, d.DocNo, d.Kind, d.Customer, d.Gross, d.Discount, d.Net, d.Method }).ToList(),
                Totals = ["Total", null, null, null, r.Documents.Sum(d => d.Gross), r.Documents.Sum(d => d.Discount), r.Documents.Sum(d => d.Net), null],
            },
        ],
        Notes = ["Hanya dokumen LUNAS/POSTED, berdasarkan waktu posting. Dokumen yang dibatalkan tidak dihitung."],
    };

    // ------------------------------------------------------------------ 3. Komisi Capster

    public static ReportDocument Commission(CommissionReport r, string salon, string user, DateTime now)
    {
        var single = r.EmployeeFilterName is not null;
        return new ReportDocument
        {
            Title = single ? $"Slip Komisi — {r.EmployeeFilterName}" : "Laporan Komisi Capster",
            PeriodText = ReportPeriods.Text(r.From, r.To),
            SalonName = salon, GeneratedAt = now, GeneratedBy = user,
            FileName = single ? $"Komisi-{Safe(r.EmployeeFilterName!)}-{Stamp(r.From, r.To)}" : $"Komisi-{Stamp(r.From, r.To)}",
            Summary =
            [
                new("Total komisi", Rp(r.TotalCommission)),
                new("Nilai jasa", Rp(r.TotalValue)),
                new("Client", r.Capsters.Sum(c => c.Clients).ToString()),
                new("Jasa dikerjakan", r.Capsters.Sum(c => c.Services).ToString()),
            ],
            Sections =
            [
                new ReportSection
                {
                    Title = "Ringkasan per capster",
                    Columns =
                    [
                        new("Capster", Width: 24), new("Client", ReportValueKind.Integer, 9), new("Jasa", ReportValueKind.Integer, 9),
                        new("Nilai jasa", ReportValueKind.Money, 15), new("Komisi", ReportValueKind.Money, 15),
                    ],
                    Rows = r.Capsters.Select(c => new object?[] { c.EmployeeName, c.Clients, c.Services, c.Value, c.Commission }).ToList(),
                    Totals = ["Total", r.Capsters.Sum(c => c.Clients), r.Capsters.Sum(c => c.Services), r.TotalValue, r.TotalCommission],
                },
                new ReportSection
                {
                    Title = "Rincian per nota",
                    Columns =
                    [
                        new("Waktu", ReportValueKind.DateTime, 17), new("No. Nota", Width: 16), new("Customer", Width: 22),
                        new("Capster", Width: 20), new("Jasa", Width: 22), new("Harga", ReportValueKind.Money, 13),
                        new("Komisi", ReportValueKind.Money, 13),
                    ],
                    Rows = r.Lines.Select(l => new object?[] { l.At, l.DocNo, l.Customer, l.EmployeeName, l.ServiceName, l.Price, l.Commission }).ToList(),
                    Totals = ["Total", null, null, null, null, r.Lines.Sum(l => l.Price), r.Lines.Sum(l => l.Commission)],
                },
            ],
            Notes =
            [
                "Komisi = komisi master jasa saat treatment LUNAS, untuk capster terakhir yang mengerjakan. Diskon nota tidak mengurangi komisi (K20).",
                "Treatment yang dibatalkan tidak menghasilkan komisi.",
            ],
            Signatures = single ? ["Diserahkan oleh", $"Diterima oleh ({r.EmployeeFilterName})"] : [],
        };
    }
}
