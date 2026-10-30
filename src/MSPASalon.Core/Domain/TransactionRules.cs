using MSPASalon.Core.Entities;

namespace MSPASalon.Core.Domain;

/// <summary>
/// Pure calculation and validation rules for transactions (BUSINESS_RULES §4–§9).
/// No database access, so every rule is unit-tested directly.
/// </summary>
public static class TransactionRules
{
    /// <summary>
    /// Sale line (rule S2): qty &gt; 0 (max 4 decimals), price ≥ 0, 0 ≤ discount ≤ gross.
    /// gross = round(qty × price); total = gross − discount.
    /// </summary>
    public static (decimal Gross, decimal Total) SaleLine(decimal qty, decimal price, decimal discount, string label, List<string> errors)
    {
        if (qty <= 0) errors.Add($"{label}: qty harus lebih dari 0.");
        else if (!Quantity.HasValidPrecision(qty)) errors.Add($"{label}: qty maksimal {Quantity.Decimals} desimal.");
        if (price < 0 || !Money.IsWholeRupiah(price)) errors.Add($"{label}: harga harus Rupiah bulat dan tidak negatif.");
        if (discount < 0 || !Money.IsWholeRupiah(discount)) errors.Add($"{label}: diskon harus Rupiah bulat dan tidak negatif.");
        var gross = qty > 0 && price >= 0 ? Money.LineGross(qty, price) : 0m;
        if (discount > gross) errors.Add($"{label}: diskon ({Money.Format(discount)}) melebihi nilai baris ({Money.Format(gross)}).");
        return (gross, Math.Max(0, gross - discount));
    }

    /// <summary>Treatment discount (rule P4): nominal on the header, 0 ≤ discount ≤ subtotal.</summary>
    public static decimal TreatmentTotal(decimal subtotal, decimal discount, List<string> errors)
    {
        if (discount < 0 || !Money.IsWholeRupiah(discount)) errors.Add("Diskon harus Rupiah bulat dan tidak negatif.");
        else if (discount > subtotal) errors.Add($"Diskon ({Money.Format(discount)}) melebihi subtotal ({Money.Format(subtotal)}).");
        return Math.Max(0, subtotal - Math.Max(0, discount));
    }

    /// <summary>
    /// Payment (rules Y2–Y4, decision K8). Cash: received ≥ due, change = received − due.
    /// Non-cash (QRIS/transfer): recorded at exactly the amount due, no change; the received input is ignored.
    /// Throws <see cref="BusinessException"/> on underpayment or an inactive method.
    /// </summary>
    public static (decimal Received, decimal Change) ResolvePayment(PaymentMethod method, decimal amountDue, decimal amountReceived)
    {
        if (!method.IsActive) throw new BusinessException($"Metode bayar '{method.Name}' tidak aktif.");
        if (amountDue < 0) throw new BusinessException("Total tagihan tidak boleh negatif.");
        if (!method.IsCash) return (amountDue, 0m);
        if (!Money.IsWholeRupiah(amountReceived)) throw new BusinessException("Uang diterima harus Rupiah bulat.");
        if (amountReceived < amountDue)
            throw new BusinessException($"Uang diterima ({Money.Format(amountReceived)}) kurang dari total ({Money.Format(amountDue)}).");
        return (amountReceived, amountReceived - amountDue);
    }

    /// <summary>
    /// Backflush (rules B1–B3): total consumption per product = Σ recipe qty of every service line.
    /// A service appearing twice consumes its recipe twice. Services without a recipe consume nothing.
    /// </summary>
    public static Dictionary<long, decimal> Backflush(IEnumerable<long> serviceIdsOfLines,
        IReadOnlyDictionary<long, IReadOnlyList<(long ProductId, decimal Qty)>> recipes)
    {
        var result = new Dictionary<long, decimal>();
        foreach (var serviceId in serviceIdsOfLines)
        {
            if (!recipes.TryGetValue(serviceId, out var recipe)) continue;
            foreach (var (productId, qty) in recipe)
                result[productId] = result.GetValueOrDefault(productId) + qty;
        }
        return result;
    }

    /// <summary>Cancellation reason is mandatory (K11).</summary>
    public static string RequireReason(string? reason)
    {
        var r = (reason ?? "").Trim();
        if (r.Length < 3) throw new BusinessException("Alasan pembatalan wajib diisi (minimal 3 karakter).");
        return r;
    }
}
