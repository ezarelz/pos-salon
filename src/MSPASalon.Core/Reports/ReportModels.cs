using System.Globalization;
using MSPASalon.Core.Domain;

namespace MSPASalon.Core.Reports;

/// <summary>How a report cell is typed: drives on-screen formatting, Excel number formats and print alignment.</summary>
public enum ReportValueKind { Text, Integer, Money, Qty, Date, DateTime, Time, Percent }

/// <summary>Column of a report table. <paramref name="Width"/> is the Excel column width in characters (print uses it as a ratio).</summary>
public sealed record ReportColumn(string Header, ReportValueKind Kind = ReportValueKind.Text, double Width = 14)
{
    public bool IsNumeric => Kind is ReportValueKind.Integer or ReportValueKind.Money or ReportValueKind.Qty or ReportValueKind.Percent;
}

/// <summary>
/// One table of a report. Rows hold raw typed values (string, int, decimal, DateTime; Percent as a fraction 0..1),
/// never pre-formatted text, so Excel receives real numbers and dates.
/// </summary>
public sealed class ReportSection
{
    public required string Title { get; init; }
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required IReadOnlyList<object?[]> Rows { get; init; }
    public object?[]? Totals { get; init; }
    public string EmptyText { get; init; } = "Tidak ada data pada periode ini.";
}

public sealed record ReportSummaryItem(string Label, string Value);

/// <summary>Complete, presentation-independent report: shown on screen, exported to .xlsx and printed (A4 / PDF).</summary>
public sealed class ReportDocument
{
    public required string Title { get; init; }
    public required string PeriodText { get; init; }
    public string SalonName { get; init; } = "";
    public DateTime GeneratedAt { get; init; }
    public string GeneratedBy { get; init; } = "";
    public IReadOnlyList<ReportSummaryItem> Summary { get; init; } = [];
    public IReadOnlyList<ReportSection> Sections { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    /// <summary>Signature boxes printed at the end (e.g. commission slip: "Diserahkan oleh", "Diterima oleh").</summary>
    public IReadOnlyList<string> Signatures { get; init; } = [];
    /// <summary>Suggested file name without extension (safe characters only).</summary>
    public required string FileName { get; init; }

    public string GeneratedText => $"Dicetak {GeneratedAt.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)}"
                                   + (string.IsNullOrWhiteSpace(GeneratedBy) ? "" : $" oleh {GeneratedBy}");
}

/// <summary>Text formatting of report values (screen and print). Excel uses native number formats instead.</summary>
public static class ReportFormat
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static string Format(object? value, ReportValueKind kind) => value switch
    {
        null => "",
        string s => s,
        DateTime d => kind switch
        {
            ReportValueKind.Date => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            ReportValueKind.Time => d.ToString("HH:mm", CultureInfo.InvariantCulture),
            _ => d.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture),
        },
        _ when kind == ReportValueKind.Money => Money.Format(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        _ when kind == ReportValueKind.Qty => Quantity.Format(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        _ when kind == ReportValueKind.Percent =>
            (Convert.ToDecimal(value, CultureInfo.InvariantCulture) * 100).ToString("0.#", Id) + "%",
        _ when kind == ReportValueKind.Integer => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("#,0", Id),
        _ => Convert.ToString(value, Id) ?? "",
    };

    public static string Rp(decimal amount) => $"Rp {Money.Format(amount)}";
}
