namespace MSPASalon.Core.Domain;

/// <summary>
/// Document numbering codes (BUSINESS_RULES §1). Format: YYMM/CODE/0001, reset monthly per code.
/// A treatment (process) does NOT have its own code: it reuses the registration number (rule P1).
/// </summary>
public static class DocumentType
{
    public const string Registration = "RG";
    public const string Sale = "FJ";
    public const string Purchase = "FB";
    public const string Adjustment = "AD";

    public static readonly IReadOnlyList<string> All = [Registration, Sale, Purchase, Adjustment];

    public static string Period(DateTime at) => at.ToString("yyMM", System.Globalization.CultureInfo.InvariantCulture);

    public static string Format(string code, DateTime at, long sequence) => $"{Period(at)}/{code}/{sequence:0000}";
}
