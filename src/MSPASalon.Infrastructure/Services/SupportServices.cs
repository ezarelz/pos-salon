using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Validation;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

public sealed class BranchService(DbConnectionFactory db) : IBranchService
{
    public Branch Get()
    {
        using var c = db.Open();
        return c.QuerySingle<Branch>("SELECT short_name, name, address, phone, receipt_footer, paper_width_mm FROM branch WHERE id = 1");
    }

    public void Save(Branch b)
    {
        b.Name = (b.Name ?? "").Trim();
        b.ShortName = (b.ShortName ?? "").Trim();
        var errors = MasterValidators.Validate(b);
        if (errors.Count > 0) throw new BusinessException(errors);
        using var c = db.Open();
        c.Execute("""
            UPDATE branch SET short_name = @ShortName, name = @Name, address = @Address, phone = @Phone,
                   receipt_footer = @ReceiptFooter, paper_width_mm = @PaperWidthMm WHERE id = 1
            """, new
        {
            b.ShortName, b.Name, Address = Clean(b.Address), Phone = Clean(b.Phone),
            ReceiptFooter = Clean(b.ReceiptFooter), b.PaperWidthMm,
        });
    }

    public byte[]? GetLogo()
    {
        using var c = db.Open();
        return c.ExecuteScalar<byte[]?>("SELECT logo FROM branch WHERE id = 1");
    }

    public void SaveLogo(byte[]? image)
    {
        if (image is not null)
        {
            if (image.Length > ReceiptLogo.MaxBytes)
                throw new BusinessException($"Ukuran logo maksimal {ReceiptLogo.MaxBytes / 1024} KB. Perkecil gambar terlebih dahulu.");
            if (!ReceiptLogo.IsSupportedImage(image))
                throw new BusinessException("Format logo harus PNG, JPG, atau BMP.");
        }
        using var c = db.Open();
        c.Execute("UPDATE branch SET logo = @image WHERE id = 1", new { image });
    }

    private const string KeyActive = "customer.visit.active_days", KeyInactive = "customer.visit.inactive_days",
        KeyShowLast = "registration.show_last_visit";

    public MSPASalon.Core.Reports.VisitSettings GetVisitSettings()
    {
        using var c = db.Open();
        var kv = c.Query<(string Key, string Value)>("SELECT key, value FROM app_setting WHERE key IN @keys",
            new { keys = new[] { KeyActive, KeyInactive, KeyShowLast } }).ToDictionary(x => x.Key, x => x.Value);
        var d = new MSPASalon.Core.Reports.VisitSettings();
        int Int(string key, int fallback) => kv.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;
        return new MSPASalon.Core.Reports.VisitSettings(Int(KeyActive, d.ActiveDays), Int(KeyInactive, d.InactiveDays),
            kv.TryGetValue(KeyShowLast, out var show) ? show == "1" : d.ShowLastVisitOnRegistration);
    }

    public void SaveVisitSettings(MSPASalon.Core.Reports.VisitSettings s)
    {
        if (s.ActiveDays < 1 || s.InactiveDays > 3650 || s.ActiveDays >= s.InactiveDays)
            throw new BusinessException("Batas hari kunjungan: 'Aktif' minimal 1 hari dan harus lebih kecil dari 'Tidak aktif' (maksimal 3650 hari).");
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        foreach (var (key, value) in new[] { (KeyActive, s.ActiveDays.ToString()), (KeyInactive, s.InactiveDays.ToString()),
                     (KeyShowLast, s.ShowLastVisitOnRegistration ? "1" : "0") })
            c.Execute("INSERT INTO app_setting(key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                new { key, value }, tx);
        tx.Commit();
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>Online backup through the SQLite backup API: consistent even while the app is running (WAL).</summary>
public sealed class BackupService(DbConnectionFactory db, IClock clock) : IBackupService
{
    public string DatabasePath => db.DatabasePath;

    public string Backup(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var file = Path.Combine(targetDirectory, $"mspasalon_{clock.Now:yyyyMMdd_HHmmss}.db");
        using var source = db.Open();
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
        {
            target.Open();
            source.BackupDatabase(target);
        }
        return file;
    }
}

public sealed class DashboardService(DbConnectionFactory db) : IDashboardService
{
    private sealed class LowRow
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public long StockOnHandX { get; set; }
        public long StockMinX { get; set; }
    }

    private sealed class MethodRow
    {
        public string MethodName { get; set; } = "";
        public long IsCash { get; set; }
        public int Cnt { get; set; }
        public long Amount { get; set; }
    }

    private sealed class DocRow
    {
        public string At { get; set; } = "";
        public string DocNo { get; set; } = "";
        public string Kind { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public long Total { get; set; }
        public string? MethodName { get; set; }
    }

    private sealed class CapsterRow
    {
        public string Name { get; set; } = "";
        public int Clients { get; set; }
        public int Services { get; set; }
        public long Amount { get; set; }
        public long Commission { get; set; }
    }

    public DashboardSummary GetSummary(DateTime day)
    {
        var (from, to) = DayRange(day.Date);
        var (prevFrom, prevTo) = DayRange(day.Date.AddDays(-1));
        using var c = db.Open();

        long Sum(string sql, string f, string t) => c.ExecuteScalar<long>(sql, new { f, t });
        const string serviceSql = "SELECT COALESCE(SUM(grand_total), 0) FROM treatment WHERE status = 'LUNAS' AND posted_at >= @f AND posted_at < @t";
        const string productSql = "SELECT COALESCE(SUM(grand_total), 0) FROM sale WHERE status = 'POSTED' AND posted_at >= @f AND posted_at < @t";

        var methods = c.Query<MethodRow>("""
                SELECT method_name AS MethodName, MAX(is_cash) AS IsCash, COUNT(*) AS Cnt, SUM(amount_due) AS Amount
                FROM payment WHERE paid_at >= @f AND paid_at < @t
                GROUP BY method_name ORDER BY SUM(amount_due) DESC, method_name
                """, new { f = from, t = to })
            .Select(r => new DashboardMethodTotal(r.MethodName, r.IsCash == 1, r.Cnt, Money.FromStored(r.Amount)))
            .ToList();

        var recent = c.Query<DocRow>("""
                SELECT d.at AS At, d.doc_no AS DocNo, d.kind AS Kind, d.customer_name AS CustomerName, d.total AS Total,
                       p.method_name AS MethodName
                FROM (
                    SELECT t.posted_at AS at, t.doc_no, 'Treatment' AS kind, r.customer_name, t.grand_total AS total,
                           'TREATMENT' AS src, t.id AS src_id
                    FROM treatment t JOIN registration r ON r.id = t.registration_id
                    WHERE t.status = 'LUNAS' AND t.posted_at >= @f AND t.posted_at < @t
                    UNION ALL
                    SELECT s.posted_at, s.doc_no, 'Penjualan', s.customer_name, s.grand_total, 'SALE', s.id
                    FROM sale s WHERE s.status = 'POSTED' AND s.posted_at >= @f AND s.posted_at < @t
                ) d
                LEFT JOIN payment p ON p.source_type = d.src AND p.source_id = d.src_id
                ORDER BY d.at DESC, d.doc_no DESC LIMIT 10
                """, new { f = from, t = to })
            .Select(r => new DashboardDocument(DbTime.FromDb(r.At), r.DocNo, r.Kind, r.CustomerName, Money.FromStored(r.Total), r.MethodName ?? "-"))
            .ToList();

        var capsters = c.Query<CapsterRow>("""
                SELECT l.employee_name AS Name, COUNT(DISTINCT l.treatment_id) AS Clients, COUNT(*) AS Services, SUM(l.price) AS Amount,
                       SUM(l.commission) AS Commission
                FROM treatment_line l JOIN treatment t ON t.id = l.treatment_id
                WHERE t.status = 'LUNAS' AND t.posted_at >= @f AND t.posted_at < @t
                GROUP BY l.employee_name ORDER BY Clients DESC, Amount DESC, Name
                """, new { f = from, t = to })
            .Select(r => new DashboardCapster(r.Name, r.Clients, r.Services, Money.FromStored(r.Amount), Money.FromStored(r.Commission)))
            .ToList();

        var low = c.Query<LowRow>("""
                SELECT id, code, name, unit, stock_on_hand_x, stock_min_x FROM product
                WHERE is_active = 1 AND stock_on_hand_x <= stock_min_x
                ORDER BY (stock_on_hand_x - stock_min_x), code LIMIT 50
                """)
            .Select(r => new Product
            {
                Id = r.Id, Code = r.Code, Name = r.Name, Unit = r.Unit,
                StockOnHand = Quantity.FromStored(r.StockOnHandX), StockMin = Quantity.FromStored(r.StockMinX),
            }).ToList();

        var cancelled = c.ExecuteScalar<int>("""
            SELECT (SELECT COUNT(*) FROM treatment WHERE status = 'BATAL' AND updated_at >= @f AND updated_at < @t)
                 + (SELECT COUNT(*) FROM sale WHERE status = 'BATAL' AND updated_at >= @f AND updated_at < @t)
            """, new { f = from, t = to });

        return new DashboardSummary(
            day.Date,
            Money.FromStored(Sum(serviceSql, from, to)),
            Money.FromStored(Sum(productSql, from, to)),
            Money.FromStored(Sum(serviceSql, prevFrom, prevTo) + Sum(productSql, prevFrom, prevTo)),
            c.ExecuteScalar<int>("SELECT COUNT(*) FROM treatment WHERE status = 'LUNAS' AND posted_at >= @f AND posted_at < @t", new { f = from, t = to }),
            c.ExecuteScalar<int>("SELECT COUNT(*) FROM sale WHERE status = 'POSTED' AND posted_at >= @f AND posted_at < @t", new { f = from, t = to }),
            c.ExecuteScalar<int>("SELECT COUNT(*) FROM registration WHERE status = 'MENUNGGU'"),
            c.ExecuteScalar<int>("SELECT COUNT(*) FROM treatment WHERE status = 'DIKERJAKAN'"),
            cancelled, methods, recent, capsters, low);
    }

    private static (string From, string To) DayRange(DateTime day) => (DbTime.ToDb(day), DbTime.ToDb(day.AddDays(1)));
}

/// <summary>
/// Single entry point for every stock movement (BUSINESS_RULES §10). Each call writes one ledger row and
/// updates the product's cached on-hand quantity in the SAME transaction.
/// </summary>
public static class StockLedger
{
    public static void Post(SqliteConnection c, SqliteTransaction tx, long productId, DateTime at, string sourceType,
        long sourceId, long sourceLineId, string docNo, decimal signedQty, decimal unitCost, string? notes = null)
    {
        if (signedQty == 0) return;
        var qtyX = Quantity.ToStored(Math.Abs(signedQty));
        c.Execute("""
            INSERT INTO stock_ledger(product_id, moved_at, source_type, source_id, source_line_id, doc_no,
                                     qty_in_x, qty_out_x, unit_cost, notes)
            VALUES (@productId, @at, @sourceType, @sourceId, @sourceLineId, @docNo, @inX, @outX, @cost, @notes)
            """, new
        {
            productId, at = DbTime.ToDb(at), sourceType, sourceId, sourceLineId, docNo,
            inX = signedQty > 0 ? qtyX : 0, outX = signedQty < 0 ? qtyX : 0,
            cost = Money.ToStored(Money.Round(unitCost)), notes,
        }, tx);
        c.Execute("UPDATE product SET stock_on_hand_x = stock_on_hand_x + @d WHERE id = @productId",
            new { d = signedQty > 0 ? qtyX : -qtyX, productId }, tx);
    }

    /// <summary>On-hand quantity recomputed from the ledger (the source of truth).</summary>
    public static decimal LedgerBalance(SqliteConnection c, long productId, SqliteTransaction? tx = null) =>
        Quantity.FromStored(c.ExecuteScalar<long>(
            "SELECT COALESCE(SUM(qty_in_x - qty_out_x), 0) FROM stock_ledger WHERE product_id = @productId", new { productId }, tx));

    /// <summary>Rebuilds every cached on-hand quantity from the ledger. Returns the number of products corrected.</summary>
    public static int RebuildCache(SqliteConnection c, SqliteTransaction tx) =>
        c.Execute("""
            UPDATE product SET stock_on_hand_x =
                (SELECT COALESCE(SUM(qty_in_x - qty_out_x), 0) FROM stock_ledger l WHERE l.product_id = product.id)
            WHERE stock_on_hand_x <>
                (SELECT COALESCE(SUM(qty_in_x - qty_out_x), 0) FROM stock_ledger l WHERE l.product_id = product.id)
            """, transaction: tx);
}
