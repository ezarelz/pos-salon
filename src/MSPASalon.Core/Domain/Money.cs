using System.Globalization;

namespace MSPASalon.Core.Domain;

/// <summary>
/// Money rules (BUSINESS_RULES §1): whole Rupiah only, arithmetic in <see cref="decimal"/>,
/// stored in SQLite as INTEGER. Never use double/REAL for money.
/// </summary>
public static class Money
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static bool IsWholeRupiah(decimal amount) => decimal.Truncate(amount) == amount;

    /// <summary>Converts to the stored integer. Rejects fractional Rupiah instead of silently rounding.</summary>
    public static long ToStored(decimal amount)
    {
        if (!IsWholeRupiah(amount))
            throw new ArgumentException($"Nilai uang {amount} harus Rupiah bulat.", nameof(amount));
        return (long)amount;
    }

    /// <summary>Stored integer Rupiah → decimal.</summary>
    public static decimal FromStored(long stored) => stored;

    /// <summary>
    /// Rounds a computed value (e.g. qty × price with a fractional qty) to whole Rupiah,
    /// half away from zero. This is the ONLY place money is rounded.
    /// </summary>
    public static decimal Round(decimal amount) => decimal.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>Gross line value = round(qty × price).</summary>
    public static decimal LineGross(decimal qty, decimal price) => Round(qty * price);

    /// <summary>
    /// Parses user input in Indonesian style: "28.000", "28000", "Rp 28.000" → 28000.
    /// Dots and commas used as thousands separators are ignored; a decimal part ("28.000,50") is rejected.
    /// </summary>
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("Rp", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = s.Replace(" ", "");
        if (s.Length == 0) return false;

        // A comma followed by 1-2 digits at the end means cents -> not allowed.
        var lastComma = s.LastIndexOf(',');
        if (lastComma >= 0 && s.Length - lastComma - 1 is 1 or 2) return false;

        var negative = s.StartsWith('-');
        if (negative) s = s[1..];
        s = s.Replace(".", "").Replace(",", "");
        if (s.Length == 0 || !s.All(char.IsAsciiDigit)) return false;
        if (!decimal.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;
        amount = negative ? -value : value;
        return true;
    }

    public static string Format(decimal amount) => amount.ToString("N0", Id);
}
