using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>Direct product sale (BUSINESS_RULES §6). Saved and posted in one step when paid.</summary>
public sealed class SaleService(DbConnectionFactory db, IClock clock) : ISaleService
{
    private sealed class HeaderRow
    {
        public long Id { get; set; } public string DocNo { get; set; } = ""; public string DocAt { get; set; } = "";
        public long? CustomerId { get; set; } public string CustomerCode { get; set; } = ""; public string CustomerName { get; set; } = "";
        public string Status { get; set; } = ""; public long Subtotal { get; set; } public long DiscountTotal { get; set; }
        public long GrandTotal { get; set; } public string? Notes { get; set; } public string? CancelReason { get; set; }
        public string CreatedBy { get; set; } = ""; public string? PaymentMethodName { get; set; } public string? LineSummary { get; set; }

        public Sale ToEntity() => new()
        {
            Id = Id, DocNo = DocNo, DocAt = DbTime.FromDb(DocAt), CustomerId = CustomerId, CustomerCode = CustomerCode,
            CustomerName = CustomerName, Status = Status, Subtotal = Subtotal, DiscountTotal = DiscountTotal, GrandTotal = GrandTotal,
            Notes = Notes, CancelReason = CancelReason, CreatedBy = CreatedBy, PaymentMethodName = PaymentMethodName,
            LineSummary = LineSummary ?? "",
        };
    }

    private const string SelectHeader = """
        SELECT s.id, s.doc_no, s.doc_at, s.customer_id, s.customer_code, s.customer_name, s.status, s.subtotal, s.discount_total,
               s.grand_total, s.notes, s.cancel_reason, s.created_by,
               (SELECT method_name FROM payment p WHERE p.source_type = 'SALE' AND p.source_id = s.id) AS payment_method_name,
               (SELECT group_concat(product_name, ', ') FROM (SELECT product_name FROM sale_line WHERE sale_id = s.id ORDER BY line_no)) AS line_summary
        FROM sale s
        """;

    public IReadOnlyList<Sale> GetList(DateTime from, DateTime to)
    {
        var (f, t) = Tx.Range(from, to);
        using var c = db.Open();
        return c.Query<HeaderRow>(SelectHeader + " WHERE s.doc_at >= @f AND s.doc_at < @t ORDER BY s.doc_at DESC, s.id DESC", new { f, t })
            .Select(r => r.ToEntity()).ToList();
    }

    public Sale? Get(long id)
    {
        using var c = db.Open();
        var s = c.QuerySingleOrDefault<HeaderRow>(SelectHeader + " WHERE s.id = @id", new { id })?.ToEntity();
        if (s is null) return null;
        s.Lines = c.Query<Tx.ProductLineRow>("""
            SELECT id, line_no, product_id, product_code, product_name, unit, qty_x, price, gross, discount, line_total, cost_price
            FROM sale_line WHERE sale_id = @id ORDER BY line_no
            """, new { id }).Select(r => r.ToEntity()).ToList();
        s.Payment = Tx.LoadPayment(c, "SALE", id);
        return s;
    }

    public PostResult Post(long customerId, IReadOnlyList<SaleLineInput> lines, string? notes, PaymentInput payment, string user)
    {
        var at = clock.Now;
        var now = DbTime.ToDb(at);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var errors = new List<string>();
        var customer = Tx.Customer(c, tx, customerId);
        if (!customer.IsActive) errors.Add($"Customer '{customer.Name}' tidak aktif.");
        if (lines.Count == 0) errors.Add("Minimal satu produk harus diisi.");

        var resolved = new List<(Tx.ProductRow P, SaleLineInput In, decimal Gross, decimal Total)>();
        for (var i = 0; i < lines.Count; i++)
        {
            Tx.ProductRow p;
            try { p = Tx.Product(c, tx, lines[i].ProductId); }
            catch (BusinessException ex) { errors.Add($"Baris {i + 1}: {ex.Message}"); continue; }
            var label = $"Baris {i + 1} ({p.Name})";
            if (!p.IsActive) errors.Add($"{label}: produk tidak aktif.");
            if (!p.IsSellable) errors.Add($"{label}: produk tidak ditandai 'Dijual'.");
            var (gross, total) = TransactionRules.SaleLine(lines[i].Qty, lines[i].Price, lines[i].Discount, label, errors);
            resolved.Add((p, lines[i], gross, total));
        }

        // Stock check per product across all lines (A7: direct sale is rejected when stock is insufficient).
        foreach (var g in resolved.GroupBy(r => r.P.Id))
        {
            var need = g.Sum(x => x.In.Qty);
            var p = g.First().P;
            if (need > p.Stock)
                errors.Add($"Stok {p.Name} tidak cukup: tersedia {Quantity.Format(p.Stock)} {p.Unit}, diminta {Quantity.Format(need)} {p.Unit}.");
        }
        if (errors.Count > 0) throw new BusinessException(errors);

        var subtotal = resolved.Sum(r => r.Gross);
        var discountTotal = resolved.Sum(r => r.In.Discount);
        var grand = subtotal - discountTotal;
        var docNo = DocumentNumberGenerator.Next(c, tx, DocumentType.Sale, at);
        var id = c.ExecuteScalar<long>("""
            INSERT INTO sale(doc_no, doc_at, customer_id, customer_code, customer_name, status, subtotal, discount_total, grand_total,
                             notes, posted_by, posted_at, created_by, created_at, updated_at)
            VALUES (@docNo, @now, @cid, @ccode, @cname, @status, @subtotal, @disc, @grand, @notes, @user, @now, @user, @now, @now)
            RETURNING id
            """, new
        {
            docNo, now, cid = customer.Id, ccode = customer.Code, cname = customer.Name, status = DocStatus.Posted,
            subtotal = Money.ToStored(subtotal), disc = Money.ToStored(discountTotal), grand = Money.ToStored(grand),
            notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), user,
        }, tx);

        var n = 1;
        foreach (var (p, input, gross, total) in resolved)
        {
            var lineId = c.ExecuteScalar<long>("""
                INSERT INTO sale_line(sale_id, line_no, product_id, product_code, product_name, unit, qty_x, price, gross, discount,
                                      line_total, cost_price)
                VALUES (@id, @n, @pid, @pcode, @pname, @unit, @qtyX, @price, @gross, @disc, @total, @cost) RETURNING id
                """, new
            {
                id, n = n++, pid = p.Id, pcode = p.Code, pname = p.Name, unit = p.Unit, qtyX = Quantity.ToStored(input.Qty),
                price = Money.ToStored(input.Price), gross = Money.ToStored(gross), disc = Money.ToStored(input.Discount),
                total = Money.ToStored(total), cost = p.CostPrice,
            }, tx);
            StockLedger.Post(c, tx, p.Id, at, StockSource.Sale, id, lineId, docNo, -input.Qty, p.CostPrice);
        }
        Tx.WritePayment(c, tx, "SALE", id, grand, payment, user, at);
        tx.Commit();
        return new PostResult(id, docNo, []);
    }

    public void Cancel(long id, string reason, string user)
    {
        var r = $"{TransactionRules.RequireReason(reason)} (oleh {user})";
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var status = c.ExecuteScalar<string?>("SELECT status FROM sale WHERE id = @id", new { id }, tx)
                     ?? throw new BusinessException("Penjualan tidak ditemukan.");
        if (status == DocStatus.Cancelled) throw new BusinessException("Penjualan sudah dibatalkan.");
        Tx.RemoveLedger(c, tx, StockSource.Sale, id);
        c.Execute("DELETE FROM payment WHERE source_type = 'SALE' AND source_id = @id", new { id }, tx);
        var at = clock.Now;
        c.Execute("""
            UPDATE sale SET status = @s, cancel_reason = @r, updated_at = @now, cancelled_by = @user, cancelled_at = @now
            WHERE id = @id
            """, new { id, s = DocStatus.Cancelled, r, now = DbTime.ToDb(at), user }, tx);
        Tx.Audit(c, tx, at, user, "CANCEL", "SALE", id,
            c.ExecuteScalar<string>("SELECT doc_no FROM sale WHERE id = @id", new { id }, tx), reason.Trim());
        tx.Commit();
    }
}

/// <summary>Purchase / goods receipt (BUSINESS_RULES §7).</summary>
public sealed class PurchaseService(DbConnectionFactory db, IClock clock) : IPurchaseService
{
    private sealed class HeaderRow
    {
        public long Id { get; set; } public string DocNo { get; set; } = ""; public string DocAt { get; set; } = "";
        public long? SupplierId { get; set; } public string SupplierCode { get; set; } = ""; public string SupplierName { get; set; } = "";
        public string? SupplierRef { get; set; } public string Status { get; set; } = ""; public long GrandTotal { get; set; }
        public string? Notes { get; set; } public string? CancelReason { get; set; } public string CreatedBy { get; set; } = "";
        public string? LineSummary { get; set; }

        public Purchase ToEntity() => new()
        {
            Id = Id, DocNo = DocNo, DocAt = DbTime.FromDb(DocAt), SupplierId = SupplierId, SupplierCode = SupplierCode,
            SupplierName = SupplierName, SupplierRef = SupplierRef, Status = Status, GrandTotal = GrandTotal, Notes = Notes,
            CancelReason = CancelReason, CreatedBy = CreatedBy, LineSummary = LineSummary ?? "",
        };
    }

    private const string SelectHeader = """
        SELECT p.id, p.doc_no, p.doc_at, p.supplier_id, p.supplier_code, p.supplier_name, p.supplier_ref, p.status, p.grand_total,
               p.notes, p.cancel_reason, p.created_by,
               (SELECT group_concat(product_name, ', ') FROM (SELECT product_name FROM purchase_line WHERE purchase_id = p.id ORDER BY line_no)) AS line_summary
        FROM purchase p
        """;

    public IReadOnlyList<Purchase> GetList(DateTime from, DateTime to)
    {
        var (f, t) = Tx.Range(from, to);
        using var c = db.Open();
        return c.Query<HeaderRow>(SelectHeader + " WHERE p.doc_at >= @f AND p.doc_at < @t ORDER BY p.doc_at DESC, p.id DESC", new { f, t })
            .Select(r => r.ToEntity()).ToList();
    }

    public Purchase? Get(long id)
    {
        using var c = db.Open();
        var p = c.QuerySingleOrDefault<HeaderRow>(SelectHeader + " WHERE p.id = @id", new { id })?.ToEntity();
        if (p is null) return null;
        p.Lines = c.Query<Tx.ProductLineRow>("""
            SELECT id, line_no, product_id, product_code, product_name, unit, qty_x, price, line_total AS gross, 0 AS discount, line_total, price AS cost_price
            FROM purchase_line WHERE purchase_id = @id ORDER BY line_no
            """, new { id }).Select(r => r.ToEntity()).ToList();
        return p;
    }

    public PostResult Post(long supplierId, string? supplierRef, IReadOnlyList<PurchaseLineInput> lines, string? notes, string user)
    {
        var at = clock.Now;
        var now = DbTime.ToDb(at);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var errors = new List<string>();
        var supplier = c.QuerySingleOrDefault<(long Id, string Code, string Name, long IsActive)>(
            "SELECT id, code, name, is_active FROM supplier WHERE id = @supplierId", new { supplierId }, tx);
        if (supplier.Name is null) errors.Add("Supplier wajib dipilih.");
        else if (supplier.IsActive != 1) errors.Add($"Supplier '{supplier.Name}' tidak aktif.");
        if (lines.Count == 0) errors.Add("Minimal satu produk harus diisi.");

        var resolved = new List<(Tx.ProductRow P, PurchaseLineInput In, decimal Total)>();
        for (var i = 0; i < lines.Count; i++)
        {
            Tx.ProductRow p;
            try { p = Tx.Product(c, tx, lines[i].ProductId); }
            catch (BusinessException ex) { errors.Add($"Baris {i + 1}: {ex.Message}"); continue; }
            var (_, total) = TransactionRules.SaleLine(lines[i].Qty, lines[i].Price, 0, $"Baris {i + 1} ({p.Name})", errors);
            resolved.Add((p, lines[i], total));
        }
        if (errors.Count > 0) throw new BusinessException(errors);

        var grand = resolved.Sum(r => r.Total);
        var docNo = DocumentNumberGenerator.Next(c, tx, DocumentType.Purchase, at);
        var id = c.ExecuteScalar<long>("""
            INSERT INTO purchase(doc_no, doc_at, supplier_id, supplier_code, supplier_name, supplier_ref, status, grand_total, notes,
                                 posted_by, posted_at, created_by, created_at, updated_at)
            VALUES (@docNo, @now, @sid, @scode, @sname, @sref, @status, @grand, @notes, @user, @now, @user, @now, @now) RETURNING id
            """, new
        {
            docNo, now, sid = supplier.Id, scode = supplier.Code, sname = supplier.Name,
            sref = string.IsNullOrWhiteSpace(supplierRef) ? null : supplierRef.Trim(), status = DocStatus.Posted,
            grand = Money.ToStored(grand), notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), user,
        }, tx);
        var n = 1;
        foreach (var (p, input, total) in resolved)
        {
            var lineId = c.ExecuteScalar<long>("""
                INSERT INTO purchase_line(purchase_id, line_no, product_id, product_code, product_name, unit, qty_x, price, line_total)
                VALUES (@id, @n, @pid, @pcode, @pname, @unit, @qtyX, @price, @total) RETURNING id
                """, new
            {
                id, n = n++, pid = p.Id, pcode = p.Code, pname = p.Name, unit = p.Unit, qtyX = Quantity.ToStored(input.Qty),
                price = Money.ToStored(input.Price), total = Money.ToStored(total),
            }, tx);
            // A8: purchase price is recorded on the ledger; the master cost price is NOT updated automatically.
            StockLedger.Post(c, tx, p.Id, at, StockSource.Purchase, id, lineId, docNo, input.Qty, input.Price);
        }
        tx.Commit();
        return new PostResult(id, docNo, []);
    }

    public void Cancel(long id, string reason, string user)
    {
        var r = $"{TransactionRules.RequireReason(reason)} (oleh {user})";
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var status = c.ExecuteScalar<string?>("SELECT status FROM purchase WHERE id = @id", new { id }, tx)
                     ?? throw new BusinessException("Pembelian tidak ditemukan.");
        if (status == DocStatus.Cancelled) throw new BusinessException("Pembelian sudah dibatalkan.");
        var after = Tx.RemoveLedger(c, tx, StockSource.Purchase, id);
        var negative = after.Where(kv => kv.Value < 0).Select(kv => kv.Key).ToList();
        if (negative.Count > 0)
        {
            var names = c.Query<string>("SELECT name FROM product WHERE id IN @negative", new { negative }, tx);
            throw new BusinessException($"Pembelian tidak bisa dibatalkan karena stok akan minus: {string.Join(", ", names)}. " +
                                        "Barang sudah terpakai/terjual; lakukan stock adjustment bila perlu.");
            // the transaction is disposed without commit → nothing changes
        }
        var at = clock.Now;
        c.Execute("""
            UPDATE purchase SET status = @s, cancel_reason = @r, updated_at = @now, cancelled_by = @user, cancelled_at = @now
            WHERE id = @id
            """, new { id, s = DocStatus.Cancelled, r, now = DbTime.ToDb(at), user }, tx);
        Tx.Audit(c, tx, at, user, "CANCEL", "PURCHASE", id,
            c.ExecuteScalar<string>("SELECT doc_no FROM purchase WHERE id = @id", new { id }, tx), reason.Trim());
        tx.Commit();
    }
}

/// <summary>Stock adjustment / opname (BUSINESS_RULES §8): signed quantities valued at cost price.</summary>
public sealed class AdjustmentService(DbConnectionFactory db, IClock clock) : IAdjustmentService
{
    private sealed class HeaderRow
    {
        public long Id { get; set; } public string DocNo { get; set; } = ""; public string DocAt { get; set; } = "";
        public string Reason { get; set; } = ""; public string Status { get; set; } = ""; public long TotalValue { get; set; }
        public string? CancelReason { get; set; } public string CreatedBy { get; set; } = ""; public string? LineSummary { get; set; }

        public StockAdjustment ToEntity() => new()
        {
            Id = Id, DocNo = DocNo, DocAt = DbTime.FromDb(DocAt), Reason = Reason, Status = Status, TotalValue = TotalValue,
            CancelReason = CancelReason, CreatedBy = CreatedBy, LineSummary = LineSummary ?? "",
        };
    }

    private const string SelectHeader = """
        SELECT a.id, a.doc_no, a.doc_at, a.reason, a.status, a.total_value, a.cancel_reason, a.created_by,
               (SELECT group_concat(product_name, ', ') FROM (SELECT product_name FROM stock_adjustment_line WHERE adjustment_id = a.id ORDER BY line_no)) AS line_summary
        FROM stock_adjustment a
        """;

    public IReadOnlyList<StockAdjustment> GetList(DateTime from, DateTime to)
    {
        var (f, t) = Tx.Range(from, to);
        using var c = db.Open();
        return c.Query<HeaderRow>(SelectHeader + " WHERE a.doc_at >= @f AND a.doc_at < @t ORDER BY a.doc_at DESC, a.id DESC", new { f, t })
            .Select(r => r.ToEntity()).ToList();
    }

    public StockAdjustment? Get(long id)
    {
        using var c = db.Open();
        var a = c.QuerySingleOrDefault<HeaderRow>(SelectHeader + " WHERE a.id = @id", new { id })?.ToEntity();
        if (a is null) return null;
        a.Lines = c.Query<Tx.ProductLineRow>("""
            SELECT id, line_no, product_id, product_code, product_name, unit, qty_x, cost_price AS price, 0 AS gross, 0 AS discount,
                   line_value AS line_total, cost_price
            FROM stock_adjustment_line WHERE adjustment_id = @id ORDER BY line_no
            """, new { id }).Select(r => r.ToEntity()).ToList();
        return a;
    }

    public PostResult Post(string reason, IReadOnlyList<AdjustmentLineInput> lines, string user)
    {
        var at = clock.Now;
        var now = DbTime.ToDb(at);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3) errors.Add("Alasan adjustment wajib diisi (mis. 'Stock opname Oktober').");
        if (lines.Count == 0) errors.Add("Minimal satu produk harus diisi.");

        var resolved = new List<(Tx.ProductRow P, decimal Qty, decimal Value)>();
        for (var i = 0; i < lines.Count; i++)
        {
            Tx.ProductRow p;
            try { p = Tx.Product(c, tx, lines[i].ProductId); }
            catch (BusinessException ex) { errors.Add($"Baris {i + 1}: {ex.Message}"); continue; }
            var q = lines[i].Qty;
            if (q == 0) errors.Add($"Baris {i + 1} ({p.Name}): qty tidak boleh 0 (isi + untuk tambah, − untuk kurang).");
            else if (!Quantity.HasValidPrecision(q)) errors.Add($"Baris {i + 1} ({p.Name}): qty maksimal {Quantity.Decimals} desimal.");
            resolved.Add((p, q, Money.LineGross(q, p.CostPrice)));
        }
        // An adjustment corrects stock to reality; it must not leave a product with negative stock.
        foreach (var g in resolved.GroupBy(r => r.P.Id))
        {
            var p = g.First().P;
            var after = p.Stock + g.Sum(x => x.Qty);
            if (after < 0) errors.Add($"Stok {p.Name} akan menjadi {Quantity.Format(after)} {p.Unit} (minus).");
        }
        if (errors.Count > 0) throw new BusinessException(errors);

        var docNo = DocumentNumberGenerator.Next(c, tx, DocumentType.Adjustment, at);
        var id = c.ExecuteScalar<long>("""
            INSERT INTO stock_adjustment(doc_no, doc_at, reason, status, total_value, posted_by, posted_at, created_by, created_at, updated_at)
            VALUES (@docNo, @now, @reason, @status, @total, @user, @now, @user, @now, @now) RETURNING id
            """, new { docNo, now, reason = reason.Trim(), status = DocStatus.Posted, total = Money.ToStored(resolved.Sum(r => r.Value)), user }, tx);
        var n = 1;
        foreach (var (p, qty, value) in resolved)
        {
            var lineId = c.ExecuteScalar<long>("""
                INSERT INTO stock_adjustment_line(adjustment_id, line_no, product_id, product_code, product_name, unit, qty_x, cost_price, line_value)
                VALUES (@id, @n, @pid, @pcode, @pname, @unit, @qtyX, @cost, @value) RETURNING id
                """, new { id, n = n++, pid = p.Id, pcode = p.Code, pname = p.Name, unit = p.Unit, qtyX = Quantity.ToStored(qty),
                           cost = p.CostPrice, value = Money.ToStored(value) }, tx);
            StockLedger.Post(c, tx, p.Id, at, StockSource.Adjustment, id, lineId, docNo, qty, p.CostPrice, reason.Trim());
        }
        tx.Commit();
        return new PostResult(id, docNo, []);
    }

    public void Cancel(long id, string reason, string user)
    {
        var r = $"{TransactionRules.RequireReason(reason)} (oleh {user})";
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var status = c.ExecuteScalar<string?>("SELECT status FROM stock_adjustment WHERE id = @id", new { id }, tx)
                     ?? throw new BusinessException("Adjustment tidak ditemukan.");
        if (status == DocStatus.Cancelled) throw new BusinessException("Adjustment sudah dibatalkan.");
        var after = Tx.RemoveLedger(c, tx, StockSource.Adjustment, id);
        if (after.Any(kv => kv.Value < 0))
            throw new BusinessException("Adjustment tidak bisa dibatalkan karena stok salah satu produk akan menjadi minus.");
        var at = clock.Now;
        c.Execute("""
            UPDATE stock_adjustment SET status = @s, cancel_reason = @r, updated_at = @now, cancelled_by = @user, cancelled_at = @now
            WHERE id = @id
            """, new { id, s = DocStatus.Cancelled, r, now = DbTime.ToDb(at), user }, tx);
        Tx.Audit(c, tx, at, user, "CANCEL", "ADJUSTMENT", id,
            c.ExecuteScalar<string>("SELECT doc_no FROM stock_adjustment WHERE id = @id", new { id }, tx), reason.Trim());
        tx.Commit();
    }
}

/// <summary>Stock card (kartu stok) straight from the ledger with running balance.</summary>
public sealed class StockCardService(DbConnectionFactory db) : IStockCardService
{
    private sealed class Row
    {
        public string MovedAt { get; set; } = ""; public string DocNo { get; set; } = ""; public string SourceType { get; set; } = "";
        public long QtyInX { get; set; } public long QtyOutX { get; set; } public long UnitCost { get; set; } public string? Notes { get; set; }
    }

    public StockCard Get(long productId, DateTime from, DateTime to)
    {
        var product = new ProductService(db, new SystemClock()).Get(productId) ?? throw new BusinessException("Produk tidak ditemukan.");
        var (f, t) = Tx.Range(from, to);
        using var c = db.Open();
        var opening = Quantity.FromStored(c.ExecuteScalar<long>(
            "SELECT COALESCE(SUM(qty_in_x - qty_out_x), 0) FROM stock_ledger WHERE product_id = @productId AND moved_at < @f",
            new { productId, f }));
        var balance = opening;
        var rows = new List<StockCardRow>();
        foreach (var r in c.Query<Row>("""
                     SELECT moved_at, doc_no, source_type, qty_in_x, qty_out_x, unit_cost, notes FROM stock_ledger
                     WHERE product_id = @productId AND moved_at >= @f AND moved_at < @t ORDER BY moved_at, id
                     """, new { productId, f, t }))
        {
            var qin = Quantity.FromStored(r.QtyInX);
            var qout = Quantity.FromStored(r.QtyOutX);
            balance += qin - qout;
            rows.Add(new StockCardRow
            {
                MovedAt = DbTime.FromDb(r.MovedAt), DocNo = r.DocNo, SourceType = r.SourceType, QtyIn = qin, QtyOut = qout,
                Balance = balance, UnitCost = r.UnitCost, Notes = r.Notes,
            });
        }
        return new StockCard { Product = product, From = from.Date, To = to.Date, Opening = opening, Rows = rows, Closing = balance };
    }
}
