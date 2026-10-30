using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>Master-data lookups and shared writers used inside transaction services (always on the caller's transaction).</summary>
internal static class Tx
{
    internal sealed class CustomerRow { public long Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public bool IsActive { get; set; } }
    internal sealed class ServiceRow { public long Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public long Price { get; set; } public long Commission { get; set; } public bool IsActive { get; set; } }
    internal sealed class EmployeeRow { public long Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public bool IsCapster { get; set; } public bool IsActive { get; set; } }
    internal sealed class ProductRow
    {
        public long Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Unit { get; set; } = "";
        public long CostPrice { get; set; } public long SellPrice { get; set; } public bool IsSellable { get; set; } public bool IsActive { get; set; }
        public long StockOnHandX { get; set; }
        public decimal Stock => Quantity.FromStored(StockOnHandX);
    }

    public static CustomerRow Customer(SqliteConnection c, SqliteTransaction tx, long id) =>
        c.QuerySingleOrDefault<CustomerRow>("SELECT id, code, name, is_active FROM customer WHERE id = @id", new { id }, tx)
        ?? throw new BusinessException("Customer tidak ditemukan.");

    public static ServiceRow Service(SqliteConnection c, SqliteTransaction tx, long id) =>
        c.QuerySingleOrDefault<ServiceRow>("SELECT id, code, name, price, commission, is_active FROM service WHERE id = @id", new { id }, tx)
        ?? throw new BusinessException($"Jasa (id {id}) tidak ditemukan.");

    public static EmployeeRow Employee(SqliteConnection c, SqliteTransaction tx, long id) =>
        c.QuerySingleOrDefault<EmployeeRow>("SELECT id, code, name, is_capster, is_active FROM employee WHERE id = @id", new { id }, tx)
        ?? throw new BusinessException($"Karyawan (id {id}) tidak ditemukan.");

    public static ProductRow Product(SqliteConnection c, SqliteTransaction tx, long id) =>
        c.QuerySingleOrDefault<ProductRow>("""
            SELECT id, code, name, unit, cost_price, sell_price, is_sellable, is_active, stock_on_hand_x FROM product WHERE id = @id
            """, new { id }, tx)
        ?? throw new BusinessException($"Produk (id {id}) tidak ditemukan.");

    /// <summary>Validates service + capster lines (R1) and returns master snapshots in input order.</summary>
    public static List<(ServiceRow Service, EmployeeRow Employee)> ResolveServiceLines(
        SqliteConnection c, SqliteTransaction tx, IReadOnlyList<ServiceLineInput> lines, List<string> errors)
    {
        var result = new List<(ServiceRow, EmployeeRow)>();
        if (lines.Count == 0) errors.Add("Minimal satu jasa harus dipilih.");
        for (var i = 0; i < lines.Count; i++)
        {
            var n = i + 1;
            ServiceRow s; EmployeeRow e;
            try { s = Service(c, tx, lines[i].ServiceId); }
            catch (BusinessException ex) { errors.Add($"Baris {n}: {ex.Message}"); continue; }
            if (!s.IsActive) errors.Add($"Baris {n}: jasa '{s.Name}' tidak aktif.");
            try { e = Employee(c, tx, lines[i].EmployeeId); }
            catch (BusinessException) { errors.Add($"Baris {n}: capster untuk '{s.Name}' wajib dipilih."); continue; }
            if (!e.IsActive || !e.IsCapster) errors.Add($"Baris {n}: '{e.Name}' bukan capster aktif.");
            result.Add((s, e));
        }
        return result;
    }

    /// <summary>Writes the single payment row of a document (Y1–Y5) and returns it.</summary>
    public static Payment WritePayment(SqliteConnection c, SqliteTransaction tx, string sourceType, long sourceId,
        decimal amountDue, PaymentInput input, string user, DateTime at)
    {
        var method = c.QuerySingleOrDefault<PaymentMethod>(
                "SELECT id, code, name, is_cash, is_active, sort_order FROM payment_method WHERE id = @id", new { id = input.PaymentMethodId }, tx)
            ?? throw new BusinessException("Metode bayar wajib dipilih.");
        var (received, change) = TransactionRules.ResolvePayment(method, amountDue, input.AmountReceived);
        var reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim();
        c.Execute("""
            INSERT INTO payment(source_type, source_id, payment_method_id, method_code, method_name, is_cash,
                                amount_due, amount_received, change_amount, reference, paid_at, created_by)
            VALUES (@sourceType, @sourceId, @mid, @code, @name, @cash, @due, @received, @change, @reference, @at, @user)
            """, new
        {
            sourceType, sourceId, mid = method.Id, code = method.Code, name = method.Name, cash = method.IsCash,
            due = Money.ToStored(amountDue), received = Money.ToStored(received), change = Money.ToStored(change),
            reference, at = DbTime.ToDb(at), user,
        }, tx);
        return new Payment
        {
            PaymentMethodId = method.Id, MethodCode = method.Code, MethodName = method.Name, IsCash = method.IsCash,
            AmountDue = amountDue, AmountReceived = received, ChangeAmount = change, Reference = reference, PaidAt = at,
        };
    }

    private sealed class PaymentRow
    {
        public long? PaymentMethodId { get; set; } public string MethodCode { get; set; } = ""; public string MethodName { get; set; } = "";
        public bool IsCash { get; set; } public long AmountDue { get; set; } public long AmountReceived { get; set; }
        public long ChangeAmount { get; set; } public string? Reference { get; set; } public string PaidAt { get; set; } = "";
    }

    public static Payment? LoadPayment(SqliteConnection c, string sourceType, long sourceId)
    {
        var r = c.QuerySingleOrDefault<PaymentRow>("""
            SELECT payment_method_id, method_code, method_name, is_cash, amount_due, amount_received, change_amount, reference, paid_at
            FROM payment WHERE source_type = @sourceType AND source_id = @sourceId
            """, new { sourceType, sourceId });
        return r is null ? null : new Payment
        {
            PaymentMethodId = r.PaymentMethodId, MethodCode = r.MethodCode, MethodName = r.MethodName, IsCash = r.IsCash,
            AmountDue = r.AmountDue, AmountReceived = r.AmountReceived, ChangeAmount = r.ChangeAmount, Reference = r.Reference,
            PaidAt = DbTime.FromDb(r.PaidAt),
        };
    }

    /// <summary>
    /// Removes every ledger row of a document and reverses its effect on the cached stock (cancellation, K11).
    /// Returns the resulting on-hand quantity per affected product.
    /// </summary>
    public static Dictionary<long, decimal> RemoveLedger(SqliteConnection c, SqliteTransaction tx, string sourceType, long sourceId)
    {
        var rows = c.Query<(long ProductId, long Delta)>("""
            SELECT product_id, SUM(qty_in_x - qty_out_x) FROM stock_ledger
            WHERE source_type = @sourceType AND source_id = @sourceId GROUP BY product_id
            """, new { sourceType, sourceId }, tx).ToList();
        foreach (var (productId, delta) in rows)
            c.Execute("UPDATE product SET stock_on_hand_x = stock_on_hand_x - @delta WHERE id = @productId", new { delta, productId }, tx);
        c.Execute("DELETE FROM stock_ledger WHERE source_type = @sourceType AND source_id = @sourceId", new { sourceType, sourceId }, tx);
        return rows.ToDictionary(r => r.ProductId,
            r => Quantity.FromStored(c.ExecuteScalar<long>("SELECT stock_on_hand_x FROM product WHERE id = @id", new { id = r.ProductId }, tx)));
    }

    public static (string From, string ToExclusive) Range(DateTime from, DateTime to) =>
        (DbTime.ToDb(from.Date), DbTime.ToDb(to.Date.AddDays(1)));

    /// <summary>Row of a product line with scaled quantity; mapped explicitly to avoid any floating-point conversion.</summary>
    internal sealed class ProductLineRow
    {
        public long Id { get; set; } public int LineNo { get; set; } public long? ProductId { get; set; }
        public string ProductCode { get; set; } = ""; public string ProductName { get; set; } = ""; public string Unit { get; set; } = "";
        public long QtyX { get; set; } public long Price { get; set; } public long Gross { get; set; } public long Discount { get; set; }
        public long LineTotal { get; set; } public long CostPrice { get; set; }

        public ProductLine ToEntity() => new()
        {
            Id = Id, LineNo = LineNo, ProductId = ProductId, ProductCode = ProductCode, ProductName = ProductName, Unit = Unit,
            Qty = Quantity.FromStored(QtyX), Price = Price, Gross = Gross, Discount = Discount, LineTotal = LineTotal, CostPrice = CostPrice,
        };
    }
}
