using System.Globalization;

namespace MSPASalon.Core.Reports;

public enum PeriodPreset
{
    Custom, Today, Yesterday, ThisWeek, ThisMonth, LastMonth,
    FirstHalfThisMonth, SecondHalfThisMonth, FirstHalfLastMonth, SecondHalfLastMonth,
}

/// <summary>Quick period choices of the report filter (K25). Dates only; the query covers From 00:00 to To 23:59:59 (L2).</summary>
public static class ReportPeriods
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");
    public const int MaxDays = 366;

    public static readonly IReadOnlyList<PeriodPreset> General =
        [PeriodPreset.Today, PeriodPreset.Yesterday, PeriodPreset.ThisWeek, PeriodPreset.ThisMonth, PeriodPreset.LastMonth, PeriodPreset.Custom];

    public static readonly IReadOnlyList<PeriodPreset> Commission =
    [
        PeriodPreset.FirstHalfThisMonth, PeriodPreset.SecondHalfThisMonth, PeriodPreset.ThisMonth,
        PeriodPreset.FirstHalfLastMonth, PeriodPreset.SecondHalfLastMonth, PeriodPreset.LastMonth, PeriodPreset.Custom,
    ];

    public static string Label(PeriodPreset p) => p switch
    {
        PeriodPreset.Today => "Hari ini",
        PeriodPreset.Yesterday => "Kemarin",
        PeriodPreset.ThisWeek => "Minggu ini",
        PeriodPreset.ThisMonth => "Bulan ini",
        PeriodPreset.LastMonth => "Bulan lalu",
        PeriodPreset.FirstHalfThisMonth => "Tgl 1–15 bulan ini",
        PeriodPreset.SecondHalfThisMonth => "Tgl 16–akhir bulan ini",
        PeriodPreset.FirstHalfLastMonth => "Tgl 1–15 bulan lalu",
        PeriodPreset.SecondHalfLastMonth => "Tgl 16–akhir bulan lalu",
        _ => "Pilih tanggal",
    };

    /// <summary>Date range of a preset relative to <paramref name="today"/>. Weeks start on Monday.</summary>
    public static (DateTime From, DateTime To) Range(PeriodPreset p, DateTime today)
    {
        var d = today.Date;
        var monthStart = new DateTime(d.Year, d.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        return p switch
        {
            PeriodPreset.Yesterday => (d.AddDays(-1), d.AddDays(-1)),
            PeriodPreset.ThisWeek => (d.AddDays(-(((int)d.DayOfWeek + 6) % 7)), d),
            PeriodPreset.ThisMonth => (monthStart, monthStart.AddMonths(1).AddDays(-1)),
            PeriodPreset.LastMonth => (lastMonthStart, monthStart.AddDays(-1)),
            PeriodPreset.FirstHalfThisMonth => (monthStart, monthStart.AddDays(14)),
            PeriodPreset.SecondHalfThisMonth => (monthStart.AddDays(15), monthStart.AddMonths(1).AddDays(-1)),
            PeriodPreset.FirstHalfLastMonth => (lastMonthStart, lastMonthStart.AddDays(14)),
            PeriodPreset.SecondHalfLastMonth => (lastMonthStart.AddDays(15), monthStart.AddDays(-1)),
            _ => (d, d),
        };
    }

    /// <summary>"31 Oktober 2026" or "1–15 Oktober 2026" or "28 September 2026 – 4 Oktober 2026".</summary>
    public static string Text(DateTime from, DateTime to)
    {
        from = from.Date; to = to.Date;
        if (from == to) return from.ToString("d MMMM yyyy", Id);
        if (from.Year == to.Year && from.Month == to.Month) return $"{from.Day}–{to.ToString("d MMMM yyyy", Id)}";
        return $"{from.ToString("d MMMM yyyy", Id)} – {to.ToString("d MMMM yyyy", Id)}";
    }

    /// <summary>Returns an error text, or null when the range is valid.</summary>
    public static string? Validate(DateTime from, DateTime to) =>
        to.Date < from.Date ? "Tanggal akhir tidak boleh sebelum tanggal awal."
        : (to.Date - from.Date).TotalDays + 1 > MaxDays ? $"Periode laporan maksimal {MaxDays} hari."
        : null;
}
