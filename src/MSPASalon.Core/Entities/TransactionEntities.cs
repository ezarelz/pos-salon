namespace MSPASalon.Core.Entities;

public static class RegistrationStatus
{
    public const string Waiting = "MENUNGGU";
    public const string InProcess = "DIPROSES";
    public const string Done = "SELESAI";
    public const string Cancelled = "BATAL";
}

public static class TreatmentStatus
{
    public const string InProgress = "DIKERJAKAN";
    public const string Paid = "LUNAS";
    public const string Cancelled = "BATAL";
}

/// <summary>Status of sale / purchase / stock adjustment documents.</summary>
public static class DocStatus
{
    public const string Draft = "DRAFT";
    public const string Posted = "POSTED";
    public const string Cancelled = "BATAL";
}

/// <summary>Stock ledger source types (column stock_ledger.source_type).</summary>
public static class StockSource
{
    public const string Opening = "OPENING";
    public const string Purchase = "PURCHASE";
    public const string Sale = "SALE";
    public const string Treatment = "TREATMENT";
    public const string Adjustment = "ADJUSTMENT";

    public static string Label(string source) => source switch
    {
        Opening => "Saldo awal",
        Purchase => "Pembelian",
        Sale => "Penjualan",
        Treatment => "Pemakaian bahan (treatment)",
        Adjustment => "Adjustment",
        _ => source,
    };
}

/// <summary>Service + capster on a registration or treatment (snapshot of master data at the time).</summary>
public sealed class ServiceLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public long? ServiceId { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public decimal Price { get; set; }
    public long? EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    /// <summary>Treatment lines only: commission snapshot taken at posting (§11).</summary>
    public decimal Commission { get; set; }
}

public sealed class Registration
{
    public long Id { get; set; }
    public string RegNo { get; set; } = "";
    public DateTime RegAt { get; set; }
    public long? CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? Notes { get; set; }
    public string Status { get; set; } = RegistrationStatus.Waiting;
    public string? CancelReason { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<ServiceLine> Lines { get; set; } = [];
    /// <summary>List views: "Potong Rambut (Sandi), Creambath (Rina)".</summary>
    public string ServiceSummary { get; set; } = "";
    public decimal Total { get; set; }
    public long? TreatmentId { get; set; }
}

public sealed class Payment
{
    public long? PaymentMethodId { get; set; }
    public string MethodCode { get; set; } = "";
    public string MethodName { get; set; } = "";
    public bool IsCash { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountReceived { get; set; }
    public decimal ChangeAmount { get; set; }
    public string? Reference { get; set; }
    public DateTime PaidAt { get; set; }
}

public sealed class Treatment
{
    public long Id { get; set; }
    public long RegistrationId { get; set; }
    public string DocNo { get; set; } = "";
    public DateTime TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public string Status { get; set; } = TreatmentStatus.InProgress;
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal GrandTotal { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CancelReason { get; set; }
    public string? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<ServiceLine> Lines { get; set; } = [];
    public Payment? Payment { get; set; }
    public string ServiceSummary { get; set; } = "";
    public string? PaymentMethodName { get; set; }

    public TimeSpan? Duration => TimeOut.HasValue ? TimeOut.Value - TimeIn : null;
}

public sealed class ProductLine
{
    public long Id { get; set; }
    public int LineNo { get; set; }
    public long? ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    /// <summary>Adjustment lines: signed (+ in, − out). Sale/purchase lines: always &gt; 0.</summary>
    public decimal Qty { get; set; }
    /// <summary>Selling price (sale), purchase price (purchase) or cost price (adjustment).</summary>
    public decimal Price { get; set; }
    public decimal Gross { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal CostPrice { get; set; }
}

public sealed class Sale
{
    public long Id { get; set; }
    public string DocNo { get; set; } = "";
    public DateTime DocAt { get; set; }
    public long? CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = DocStatus.Posted;
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<ProductLine> Lines { get; set; } = [];
    public Payment? Payment { get; set; }
    public string? PaymentMethodName { get; set; }
    public string LineSummary { get; set; } = "";
}

public sealed class Purchase
{
    public long Id { get; set; }
    public string DocNo { get; set; } = "";
    public DateTime DocAt { get; set; }
    public long? SupplierId { get; set; }
    public string SupplierCode { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string? SupplierRef { get; set; }
    public string Status { get; set; } = DocStatus.Posted;
    public decimal GrandTotal { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<ProductLine> Lines { get; set; } = [];
    public string LineSummary { get; set; } = "";
}

public sealed class StockAdjustment
{
    public long Id { get; set; }
    public string DocNo { get; set; } = "";
    public DateTime DocAt { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = DocStatus.Posted;
    public decimal TotalValue { get; set; }
    public string? CancelReason { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<ProductLine> Lines { get; set; } = [];
    public string LineSummary { get; set; } = "";
}

/// <summary>Planned consumption of one product when a treatment is posted (backflush preview).</summary>
public sealed class BackflushItem
{
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal StockOnHand { get; set; }
    public decimal UnitCost { get; set; }
    public decimal StockAfter => StockOnHand - Qty;
    public bool IsShort => StockAfter < 0;
    public decimal Value => Domain.Money.LineGross(Qty, UnitCost);
}

public sealed class StockCardRow
{
    public DateTime MovedAt { get; set; }
    public string DocNo { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string SourceLabel => StockSource.Label(SourceType);
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
    public decimal Balance { get; set; }
    public decimal UnitCost { get; set; }
    public string? Notes { get; set; }
}

public sealed class StockCard
{
    public required Product Product { get; init; }
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public decimal Opening { get; init; }
    public required IReadOnlyList<StockCardRow> Rows { get; init; }
    public decimal Closing { get; init; }
}

// ------------------------------------------------------------------ inputs

public sealed record ServiceLineInput(long ServiceId, long EmployeeId);
public sealed record SaleLineInput(long ProductId, decimal Qty, decimal Price, decimal Discount);
public sealed record PurchaseLineInput(long ProductId, decimal Qty, decimal Price);
public sealed record AdjustmentLineInput(long ProductId, decimal Qty);
public sealed record PaymentInput(long PaymentMethodId, decimal AmountReceived, string? Reference);

/// <summary>Result of posting a document.</summary>
public sealed record PostResult(long Id, string DocNo, IReadOnlyList<string> Warnings);
