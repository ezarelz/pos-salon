using System.Globalization;

namespace MSPASalon.Core.Domain;

/// <summary>
/// Quantity rules (BUSINESS_RULES §1): maximum 4 decimal places, stored in SQLite as an
/// INTEGER scaled by 10,000 (e.g. 0,1000 BTL is stored as 1000). This removes every
/// floating-point error from stock arithmetic.
/// </summary>
public static class Quantity
{
    public const int Decimals = 4;
    public const long Scale = 10_000;

    /// <summary>Converts a domain quantity to its stored integer form. Rejects more than 4 decimals.</summary>
    public static long ToStored(decimal qty)
    {
        if (!HasValidPrecision(qty))
            throw new ArgumentException($"Kuantitas {qty} melebihi {Decimals} angka desimal.", nameof(qty));
        return (long)(qty * Scale);
    }

    public static decimal FromStored(long stored) => (decimal)stored / Scale;

    public static bool HasValidPrecision(decimal qty) => decimal.Round(qty, Decimals) == qty;

    /// <summary>
    /// Parses user input. Quantity fields never use a thousands separator, so both ',' and '.'
    /// are accepted as the decimal separator ("0,1" = "0.1" = 0.1). This fixes the legacy bug
    /// where "1.5" was read as 15. Returns false for empty, invalid or over-precise input.
    /// </summary>
    public static bool TryParse(string? text, out decimal qty)
    {
        qty = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace(" ", "");
        if (s.Count(c => c is ',' or '.') > 1) return false;
        s = s.Replace(',', '.');
        if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) return false;
        if (!HasValidPrecision(value)) return false;
        qty = value;
        return true;
    }

    /// <summary>Formats with Indonesian decimal comma, no grouping, trailing zeros trimmed (0,1 / 2 / 0,0125).</summary>
    public static string Format(decimal qty)
    {
        var s = qty.ToString("0.####", CultureInfo.InvariantCulture);
        return s.Replace('.', ',');
    }
}
