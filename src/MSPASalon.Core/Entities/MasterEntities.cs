namespace MSPASalon.Core.Entities;

/// <summary>Common shape of every master record: surrogate id + unique business code + display name.</summary>
public interface IMasterEntity
{
    long Id { get; set; }
    string Code { get; set; }
    string Name { get; set; }
}

public static class Gender
{
    public const string Male = "L";
    public const string Female = "P";

    public static string Label(string? g) => g switch { Male => "Laki-laki", Female => "Perempuan", _ => "-" };
}

public sealed class Customer : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Gender { get; set; }
    public bool IsMember { get; set; }
    public bool IsWalkIn { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public sealed class Supplier : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Category : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class Employee : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Gender { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsCapster { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Product : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    /// <summary>Stock unit, e.g. BTL, PCS, BOX.</summary>
    public string Unit { get; set; } = "PCS";
    /// <summary>Optional informational content per stock unit, e.g. 200 (ml). Not used in stock arithmetic.</summary>
    public decimal? ContentQty { get; set; }
    public string? ContentUnit { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal StockMin { get; set; }
    public decimal StockMax { get; set; }
    public bool IsSellable { get; set; } = true;
    public bool IsConsumable { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Cached on-hand stock. The stock ledger is the source of truth (BUSINESS_RULES §10).</summary>
    public decimal StockOnHand { get; set; }
}

public sealed class Service : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    /// <summary>Fixed commission (Rupiah) per service performed.</summary>
    public decimal Commission { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ServiceMaterial> Materials { get; set; } = [];
    public int MaterialCount { get; set; }
}

/// <summary>Recipe line: consumption of one consumable product per 1 customer, in the product's stock unit (K9).</summary>
public sealed class ServiceMaterial
{
    public long Id { get; set; }
    public long ServiceId { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? ContentQty { get; set; }
    public string? ContentUnit { get; set; }
    public decimal Qty { get; set; }
}

public sealed class PaymentMethod : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Cash: change is calculated. Non-cash: amount received always equals the total (K8).</summary>
    public bool IsCash { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class Branch
{
    public string ShortName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? ReceiptFooter { get; set; }
    public int PaperWidthMm { get; set; } = 58;
}
