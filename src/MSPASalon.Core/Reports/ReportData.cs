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
