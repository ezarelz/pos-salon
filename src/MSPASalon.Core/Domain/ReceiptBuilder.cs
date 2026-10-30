using System.Globalization;
using MSPASalon.Core.Entities;

namespace MSPASalon.Core.Domain;

/// <summary>One printed item: "Name x Qty" on the left, Gross (qty × price) on the right, optional discount line below.</summary>
public sealed record ReceiptLine(string Name, decimal Qty, decimal Gross, decimal Discount)
{
    public decimal Total => Gross - Discount;
}

/// <summary>Receipt logo rules (the image itself is printed by the WPF print service above the text).</summary>
public static class ReceiptLogo
{
    public const int MaxBytes = 1024 * 1024;

    public static bool IsSupportedImage(byte[] b) =>
        (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||   // PNG
        (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) ||                  // JPEG
        (b.Length > 2 && b[0] == 0x42 && b[1] == 0x4D);                                      // BMP
}

/// <summary>Everything printed on a receipt (nota). Built from a treatment or a sale.</summary>
public sealed class ReceiptData
{
    public required Branch Branch { get; init; }
    public required string Title { get; init; }
    public required string DocNo { get; init; }
    public DateTime At { get; init; }
    public string Cashier { get; init; } = "";
    public string Customer { get; init; } = "";
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Total { get; init; }
    public Payment? Payment { get; init; }
    public bool IsCancelled { get; init; }
    public IReadOnlyList<string> ExtraLines { get; init; } = [];
}

/// <summary>
/// Plain-text receipt for thermal printers (monospace): 32 characters per line on 58 mm paper,
/// 48 on 80 mm (decision K15). Pure function, unit-tested; the WPF layer only prints the lines.
/// </summary>
public static class ReceiptBuilder
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static int CharsPerLine(int paperWidthMm) => paperWidthMm >= 80 ? 48 : 32;

    public static IReadOnlyList<string> Build(ReceiptData d, int width)
    {
        if (width < 24) throw new ArgumentOutOfRangeException(nameof(width));
        var o = new List<string>();
        var rule = new string('-', width);

        foreach (var l in Wrap(d.Branch.Name.ToUpperInvariant(), width)) o.Add(Center(l, width));
        if (!string.IsNullOrWhiteSpace(d.Branch.Address))
            foreach (var l in Wrap(d.Branch.Address!, width)) o.Add(Center(l, width));
        if (!string.IsNullOrWhiteSpace(d.Branch.Phone)) o.Add(Center($"Telp {d.Branch.Phone}", width));
        o.Add(rule);
        o.Add(Center(d.Title.ToUpperInvariant(), width));
        if (d.IsCancelled) o.Add(Center("*** DIBATALKAN ***", width));
        o.Add(Field("No", d.DocNo, width));
        o.Add(Field("Tgl", d.At.ToString("dd-MM-yyyy HH:mm", Id), width));
        if (!string.IsNullOrWhiteSpace(d.Cashier)) o.Add(Field("Kasir", d.Cashier, width));
        if (!string.IsNullOrWhiteSpace(d.Customer)) o.Add(Field("Cust", d.Customer, width));
        o.Add(rule);

        foreach (var line in d.Lines)
        {
            o.AddRange(ItemLines($"{line.Name} x {Quantity.Format(line.Qty)}", Money.Format(line.Gross), width));
            if (line.Discount > 0) o.Add(LeftRight("  Diskon", "-" + Money.Format(line.Discount), width));
        }
        o.Add(rule);

        if (d.Discount > 0)
        {
            o.Add(LeftRight("Subtotal", Money.Format(d.Subtotal), width));
            o.Add(LeftRight("Diskon", "-" + Money.Format(d.Discount), width));
        }
        o.Add(LeftRight("TOTAL", Money.Format(d.Total), width));
        if (d.Payment is { } p)
        {
            o.Add(LeftRight(p.MethodName, Money.Format(p.AmountReceived), width));
            if (p.IsCash) o.Add(LeftRight("Kembali", Money.Format(p.ChangeAmount), width));
            if (!string.IsNullOrWhiteSpace(p.Reference)) o.Add(Field("Ref", p.Reference!, width));
        }
        foreach (var extra in d.ExtraLines) foreach (var l in Wrap(extra, width)) o.Add(l);
        o.Add(rule);
        var footer = string.IsNullOrWhiteSpace(d.Branch.ReceiptFooter) ? "Terima kasih atas kunjungan Anda" : d.Branch.ReceiptFooter!;
        foreach (var raw in footer.Replace("\r", "").Split('\n'))
            foreach (var l in Wrap(raw, width)) o.Add(Center(l, width));
        return o;
    }

    /// <summary>
    /// "Barber x 1        60.000". When the text does not fit next to the amount it wraps; the amount is printed
    /// on the last line of the item so it stays right-aligned with the other amounts.
    /// </summary>
    public static IEnumerable<string> ItemLines(string text, string amount, int width)
    {
        var room = width - amount.Length - 1;
        if (text.Length <= room) { yield return LeftRight(text, amount, width); yield break; }
        var wrapped = Wrap(text, width).ToList();
        var last = wrapped[^1];
        if (last.Length <= room)
        {
            foreach (var l in wrapped.Take(wrapped.Count - 1)) yield return l;
            yield return LeftRight(last, amount, width);
        }
        else
        {
            foreach (var l in wrapped) yield return l;
            yield return LeftRight("", amount, width);
        }
    }

    private static string Field(string label, string value, int width) => Truncate($"{label,-5}: {value}", width);

    public static string Center(string text, int width)
    {
        text = Truncate(text.Trim(), width);
        var pad = (width - text.Length) / 2;
        return new string(' ', pad) + text;
    }

    /// <summary>Left text and right-aligned amount on one line; the left part is truncated if needed.</summary>
    public static string LeftRight(string left, string right, int width)
    {
        var room = width - right.Length - 1;
        if (room < 1) return Truncate(right, width);
        left = Truncate(left, room);
        return left + new string(' ', width - left.Length - right.Length) + right;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public static IEnumerable<string> Wrap(string text, int width)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = "";
        foreach (var w0 in words)
        {
            var w = w0;
            while (w.Length > width) // very long token
            {
                if (line.Length > 0) { yield return line; line = ""; }
                yield return w[..width];
                w = w[width..];
            }
            if (line.Length == 0) line = w;
            else if (line.Length + 1 + w.Length <= width) line += " " + w;
            else { yield return line; line = w; }
        }
        if (line.Length > 0) yield return line;
    }

    // ------------------------------------------------------------------ adapters

    public static ReceiptData ForTreatment(Treatment t, Branch branch)
    {
        // Capster names are not printed; the same service at the same price is merged into one "x qty" line.
        var lines = t.Lines
            .GroupBy(l => (l.ServiceName, l.Price))
            .Select(g => new ReceiptLine(g.Key.ServiceName, g.Count(), g.Key.Price * g.Count(), 0))
            .ToList();
        return new ReceiptData
        {
            Branch = branch, Title = "Faktur Penjualan", DocNo = t.DocNo, At = t.PostedAt ?? t.TimeIn,
            Cashier = t.PostedBy ?? t.CreatedBy, Customer = t.CustomerName, Lines = lines,
            Subtotal = t.Subtotal, Discount = t.Discount, Total = t.GrandTotal, Payment = t.Payment,
            IsCancelled = t.Status == TreatmentStatus.Cancelled,
        };
    }

    public static ReceiptData ForSale(Sale s, Branch branch)
    {
        var lines = s.Lines.Select(l => new ReceiptLine(l.ProductName, l.Qty, l.LineTotal + l.Discount, l.Discount)).ToList();
        return new ReceiptData
        {
            Branch = branch, Title = "Nota Penjualan", DocNo = s.DocNo, At = s.DocAt, Cashier = s.CreatedBy,
            Customer = s.CustomerName, Lines = lines, Subtotal = s.Subtotal, Discount = 0, Total = s.GrandTotal,
            Payment = s.Payment, IsCancelled = s.Status == DocStatus.Cancelled,
            ExtraLines = s.DiscountTotal > 0 ? [$"Total diskon: {Money.Format(s.DiscountTotal)}"] : [],
        };
    }
}
