using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Reports;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>Report queries (BUSINESS_RULES §14). Read-only; posted documents only, by posting time (L1, L2).</summary>
public sealed class ReportService(DbConnectionFactory db) : IReportService
{
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
        public long Gross { get; set; }
        public long Discount { get; set; }
        public long Total { get; set; }
        public string? MethodName { get; set; }
        public long? IsCash { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class DayRow
    {
        public string Day { get; set; } = "";
        public int Cnt { get; set; }
        public long Gross { get; set; }
        public long Discount { get; set; }
    }

    private sealed class CommissionRow
    {
        public string PostedAt { get; set; } = "";
        public string DocNo { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public long Price { get; set; }
        public long Commission { get; set; }
    }

    private static (string F, string T) Range(DateTime from, DateTime to) =>
        (DbTime.ToDb(from.Date), DbTime.ToDb(to.Date.AddDays(1)));

    // Posted treatments + sales of a period, with their payment. Gross/discount per document type.
    private const string DocumentsSql = """
        SELECT d.at AS At, d.doc_no AS DocNo, d.kind AS Kind, d.customer_name AS CustomerName,
               d.gross AS Gross, d.discount AS Discount, d.total AS Total,
               p.method_name AS MethodName, p.is_cash AS IsCash, p.reference AS Reference
        FROM (
            SELECT t.posted_at AS at, t.doc_no, 'Treatment' AS kind, r.customer_name, t.subtotal AS gross, t.discount,
                   t.grand_total AS total, 'TREATMENT' AS src, t.id AS src_id
            FROM treatment t JOIN registration r ON r.id = t.registration_id
            WHERE t.status = 'LUNAS' AND t.posted_at >= @f AND t.posted_at < @t
            UNION ALL
            SELECT s.posted_at, s.doc_no, 'Penjualan', s.customer_name, s.subtotal, s.discount_total, s.grand_total, 'SALE', s.id
            FROM sale s WHERE s.status = 'POSTED' AND s.posted_at >= @f AND s.posted_at < @t
        ) d
        LEFT JOIN payment p ON p.source_type = d.src AND p.source_id = d.src_id
        ORDER BY d.at, d.doc_no
        """;

    private static List<PaymentMethodTotal> Methods(SqliteConnection c, string f, string t) =>
        c.Query<MethodRow>("""
                SELECT method_name AS MethodName, MAX(is_cash) AS IsCash, COUNT(*) AS Cnt, SUM(amount_due) AS Amount
                FROM payment WHERE paid_at >= @f AND paid_at < @t
                GROUP BY method_name ORDER BY MAX(is_cash) DESC, SUM(amount_due) DESC, method_name
                """, new { f, t })
            .Select(r => new PaymentMethodTotal(r.MethodName, r.IsCash == 1, r.Cnt, Money.FromStored(r.Amount)))
            .ToList();

    public CashDailyReport GetCashDaily(DateTime day)
    {
        var (f, t) = Range(day, day);
        using var c = db.Open();
        var docs = c.Query<DocRow>(DocumentsSql, new { f, t })
            .Select(d => new CashDocument(DbTime.FromDb(d.At), d.DocNo, d.Kind, d.CustomerName, d.MethodName ?? "-", d.Reference,
                Money.FromStored(d.Total), d.IsCash == 1))
            .ToList();
        var cancelled = c.ExecuteScalar<int>("""
            SELECT (SELECT COUNT(*) FROM treatment WHERE status = 'BATAL' AND COALESCE(cancelled_at, updated_at) >= @f AND COALESCE(cancelled_at, updated_at) < @t)
                 + (SELECT COUNT(*) FROM sale WHERE status = 'BATAL' AND COALESCE(cancelled_at, updated_at) >= @f AND COALESCE(cancelled_at, updated_at) < @t)
            """, new { f, t });
        return new CashDailyReport(day.Date, Methods(c, f, t), docs, cancelled);
    }

    public RevenueReport GetRevenue(DateTime from, DateTime to)
    {
        var (f, t) = Range(from, to);
        using var c = db.Open();
        var treatments = c.Query<DayRow>("""
                SELECT substr(posted_at, 1, 10) AS Day, COUNT(*) AS Cnt, SUM(subtotal) AS Gross, SUM(discount) AS Discount
                FROM treatment WHERE status = 'LUNAS' AND posted_at >= @f AND posted_at < @t GROUP BY substr(posted_at, 1, 10)
                """, new { f, t }).ToDictionary(r => r.Day);
        var sales = c.Query<DayRow>("""
                SELECT substr(posted_at, 1, 10) AS Day, COUNT(*) AS Cnt, SUM(subtotal) AS Gross, SUM(discount_total) AS Discount
                FROM sale WHERE status = 'POSTED' AND posted_at >= @f AND posted_at < @t GROUP BY substr(posted_at, 1, 10)
                """, new { f, t }).ToDictionary(r => r.Day);

        var days = new List<RevenueDay>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd");
            treatments.TryGetValue(key, out var tr);
            sales.TryGetValue(key, out var sr);
            days.Add(new RevenueDay(d, tr?.Cnt ?? 0, Money.FromStored(tr?.Gross ?? 0), Money.FromStored(tr?.Discount ?? 0),
                sr?.Cnt ?? 0, Money.FromStored(sr?.Gross ?? 0), Money.FromStored(sr?.Discount ?? 0)));
        }

        var docs = c.Query<DocRow>(DocumentsSql, new { f, t })
            .Select(d => new RevenueDocument(DbTime.FromDb(d.At), d.DocNo, d.Kind, d.CustomerName,
                Money.FromStored(d.Gross), Money.FromStored(d.Discount), d.MethodName ?? "-"))
            .ToList();
        return new RevenueReport(from.Date, to.Date, days, Methods(c, f, t), docs);
    }

    public CommissionReport GetCommission(DateTime from, DateTime to, long? employeeId)
    {
        var (f, t) = Range(from, to);
        using var c = db.Open();
        var rows = c.Query<CommissionRow>($"""
                SELECT t.posted_at AS PostedAt, t.doc_no AS DocNo, r.customer_name AS CustomerName,
                       l.employee_name AS EmployeeName, l.service_name AS ServiceName, l.price AS Price, l.commission AS Commission
                FROM treatment_line l
                JOIN treatment t ON t.id = l.treatment_id
                JOIN registration r ON r.id = t.registration_id
                WHERE t.status = 'LUNAS' AND t.posted_at >= @f AND t.posted_at < @t
                {(employeeId is null ? "" : "AND l.employee_id = @employeeId")}
                ORDER BY l.employee_name, t.posted_at, t.doc_no, l.line_no
                """, new { f, t, employeeId }).ToList();

        var lines = rows.Select(r => new CommissionLine(DbTime.FromDb(r.PostedAt), r.DocNo, r.CustomerName, r.EmployeeName,
            r.ServiceName, Money.FromStored(r.Price), Money.FromStored(r.Commission))).ToList();
        var capsters = lines.GroupBy(l => l.EmployeeName)
            .Select(g => new CommissionCapster(g.Key, g.Select(l => l.DocNo).Distinct().Count(), g.Count(), g.Sum(l => l.Price), g.Sum(l => l.Commission)))
            .OrderByDescending(x => x.Commission).ThenBy(x => x.EmployeeName)
            .ToList();
        string? filterName = null;
        if (employeeId is { } id)
            filterName = c.ExecuteScalar<string?>("SELECT name FROM employee WHERE id = @id", new { id }) ?? lines.FirstOrDefault()?.EmployeeName ?? "?";
        return new CommissionReport(from.Date, to.Date, filterName, capsters, lines);
    }

    // ================================================================== M4b

    private sealed class MarginRow
    {
        public long? ServiceId { get; set; }
        public string ServiceCode { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public long Price { get; set; }
        public long Commission { get; set; }
        public long? MaterialCost { get; set; }
    }

    public ServiceMarginReport GetServiceMargin(DateTime from, DateTime to)
    {
        var (f, t) = Range(from, to);
        using var c = db.Open();
        var lines = c.Query<MarginRow>("""
                SELECT l.service_id AS ServiceId, l.service_code AS ServiceCode, l.service_name AS ServiceName,
                       l.price AS Price, l.commission AS Commission, l.material_cost AS MaterialCost
                FROM treatment_line l JOIN treatment t ON t.id = l.treatment_id
                WHERE t.status = 'LUNAS' AND t.posted_at >= @f AND t.posted_at < @t
                """, new { f, t }).ToList();

        // Lines posted before migration 003 have no snapshot: estimate from the CURRENT recipe and cost price (§14).
        var missing = lines.Where(l => l.MaterialCost is null && l.ServiceId is not null).Select(l => l.ServiceId!.Value).Distinct().ToArray();
        var estimates = missing.Length == 0
            ? new Dictionary<long, decimal>()
            : c.Query<(long ServiceId, long Total)>("""
                    SELECT m.service_id, SUM(m.qty_x * p.cost_price) FROM service_material m JOIN product p ON p.id = m.product_id
                    WHERE m.service_id IN @missing GROUP BY m.service_id
                    """, new { missing })
                .ToDictionary(x => x.ServiceId, x => Money.Round(Quantity.FromStored(x.Total)));

        var rows = lines
            .GroupBy(l => (l.ServiceCode, l.ServiceName))
            .Select(g => new ServiceMarginRow(g.Key.ServiceCode, g.Key.ServiceName, g.Count(),
                Money.FromStored(g.Sum(l => l.Price)), Money.FromStored(g.Sum(l => l.Commission)),
                g.Sum(l => l.MaterialCost is { } m ? Money.FromStored(m)
                    : l.ServiceId is { } sid && estimates.TryGetValue(sid, out var e) ? e : 0m),
                g.Count(l => l.MaterialCost is null)))
            .OrderByDescending(r => r.Gross).ThenBy(r => r.ServiceName)
            .ToList();
        var discount = c.ExecuteScalar<long>(
            "SELECT COALESCE(SUM(discount), 0) FROM treatment WHERE status = 'LUNAS' AND posted_at >= @f AND posted_at < @t", new { f, t });
        return new ServiceMarginReport(from.Date, to.Date, rows, Money.FromStored(discount));
    }

    private sealed class ProfitRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public long QtyX { get; set; }
        public long Gross { get; set; }
        public long Discount { get; set; }
        public long CostX { get; set; }
    }

    public ProductProfitReport GetProductProfit(DateTime from, DateTime to)
    {
        var (f, t) = Range(from, to);
        using var c = db.Open();
        var rows = c.Query<ProfitRow>("""
                SELECT l.product_code AS Code, l.product_name AS Name, l.unit AS Unit, SUM(l.qty_x) AS QtyX,
                       SUM(l.gross) AS Gross, SUM(l.discount) AS Discount, SUM(l.qty_x * l.cost_price) AS CostX
                FROM sale_line l JOIN sale s ON s.id = l.sale_id
                WHERE s.status = 'POSTED' AND s.posted_at >= @f AND s.posted_at < @t
                GROUP BY l.product_code, l.product_name, l.unit
                """, new { f, t })
            .Select(r => new ProductProfitRow(r.Code, r.Name, r.Unit, Quantity.FromStored(r.QtyX), Money.FromStored(r.Gross),
                Money.FromStored(r.Discount), Money.Round(Quantity.FromStored(r.CostX))))   // HPP = Σ qty × cost snapshot
            .OrderByDescending(r => r.Profit).ThenBy(r => r.Name)
            .ToList();
        return new ProductProfitReport(from.Date, to.Date, rows);
    }

    // ================================================================== M4c — stok

    private sealed class StockBase
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public long IsSellable { get; set; }
        public long IsConsumable { get; set; }
        public long IsActive { get; set; }
        public long CostPrice { get; set; }
        public long StockMinX { get; set; }
        public long OpeningX { get; set; }
    }

    private sealed class MoveRow
    {
        public long ProductId { get; set; }
        public string SourceType { get; set; } = "";
        public long InX { get; set; }
        public long OutX { get; set; }
    }

    private sealed class UsageRow
    {
        public long ProductId { get; set; }
        public long QtyX { get; set; }
        public long ValueX { get; set; }
        public int Treatments { get; set; }
    }

    public StockReport GetStock(DateTime from, DateTime to)
    {
        var (f, t) = Range(from, to);
        using var c = db.Open();
        var products = c.Query<StockBase>("""
                SELECT p.id AS Id, p.code AS Code, p.name AS Name, p.unit AS Unit, p.is_sellable AS IsSellable,
                       p.is_consumable AS IsConsumable, p.is_active AS IsActive, p.cost_price AS CostPrice, p.stock_min_x AS StockMinX,
                       COALESCE((SELECT SUM(qty_in_x - qty_out_x) FROM stock_ledger l WHERE l.product_id = p.id AND l.moved_at < @f), 0) AS OpeningX
                FROM product p ORDER BY p.name
                """, new { f }).ToList();
        var moves = c.Query<MoveRow>("""
                SELECT product_id AS ProductId, source_type AS SourceType, SUM(qty_in_x) AS InX, SUM(qty_out_x) AS OutX
                FROM stock_ledger WHERE moved_at >= @f AND moved_at < @t GROUP BY product_id, source_type
                """, new { f, t }).ToLookup(m => m.ProductId);

        decimal Q(long x) => Quantity.FromStored(x);
        var rows = new List<StockRow>();
        foreach (var p in products)
        {
            var m = moves[p.Id].ToDictionary(x => x.SourceType);
            decimal In(string s) => m.TryGetValue(s, out var r) ? Q(r.InX) : 0;
            decimal Out(string s) => m.TryGetValue(s, out var r) ? Q(r.OutX) : 0;
            var row = new StockRow(p.Code, p.Name, p.Unit, p.IsSellable == 1, p.IsConsumable == 1, Q(p.OpeningX),
                In("PURCHASE") - Out("PURCHASE"), Out("SALE") - In("SALE"), Out("TREATMENT") - In("TREATMENT"),
                In("ADJUSTMENT") - Out("ADJUSTMENT"), In("OPENING") - Out("OPENING"),
                Money.FromStored(p.CostPrice), Q(p.StockMinX));
            // Inactive products only when they still matter (stock or movement).
            if (p.IsActive == 1 || row.Opening != 0 || row.Closing != 0 || row.HasMovement) rows.Add(row);
        }

        var names = products.ToDictionary(p => p.Id);
        var usage = c.Query<UsageRow>("""
                SELECT product_id AS ProductId, SUM(qty_out_x) AS QtyX, SUM(qty_out_x * unit_cost) AS ValueX,
                       COUNT(DISTINCT source_id) AS Treatments
                FROM stock_ledger WHERE source_type = 'TREATMENT' AND moved_at >= @f AND moved_at < @t GROUP BY product_id
                """, new { f, t })
            .Where(u => names.ContainsKey(u.ProductId))
            .Select(u => new MaterialUsageRow(names[u.ProductId].Code, names[u.ProductId].Name, names[u.ProductId].Unit,
                Q(u.QtyX), Money.Round(Q(u.ValueX)), u.Treatments))
            .OrderByDescending(u => u.Value).ThenBy(u => u.Name)
            .ToList();
        return new StockReport(from.Date, to.Date, rows, usage);
    }

    // ================================================================== M4c — pembatalan

    private sealed class CancelRow
    {
        public string CancelledAt { get; set; } = "";
        public string Kind { get; set; } = "";
        public string DocNo { get; set; } = "";
        public string DocDate { get; set; } = "";
        public long? Amount { get; set; }
        public string? CancelledBy { get; set; }
        public string? Reason { get; set; }
    }

    private static readonly System.Text.RegularExpressions.Regex ByUser = new(@"\s*\(oleh ([^)]+)\)\s*$");

    public CancellationReport GetCancellations(DateTime from, DateTime to)
    {
        var (f, t) = Range(from, to);
        using var c = db.Open();
        // Registrations cancelled together with their treatment are reported once, as the treatment.
        var rows = c.Query<CancelRow>("""
                SELECT * FROM (
                    SELECT COALESCE(cancelled_at, updated_at) AS CancelledAt, 'Treatment' AS Kind, doc_no AS DocNo, time_in AS DocDate,
                           CASE WHEN posted_at IS NULL THEN NULL ELSE grand_total END AS Amount, cancelled_by AS CancelledBy, cancel_reason AS Reason
                    FROM treatment WHERE status = 'BATAL'
                    UNION ALL
                    SELECT COALESCE(r.cancelled_at, r.updated_at), 'Registrasi', r.reg_no, r.reg_at, NULL, r.cancelled_by, r.cancel_reason
                    FROM registration r WHERE r.status = 'BATAL' AND NOT EXISTS (SELECT 1 FROM treatment t WHERE t.registration_id = r.id)
                    UNION ALL
                    SELECT COALESCE(cancelled_at, updated_at), 'Penjualan', doc_no, doc_at, grand_total, cancelled_by, cancel_reason
                    FROM sale WHERE status = 'BATAL'
                    UNION ALL
                    SELECT COALESCE(cancelled_at, updated_at), 'Pembelian', doc_no, doc_at, grand_total, cancelled_by, cancel_reason
                    FROM purchase WHERE status = 'BATAL'
                    UNION ALL
                    SELECT COALESCE(cancelled_at, updated_at), 'Stock Adjustment', doc_no, doc_at, total_value, cancelled_by, cancel_reason
                    FROM stock_adjustment WHERE status = 'BATAL'
                ) WHERE CancelledAt >= @f AND CancelledAt < @t
                ORDER BY CancelledAt, DocNo
                """, new { f, t })
            .Select(r =>
            {
                var reason = r.Reason ?? "";
                var by = r.CancelledBy;
                var m = ByUser.Match(reason);
                if (m.Success)
                {
                    by ??= m.Groups[1].Value;          // documents cancelled before migration 003
                    reason = reason[..m.Index];
                }
                return new CancellationRow(DbTime.FromDb(r.CancelledAt), r.Kind, r.DocNo, DbTime.FromDb(r.DocDate),
                    r.Amount is { } a ? Money.FromStored(a) : null, by ?? "-", reason);
            })
            .ToList();
        return new CancellationReport(from.Date, to.Date, rows);
    }

    // ================================================================== M4c — kunjungan customer

    private sealed class VisitAgg
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public int Visits { get; set; }
        public string? FirstAt { get; set; }
        public string? LastAt { get; set; }
        public long ServiceSpend { get; set; }
    }

    private sealed class VisitLine
    {
        public long TreatmentId { get; set; }
        public long? CustomerId { get; set; }
        public string PostedAt { get; set; } = "";
        public string DocNo { get; set; } = "";
        public long Total { get; set; }
        public string ServiceName { get; set; } = "";
        public string EmployeeName { get; set; } = "";
    }

    private const string VisitLinesSql = """
        SELECT t.id AS TreatmentId, r.customer_id AS CustomerId, t.posted_at AS PostedAt, t.doc_no AS DocNo, t.grand_total AS Total,
               l.service_name AS ServiceName, l.employee_name AS EmployeeName
        FROM treatment t JOIN registration r ON r.id = t.registration_id JOIN treatment_line l ON l.treatment_id = t.id
        WHERE t.status = 'LUNAS'
        """;

    private static string Join(IEnumerable<string> names) => string.Join(", ", names.Distinct());

    public CustomerVisitReport GetCustomerVisits(DateTime asOf, int activeDays, int inactiveDays)
    {
        var t = DbTime.ToDb(asOf.Date.AddDays(1));
        using var c = db.Open();
        var agg = c.Query<VisitAgg>("""
                SELECT cu.id AS Id, cu.code AS Code, cu.name AS Name, cu.phone AS Phone,
                       COUNT(t.id) AS Visits, MIN(t.posted_at) AS FirstAt, MAX(t.posted_at) AS LastAt,
                       COALESCE(SUM(t.grand_total), 0) AS ServiceSpend
                FROM customer cu
                LEFT JOIN registration r ON r.customer_id = cu.id
                LEFT JOIN treatment t ON t.registration_id = r.id AND t.status = 'LUNAS' AND t.posted_at < @t
                WHERE cu.is_walk_in = 0 AND cu.is_active = 1
                GROUP BY cu.id, cu.code, cu.name, cu.phone
                """, new { t }).ToList();
        var productSpend = c.Query<(long CustomerId, long Total)>("""
                SELECT customer_id, SUM(grand_total) FROM sale
                WHERE status = 'POSTED' AND posted_at < @t AND customer_id IS NOT NULL GROUP BY customer_id
                """, new { t }).ToDictionary(x => x.CustomerId, x => x.Total);

        // Services and capsters of each customer's last visit.
        var lastLines = c.Query<VisitLine>(VisitLinesSql + """
                 AND t.posted_at < @t AND t.id = (
                    SELECT t2.id FROM treatment t2 JOIN registration r2 ON r2.id = t2.registration_id
                    WHERE r2.customer_id = r.customer_id AND t2.status = 'LUNAS' AND t2.posted_at < @t
                    ORDER BY t2.posted_at DESC, t2.id DESC LIMIT 1)
                ORDER BY l.line_no
                """, new { t }).ToLookup(l => l.CustomerId);

        var rows = agg.Select(a =>
            {
                DateTime? last = a.LastAt is null ? null : DbTime.FromDb(a.LastAt);
                int? days = last is { } l ? (asOf.Date - l.Date).Days : null;
                decimal? interval = a.Visits >= 2
                    ? Math.Round((decimal)(DbTime.FromDb(a.LastAt!) - DbTime.FromDb(a.FirstAt!)).TotalDays / (a.Visits - 1), 1)
                    : null;
                var lines = lastLines[a.Id].ToList();
                return new CustomerVisitRow(a.Id, a.Code, a.Name, a.Phone, last, days, VisitStatus.Classify(days, activeDays, inactiveDays),
                    a.Visits, interval, Money.FromStored(a.ServiceSpend + productSpend.GetValueOrDefault(a.Id)),
                    lines.Count == 0 ? null : Join(lines.Select(x => x.ServiceName)),
                    lines.Count == 0 ? null : Join(lines.Select(x => x.EmployeeName)));
            })
            .OrderBy(r => VisitStatus.Priority(r.Status)).ThenByDescending(r => r.DaysSince).ThenBy(r => r.Name)
            .ToList();
        return new CustomerVisitReport(asOf.Date, activeDays, inactiveDays, rows);
    }

    public IReadOnlyList<VisitHistoryRow> GetVisitHistory(long customerId)
    {
        using var c = db.Open();
        return c.Query<VisitLine>(VisitLinesSql + " AND r.customer_id = @customerId ORDER BY t.posted_at DESC, t.id DESC, l.line_no",
                new { customerId })
            .GroupBy(l => l.TreatmentId)
            .Select(g => new VisitHistoryRow(DbTime.FromDb(g.First().PostedAt), g.First().DocNo,
                Join(g.Select(x => x.ServiceName)), Join(g.Select(x => x.EmployeeName)), Money.FromStored(g.First().Total)))
            .ToList();
    }

    public LastVisitInfo? GetLastVisit(long customerId, DateTime asOf)
    {
        var t = DbTime.ToDb(asOf.Date.AddDays(1));
        using var c = db.Open();
        var walkIn = c.ExecuteScalar<long?>("SELECT is_walk_in FROM customer WHERE id = @customerId", new { customerId });
        if (walkIn is null or 1) return null;
        var lines = c.Query<VisitLine>(VisitLinesSql + """
                 AND r.customer_id = @customerId AND t.posted_at < @t AND t.id = (
                    SELECT t2.id FROM treatment t2 JOIN registration r2 ON r2.id = t2.registration_id
                    WHERE r2.customer_id = @customerId AND t2.status = 'LUNAS' AND t2.posted_at < @t
                    ORDER BY t2.posted_at DESC, t2.id DESC LIMIT 1)
                ORDER BY l.line_no
                """, new { customerId, t }).ToList();
        if (lines.Count == 0) return null;
        var at = DbTime.FromDb(lines[0].PostedAt);
        return new LastVisitInfo(at, (asOf.Date - at.Date).Days, Join(lines.Select(l => l.ServiceName)), Join(lines.Select(l => l.EmployeeName)));
    }
}
