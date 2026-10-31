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

    // ------------------------------------------------------------------ 4. Jasa & Margin (K21)

    public static ReportDocument ServiceMargin(ServiceMarginReport r, string salon, string user, DateTime now)
    {
        var notes = new List<string>
        {
            "Nilai per jasa adalah bruto (harga jasa); diskon nota tidak dialokasikan ke jasa (K21) dan hanya dikurangkan pada total.",
            "Biaya bahan = resep × harga pokok saat treatment LUNAS (snapshot).",
        };
        if (r.EstimatedLines > 0)
            notes.Add($"{r.EstimatedLines} baris jasa diposting sebelum pembaruan ini; biaya bahannya diestimasi dari resep dan harga pokok saat ini (kolom 'Estimasi').");
        return new ReportDocument
        {
            Title = "Laporan Jasa & Margin",
            PeriodText = ReportPeriods.Text(r.From, r.To),
            SalonName = salon, GeneratedAt = now, GeneratedBy = user,
            FileName = $"Jasa-Margin-{Stamp(r.From, r.To)}",
            Summary =
            [
                new("Nilai jasa (bruto)", Rp(r.Gross)),
                new("Diskon nota", Rp(r.InvoiceDiscount)),
                new("Komisi", Rp(r.Commission)),
                new("Biaya bahan", Rp(r.MaterialCost)),
                new("Margin bruto", Rp(r.Margin)),
                new("Margin setelah diskon", Rp(r.MarginAfterDiscount)),
            ],
            Sections =
            [
                new ReportSection
                {
                    Title = "Per jasa",
                    Columns =
                    [
                        new("Kode", Width: 10), new("Jasa", Width: 24), new("Dikerjakan", ReportValueKind.Integer, 11),
                        new("Nilai bruto", ReportValueKind.Money, 14), new("Komisi", ReportValueKind.Money, 13),
                        new("Biaya bahan", ReportValueKind.Money, 13), new("Margin", ReportValueKind.Money, 14),
                        new("Margin %", ReportValueKind.Percent, 10), new("Estimasi", ReportValueKind.Integer, 9),
                    ],
                    Rows = r.Rows.Select(x => new object?[]
                        { x.ServiceCode, x.ServiceName, x.Count, x.Gross, x.Commission, x.MaterialCost, x.Margin, x.MarginRatio,
                          x.EstimatedLines == 0 ? null : x.EstimatedLines }).ToList(),
                    Totals = ["Total", null, r.Rows.Sum(x => x.Count), r.Gross, r.Commission, r.MaterialCost, r.Margin,
                        r.Gross == 0 ? 0m : r.Margin / r.Gross, r.EstimatedLines == 0 ? null : r.EstimatedLines],
                },
                new ReportSection
                {
                    Title = "Rekonsiliasi",
                    Columns = [new("Keterangan", Width: 34), new("Jumlah", ReportValueKind.Money, 16)],
                    Rows =
                    [
                        ["Nilai jasa bruto", r.Gross], ["Diskon nota", -r.InvoiceDiscount], ["Pendapatan jasa neto", r.Net],
                        ["Komisi", -r.Commission], ["Biaya bahan", -r.MaterialCost], ["Margin setelah diskon", r.MarginAfterDiscount],
                    ],
                },
            ],
            Notes = notes,
        };
    }

    // ------------------------------------------------------------------ 5. Produk & Laba Kotor

    public static ReportDocument ProductProfit(ProductProfitReport r, string salon, string user, DateTime now) => new()
    {
        Title = "Laporan Produk & Laba Kotor",
        PeriodText = ReportPeriods.Text(r.From, r.To),
        SalonName = salon, GeneratedAt = now, GeneratedBy = user,
        FileName = $"Produk-Laba-{Stamp(r.From, r.To)}",
        Summary =
        [
            new("Penjualan neto", Rp(r.Net)),
            new("HPP", Rp(r.Cogs)),
            new("Laba kotor", Rp(r.Profit)),
            new("Margin", r.Net == 0 ? "0%" : ReportFormat.Format(r.Profit / r.Net, ReportValueKind.Percent)),
        ],
        Sections =
        [
            new ReportSection
            {
                Title = "Per produk",
                Columns =
                [
                    new("Kode", Width: 10), new("Produk", Width: 24), new("Satuan", Width: 8), new("Qty", ReportValueKind.Qty, 9),
                    new("Bruto", ReportValueKind.Money, 13), new("Diskon", ReportValueKind.Money, 12), new("Neto", ReportValueKind.Money, 13),
                    new("HPP", ReportValueKind.Money, 13), new("Laba kotor", ReportValueKind.Money, 13), new("Margin %", ReportValueKind.Percent, 10),
                ],
                Rows = r.Rows.Select(x => new object?[]
                    { x.Code, x.Name, x.Unit, x.Qty, x.Gross, x.Discount, x.Net, x.Cogs, x.Profit, x.ProfitRatio }).ToList(),
                Totals = ["Total", null, null, null, r.Rows.Sum(x => x.Gross), r.Rows.Sum(x => x.Discount), r.Net, r.Cogs, r.Profit,
                    r.Net == 0 ? 0m : r.Profit / r.Net],
            },
        ],
        Notes = ["Hanya penjualan produk langsung (POSTED). HPP = qty × harga pokok saat transaksi (snapshot). Pemakaian bahan treatment ada di Laporan Stok."],
    };

    // ------------------------------------------------------------------ 6. Stok

    public const long StockAll = 0, StockConsumable = 1, StockSellable = 2, StockBelowMin = 3;

    public static ReportDocument Stock(StockReport r, long filter, string salon, string user, DateTime now)
    {
        var rows = r.Rows.Where(x => filter switch
        {
            StockConsumable => x.IsConsumable,
            StockSellable => x.IsSellable,
            StockBelowMin => x.BelowMin,
            _ => true,
        }).ToList();
        return new ReportDocument
        {
            Title = "Laporan Stok",
            PeriodText = ReportPeriods.Text(r.From, r.To),
            SalonName = salon, GeneratedAt = now, GeneratedBy = user,
            FileName = $"Stok-{Stamp(r.From, r.To)}",
            Summary =
            [
                new("Nilai stok akhir", Rp(rows.Sum(x => x.Value))),
                new("Jumlah produk", rows.Count.ToString()),
                new("Di bawah minimum", rows.Count(x => x.BelowMin).ToString()),
                new("Nilai pemakaian bahan", Rp(r.Usage.Sum(u => u.Value))),
            ],
            Sections =
            [
                new ReportSection
                {
                    Title = "Mutasi & posisi stok",
                    Columns =
                    [
                        new("Kode", Width: 10), new("Produk", Width: 24), new("Satuan", Width: 8), new("Awal", ReportValueKind.Qty, 9),
                        new("Beli", ReportValueKind.Qty, 9), new("Jual", ReportValueKind.Qty, 9), new("Treatment", ReportValueKind.Qty, 10),
                        new("Adjustment", ReportValueKind.Qty, 11), new("Saldo awal", ReportValueKind.Qty, 10), new("Akhir", ReportValueKind.Qty, 9),
                        new("Harga pokok", ReportValueKind.Money, 12), new("Nilai", ReportValueKind.Money, 13), new("Status", Width: 12),
                    ],
                    Rows = rows.Select(x => new object?[]
                    {
                        x.Code, x.Name, x.Unit, x.Opening, x.Purchase, x.Sale, x.Treatment, x.Adjustment, x.OpeningEntries, x.Closing,
                        x.UnitCost, x.Value, x.Closing < 0 ? "Minus" : x.BelowMin ? "Di bawah min." : null,
                    }).ToList(),
                    Totals = ["Total", null, null, null, null, null, null, null, null, null, null, rows.Sum(x => x.Value), null],
                },
                new ReportSection
                {
                    Title = "Pemakaian bahan (backflush)",
                    Columns =
                    [
                        new("Kode", Width: 10), new("Bahan", Width: 24), new("Satuan", Width: 8), new("Terpakai", ReportValueKind.Qty, 10),
                        new("Nilai", ReportValueKind.Money, 13), new("Treatment", ReportValueKind.Integer, 10),
                    ],
                    Rows = r.Usage.Select(u => new object?[] { u.Code, u.Name, u.Unit, u.Qty, u.Value, u.Treatments }).ToList(),
                    Totals = ["Total", null, null, null, r.Usage.Sum(u => u.Value), null],
                    EmptyText = "Tidak ada pemakaian bahan pada periode ini.",
                },
            ],
            Notes =
            [
                "Akhir = Awal + Beli − Jual − Treatment ± Adjustment + Saldo awal (input saldo awal/impor). Sumber: stock ledger.",
                "Nilai stok = qty akhir × harga pokok master saat ini (standard cost, A6). Nilai pemakaian bahan memakai harga pokok saat treatment.",
                "Pembanding stock opname: bila fisik berkurang lebih banyak dari kolom 'Terpakai', takaran resep perlu ditinjau.",
            ],
        };
    }

    // ------------------------------------------------------------------ 7. Pembatalan

    public static ReportDocument Cancellations(CancellationReport r, string salon, string user, DateTime now) => new()
    {
        Title = "Laporan Pembatalan",
        PeriodText = ReportPeriods.Text(r.From, r.To),
        SalonName = salon, GeneratedAt = now, GeneratedBy = user,
        FileName = $"Pembatalan-{Stamp(r.From, r.To)}",
        Summary =
        [
            new("Dokumen dibatalkan", r.Rows.Count.ToString()),
            new("Nilai treatment & penjualan", Rp(r.Rows.Where(x => x.Kind is "Treatment" or "Penjualan").Sum(x => x.Amount ?? 0))),
        ],
        Sections =
        [
            new ReportSection
            {
                Title = "Daftar pembatalan",
                Columns =
                [
                    new("Waktu batal", ReportValueKind.DateTime, 17), new("Jenis", Width: 15), new("No. Dokumen", Width: 16),
                    new("Tgl dokumen", ReportValueKind.Date, 12), new("Nilai", ReportValueKind.Money, 13),
                    new("Dibatalkan oleh", Width: 15), new("Alasan", Width: 30),
                ],
                Rows = r.Rows.Select(x => new object?[] { x.CancelledAt, x.Kind, x.DocNo, x.DocDate, x.Amount, x.CancelledBy, x.Reason }).ToList(),
                EmptyText = "Tidak ada pembatalan pada periode ini.",
            },
        ],
        Notes =
        [
            "Berdasarkan waktu pembatalan. Nilai treatment kosong bila dibatalkan sebelum dibayar; registrasi tidak bernilai uang.",
            "Registrasi yang batal karena treatment-nya dibatalkan hanya ditampilkan sekali, sebagai treatment.",
        ],
    };

    // ------------------------------------------------------------------ 8. Kunjungan Customer (K22, K23)

    public const long VisitAll = 0, VisitRemind = 1, VisitInactive = 2, VisitActive = 3, VisitNever = 4;

    public static ReportDocument CustomerVisits(CustomerVisitReport r, long filter, string salon, string user, DateTime now)
    {
        var rows = r.Rows.Where(x => filter switch
        {
            VisitRemind => x.Status == VisitStatus.Remind,
            VisitInactive => x.Status == VisitStatus.Inactive,
            VisitActive => x.Status == VisitStatus.Active,
            VisitNever => x.Status == VisitStatus.Never,
            _ => true,
        }).ToList();
        int Count(string s) => r.Rows.Count(x => x.Status == s);
        return new ReportDocument
        {
            Title = "Laporan Kunjungan Customer",
            PeriodText = $"Per {ReportPeriods.Text(r.AsOf, r.AsOf)}",
            SalonName = salon, GeneratedAt = now, GeneratedBy = user,
            FileName = $"Kunjungan-Customer-{r.AsOf:yyyyMMdd}",
            Summary =
            [
                new("Perlu diingatkan", Count(VisitStatus.Remind).ToString()),
                new("Tidak aktif", Count(VisitStatus.Inactive).ToString()),
                new("Aktif", Count(VisitStatus.Active).ToString()),
                new("Belum ada kunjungan", Count(VisitStatus.Never).ToString()),
            ],
            Sections =
            [
                new ReportSection
                {
                    Title = "Customer",
                    Columns =
                    [
                        new("Status", Width: 16), new("Nama", Width: 22), new("No. HP", Width: 14), new("Terakhir datang", ReportValueKind.Date, 13),
                        new("Hari", ReportValueKind.Integer, 7), new("Kunjungan", ReportValueKind.Integer, 10),
                        new("Rata-rata jarak (hari)", ReportValueKind.Qty, 12), new("Total belanja", ReportValueKind.Money, 14),
                        new("Jasa terakhir", Width: 22), new("Capster terakhir", Width: 18),
                    ],
                    Rows = rows.Select(x => new object?[]
                    {
                        x.Status, x.Name, x.Phone, x.LastVisit, x.DaysSince, x.Visits, x.AverageIntervalDays, x.TotalSpend,
                        x.LastServices, x.LastCapsters,
                    }).ToList(),
                    EmptyText = "Tidak ada customer dengan status ini.",
                },
            ],
            Notes =
            [
                $"Aktif < {r.ActiveDays} hari · Perlu diingatkan {r.ActiveDays}–{r.InactiveDays} hari · Tidak aktif > {r.InactiveDays} hari sejak kunjungan terakhir (K23; ubah di Pengaturan Salon).",
                "Kunjungan = treatment LUNAS (K22). Total belanja = treatment + penjualan produk atas nama customer. Customer walk-in dan nonaktif tidak ditampilkan.",
                "Riwayat dimulai sejak aplikasi ini dipakai; transaksi aplikasi lama tidak diimpor.",
            ],
        };
    }

    /// <summary>Visit history of one customer as monospace text (Master Customer → Riwayat Kunjungan).</summary>
    public static IReadOnlyList<string> VisitHistoryText(Customer customer, IReadOnlyList<VisitHistoryRow> visits, VisitSettings settings, DateTime today)
    {
        var o = new List<string> { $"{customer.Name} ({customer.Code})" };
        if (!string.IsNullOrWhiteSpace(customer.Phone)) o.Add($"HP: {customer.Phone}");
        if (visits.Count == 0)
        {
            o.Add("");
            o.Add("Belum ada kunjungan (treatment LUNAS).");
            return o;
        }
        var days = (today.Date - visits[0].At.Date).Days;
        o.Add($"Kunjungan: {visits.Count} kali · terakhir {visits[0].At:dd-MM-yyyy} ({days} hari lalu)");
        o.Add($"Status: {VisitStatus.Classify(days, settings.ActiveDays, settings.InactiveDays)}");
        o.Add(new string('-', 48));
        foreach (var v in visits)
        {
            o.Add(ReceiptBuilder.LeftRight($"{v.At:dd-MM-yyyy HH:mm}  {v.DocNo}", Money.Format(v.Total), 48));
            foreach (var l in ReceiptBuilder.Wrap($"{v.Services} — {v.Capsters}", 46)) o.Add("  " + l);
        }
        return o;
    }
}
