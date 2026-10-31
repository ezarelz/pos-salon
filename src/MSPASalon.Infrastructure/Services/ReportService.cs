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
}
