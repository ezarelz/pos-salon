namespace MSPASalon.Core.Reports;

/// <summary>Payments of a period per method (amount = amount due, so the recap equals revenue; §13a).</summary>
public sealed record PaymentMethodTotal(string Method, bool IsCash, int Count, decimal Amount);

// ------------------------------------------------------------------ 1. Rekap Kas Harian (§14 no. 1)

public sealed record CashDocument(DateTime At, string DocNo, string Kind, string Customer, string Method, string? Reference, decimal Amount, bool IsCash);

public sealed record CashDailyReport(DateTime Day, IReadOnlyList<PaymentMethodTotal> Methods, IReadOnlyList<CashDocument> Documents, int CancelledCount)
{
    public decimal Total => Methods.Sum(m => m.Amount);
    public decimal CashTotal => Methods.Where(m => m.IsCash).Sum(m => m.Amount);
    public decimal NonCashTotal => Total - CashTotal;
}

// ------------------------------------------------------------------ 2. Pendapatan (§14 no. 2)

public sealed record RevenueDay(DateTime Day, int Clients, decimal ServiceGross, decimal ServiceDiscount, int SalesCount, decimal ProductGross, decimal ProductDiscount)
{
    public decimal ServiceNet => ServiceGross - ServiceDiscount;
    public decimal ProductNet => ProductGross - ProductDiscount;
    public decimal Total => ServiceNet + ProductNet;
}

public sealed record RevenueDocument(DateTime At, string DocNo, string Kind, string Customer, decimal Gross, decimal Discount, string Method)
{
    public decimal Net => Gross - Discount;
}

public sealed record RevenueReport(DateTime From, DateTime To, IReadOnlyList<RevenueDay> Days,
    IReadOnlyList<PaymentMethodTotal> Methods, IReadOnlyList<RevenueDocument> Documents)
{
    public int Clients => Days.Sum(d => d.Clients);
    public decimal ServiceNet => Days.Sum(d => d.ServiceNet);
    public decimal ProductNet => Days.Sum(d => d.ProductNet);
    public decimal Discount => Days.Sum(d => d.ServiceDiscount + d.ProductDiscount);
    public decimal Total => ServiceNet + ProductNet;
}

// ------------------------------------------------------------------ 3. Komisi Capster (§14 no. 3, §11, K20)

public sealed record CommissionLine(DateTime At, string DocNo, string Customer, string EmployeeName, string ServiceName, decimal Price, decimal Commission);

public sealed record CommissionCapster(string EmployeeName, int Clients, int Services, decimal Value, decimal Commission);

public sealed record CommissionReport(DateTime From, DateTime To, string? EmployeeFilterName,
    IReadOnlyList<CommissionCapster> Capsters, IReadOnlyList<CommissionLine> Lines)
{
    public decimal TotalCommission => Capsters.Sum(c => c.Commission);
    public decimal TotalValue => Capsters.Sum(c => c.Value);
}

// ------------------------------------------------------------------ 4. Jasa & Margin (§14 no. 4, K21)

/// <summary>Per service, gross values (invoice discount not allocated, K21). EstimatedLines = lines posted before
/// migration 003 whose material cost is estimated from the current recipe.</summary>
public sealed record ServiceMarginRow(string ServiceCode, string ServiceName, int Count, decimal Gross, decimal Commission,
    decimal MaterialCost, int EstimatedLines)
{
    public decimal Margin => Gross - Commission - MaterialCost;
    public decimal MarginRatio => Gross == 0 ? 0 : Margin / Gross;
}

public sealed record ServiceMarginReport(DateTime From, DateTime To, IReadOnlyList<ServiceMarginRow> Rows, decimal InvoiceDiscount)
{
    public decimal Gross => Rows.Sum(r => r.Gross);
    public decimal Commission => Rows.Sum(r => r.Commission);
    public decimal MaterialCost => Rows.Sum(r => r.MaterialCost);
    public decimal Margin => Rows.Sum(r => r.Margin);
    public decimal Net => Gross - InvoiceDiscount;
    public decimal MarginAfterDiscount => Net - Commission - MaterialCost;
    public int EstimatedLines => Rows.Sum(r => r.EstimatedLines);
}

// ------------------------------------------------------------------ 5. Produk & Laba Kotor (§14 no. 5)

public sealed record ProductProfitRow(string Code, string Name, string Unit, decimal Qty, decimal Gross, decimal Discount, decimal Cogs)
{
    public decimal Net => Gross - Discount;
    public decimal Profit => Net - Cogs;
    public decimal ProfitRatio => Net == 0 ? 0 : Profit / Net;
}

public sealed record ProductProfitReport(DateTime From, DateTime To, IReadOnlyList<ProductProfitRow> Rows)
{
    public decimal Net => Rows.Sum(r => r.Net);
    public decimal Cogs => Rows.Sum(r => r.Cogs);
    public decimal Profit => Rows.Sum(r => r.Profit);
}

// ------------------------------------------------------------------ 6. Stok (§14 no. 6)

public sealed record StockRow(string Code, string Name, string Unit, bool IsSellable, bool IsConsumable, decimal Opening,
    decimal Purchase, decimal Sale, decimal Treatment, decimal Adjustment, decimal OpeningEntries, decimal UnitCost, decimal StockMin)
{
    /// <summary>Opening + purchases − sales − treatment use ± adjustments + opening-balance entries of the period.</summary>
    public decimal Closing => Opening + Purchase - Sale - Treatment + Adjustment + OpeningEntries;
    public decimal Value => Domain.Money.Round(Closing * UnitCost);
    public bool BelowMin => Closing <= StockMin && StockMin > 0;
    public bool HasMovement => Purchase != 0 || Sale != 0 || Treatment != 0 || Adjustment != 0 || OpeningEntries != 0;
}

public sealed record MaterialUsageRow(string Code, string Name, string Unit, decimal Qty, decimal Value, int Treatments);

public sealed record StockReport(DateTime From, DateTime To, IReadOnlyList<StockRow> Rows, IReadOnlyList<MaterialUsageRow> Usage)
{
    public decimal Value => Rows.Sum(r => r.Value);
}

// ------------------------------------------------------------------ 7. Pembatalan (§14 no. 7)

public sealed record CancellationRow(DateTime CancelledAt, string Kind, string DocNo, DateTime DocDate, decimal? Amount,
    string CancelledBy, string Reason);

public sealed record CancellationReport(DateTime From, DateTime To, IReadOnlyList<CancellationRow> Rows);

// ------------------------------------------------------------------ 8. Kunjungan Customer (§14 no. 8, K22, K23)

public static class VisitStatus
{
    public const string Active = "Aktif";
    public const string Remind = "Perlu diingatkan";
    public const string Inactive = "Tidak aktif";
    public const string Never = "Belum ada kunjungan";

    /// <summary>K23: Aktif &lt; activeDays · Perlu diingatkan activeDays..inactiveDays · Tidak aktif &gt; inactiveDays.</summary>
    public static string Classify(int? daysSince, int activeDays, int inactiveDays) =>
        daysSince is not { } d ? Never : d < activeDays ? Active : d <= inactiveDays ? Remind : Inactive;

    /// <summary>Sort order of the report: who to contact first.</summary>
    public static int Priority(string status) => status switch { Remind => 0, Inactive => 1, Active => 2, _ => 3 };
}

public sealed record CustomerVisitRow(long CustomerId, string Code, string Name, string? Phone, DateTime? LastVisit, int? DaysSince,
    string Status, int Visits, decimal? AverageIntervalDays, decimal TotalSpend, string? LastServices, string? LastCapsters);

public sealed record CustomerVisitReport(DateTime AsOf, int ActiveDays, int InactiveDays, IReadOnlyList<CustomerVisitRow> Rows);

public sealed record VisitHistoryRow(DateTime At, string DocNo, string Services, string Capsters, decimal Total);

public sealed record LastVisitInfo(DateTime At, int DaysAgo, string Services, string Capsters)
{
    public string Text => (DaysAgo == 0 ? "Terakhir datang hari ini" : $"Terakhir datang {DaysAgo} hari lalu ({At:dd-MM-yyyy})")
                          + $" · {Services} dengan {Capsters}";
}

/// <summary>Customer visit settings (K23, K24), stored in app_setting.</summary>
public sealed record VisitSettings(int ActiveDays = 30, int InactiveDays = 60, bool ShowLastVisitOnRegistration = true);
