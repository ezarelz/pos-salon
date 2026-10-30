using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>Registration (BUSINESS_RULES §3).</summary>
public sealed class RegistrationService(DbConnectionFactory db, IClock clock) : IRegistrationService
{
    internal sealed class HeaderRow
    {
        public long Id { get; set; } public string RegNo { get; set; } = ""; public string RegAt { get; set; } = "";
        public long? CustomerId { get; set; } public string CustomerCode { get; set; } = ""; public string CustomerName { get; set; } = "";
        public string? Notes { get; set; } public string Status { get; set; } = ""; public string? CancelReason { get; set; }
        public string CreatedBy { get; set; } = ""; public string? ServiceSummary { get; set; } public long Total { get; set; }
        public long? TreatmentId { get; set; }

        public Registration ToEntity() => new()
        {
            Id = Id, RegNo = RegNo, RegAt = DbTime.FromDb(RegAt), CustomerId = CustomerId, CustomerCode = CustomerCode,
            CustomerName = CustomerName, Notes = Notes, Status = Status, CancelReason = CancelReason, CreatedBy = CreatedBy,
            ServiceSummary = ServiceSummary ?? "", Total = Total, TreatmentId = TreatmentId,
        };
    }

    internal const string SelectHeader = """
        SELECT r.id, r.reg_no, r.reg_at, r.customer_id, r.customer_code, r.customer_name, r.notes, r.status, r.cancel_reason,
               r.created_by, t.id AS treatment_id,
               (SELECT group_concat(l.service_name || ' (' || l.employee_name || ')', ', ')
                  FROM (SELECT * FROM registration_line WHERE registration_id = r.id ORDER BY line_no) l) AS service_summary,
               (SELECT COALESCE(SUM(price), 0) FROM registration_line WHERE registration_id = r.id) AS total
        FROM registration r LEFT JOIN treatment t ON t.registration_id = r.id
        """;

    public IReadOnlyList<Registration> GetList(DateTime from, DateTime to)
    {
        var (f, t) = Tx.Range(from, to);
        using var c = db.Open();
        return c.Query<HeaderRow>(SelectHeader + " WHERE r.reg_at >= @f AND r.reg_at < @t ORDER BY r.reg_at DESC, r.id DESC", new { f, t })
            .Select(r => r.ToEntity()).ToList();
    }

    public Registration? Get(long id)
    {
        using var c = db.Open();
        var r = c.QuerySingleOrDefault<HeaderRow>(SelectHeader + " WHERE r.id = @id", new { id })?.ToEntity();
        if (r is not null) r.Lines = LoadLines(c, "registration_line", "registration_id", id);
        return r;
    }

    internal static List<ServiceLine> LoadLines(SqliteConnection c, string table, string fk, long id, SqliteTransaction? tx = null) =>
        c.Query<ServiceLine>($"""
            SELECT id, line_no, service_id, service_code, service_name, price, employee_id, employee_code, employee_name
                   {(table == "treatment_line" ? ", commission" : "")}
            FROM {table} WHERE {fk} = @id ORDER BY line_no
            """, new { id }, tx).ToList();

    public PostResult Create(long customerId, IReadOnlyList<ServiceLineInput> lines, string? notes, string user)
    {
        var now = clock.Now;
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var errors = new List<string>();
        var customer = Tx.Customer(c, tx, customerId);
        if (!customer.IsActive) errors.Add($"Customer '{customer.Name}' tidak aktif.");
        var resolved = Tx.ResolveServiceLines(c, tx, lines, errors);
        if (errors.Count > 0) throw new BusinessException(errors);

        var regNo = DocumentNumberGenerator.Next(c, tx, DocumentType.Registration, now);
        var nowDb = DbTime.ToDb(now);
        var id = c.ExecuteScalar<long>("""
            INSERT INTO registration(reg_no, reg_at, customer_id, customer_code, customer_name, notes, status, created_by, created_at, updated_at)
            VALUES (@regNo, @now, @cid, @code, @name, @notes, @status, @user, @now, @now) RETURNING id
            """, new { regNo, now = nowDb, cid = customer.Id, code = customer.Code, name = customer.Name, notes = Clean(notes),
                       status = RegistrationStatus.Waiting, user }, tx);
        InsertLines(c, tx, id, resolved);
        tx.Commit();
        return new PostResult(id, regNo, []);
    }

    public void Update(long id, long customerId, IReadOnlyList<ServiceLineInput> lines, string? notes, string user)
    {
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var status = c.ExecuteScalar<string?>("SELECT status FROM registration WHERE id = @id", new { id }, tx)
                     ?? throw new BusinessException("Registrasi tidak ditemukan.");
        if (status != RegistrationStatus.Waiting)
            throw new BusinessException($"Registrasi berstatus {status} tidak bisa diubah. Perubahan jasa dilakukan di layar Proses Treatment.");
        var errors = new List<string>();
        var customer = Tx.Customer(c, tx, customerId);
        if (!customer.IsActive) errors.Add($"Customer '{customer.Name}' tidak aktif.");
        var resolved = Tx.ResolveServiceLines(c, tx, lines, errors);
        if (errors.Count > 0) throw new BusinessException(errors);

        c.Execute("""
            UPDATE registration SET customer_id = @cid, customer_code = @code, customer_name = @name, notes = @notes, updated_at = @now
            WHERE id = @id
            """, new { id, cid = customer.Id, code = customer.Code, name = customer.Name, notes = Clean(notes), now = DbTime.ToDb(clock.Now) }, tx);
        c.Execute("DELETE FROM registration_line WHERE registration_id = @id", new { id }, tx);
        InsertLines(c, tx, id, resolved);
        tx.Commit();
    }

    private static void InsertLines(SqliteConnection c, SqliteTransaction tx, long id, List<(Tx.ServiceRow S, Tx.EmployeeRow E)> lines)
    {
        var n = 1;
        foreach (var (s, e) in lines)
            c.Execute("""
                INSERT INTO registration_line(registration_id, line_no, service_id, service_code, service_name, price,
                                              employee_id, employee_code, employee_name)
                VALUES (@id, @n, @sid, @scode, @sname, @price, @eid, @ecode, @ename)
                """, new { id, n = n++, sid = s.Id, scode = s.Code, sname = s.Name, price = s.Price, eid = e.Id, ecode = e.Code, ename = e.Name }, tx);
    }

    public void Cancel(long id, string reason, string user)
    {
        var r = TransactionRules.RequireReason(reason);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var status = c.ExecuteScalar<string?>("SELECT status FROM registration WHERE id = @id", new { id }, tx)
                     ?? throw new BusinessException("Registrasi tidak ditemukan.");
        if (status != RegistrationStatus.Waiting)
            throw new BusinessException(status == RegistrationStatus.InProcess
                ? "Registrasi sedang diproses. Batalkan melalui layar Proses Treatment."
                : $"Registrasi berstatus {status} tidak bisa dibatalkan.");
        c.Execute("UPDATE registration SET status = @s, cancel_reason = @r, updated_at = @now WHERE id = @id",
            new { id, s = RegistrationStatus.Cancelled, r = $"{r} (oleh {user})", now = DbTime.ToDb(clock.Now) }, tx);
        tx.Commit();
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>Treatment: start, change services, complete &amp; pay with backflush, cancel (BUSINESS_RULES §4, §5, §9, §11).</summary>
public sealed class TreatmentService(DbConnectionFactory db, IClock clock) : ITreatmentService
{
    private sealed class HeaderRow
    {
        public long Id { get; set; } public long RegistrationId { get; set; } public string DocNo { get; set; } = "";
        public string TimeIn { get; set; } = ""; public string? TimeOut { get; set; } public string Status { get; set; } = "";
        public long Subtotal { get; set; } public long Discount { get; set; } public long GrandTotal { get; set; }
        public string CustomerCode { get; set; } = ""; public string CustomerName { get; set; } = "";
        public string? CancelReason { get; set; } public string? PostedBy { get; set; } public string? PostedAt { get; set; }
        public string CreatedBy { get; set; } = ""; public string? ServiceSummary { get; set; } public string? PaymentMethodName { get; set; }

        public Treatment ToEntity() => new()
        {
            Id = Id, RegistrationId = RegistrationId, DocNo = DocNo, TimeIn = DbTime.FromDb(TimeIn),
            TimeOut = TimeOut is null ? null : DbTime.FromDb(TimeOut), Status = Status, Subtotal = Subtotal, Discount = Discount,
            GrandTotal = GrandTotal, CustomerCode = CustomerCode, CustomerName = CustomerName, CancelReason = CancelReason,
            PostedBy = PostedBy, PostedAt = PostedAt is null ? null : DbTime.FromDb(PostedAt), CreatedBy = CreatedBy,
            ServiceSummary = ServiceSummary ?? "", PaymentMethodName = PaymentMethodName,
        };
    }

    private const string SelectHeader = """
        SELECT t.id, t.registration_id, t.doc_no, t.time_in, t.time_out, t.status, t.subtotal, t.discount, t.grand_total,
               r.customer_code, r.customer_name, t.cancel_reason, t.posted_by, t.posted_at, t.created_by,
               (SELECT group_concat(l.service_name || ' (' || l.employee_name || ')', ', ')
                  FROM (SELECT * FROM treatment_line WHERE treatment_id = t.id ORDER BY line_no) l) AS service_summary,
               (SELECT method_name FROM payment p WHERE p.source_type = 'TREATMENT' AND p.source_id = t.id) AS payment_method_name
        FROM treatment t JOIN registration r ON r.id = t.registration_id
        """;

    public IReadOnlyList<Registration> GetQueue()
    {
        using var c = db.Open();
        return c.Query<RegistrationService.HeaderRow>(RegistrationService.SelectHeader +
                " WHERE r.status IN ('MENUNGGU', 'DIPROSES') ORDER BY r.reg_at, r.id")
            .Select(r => r.ToEntity()).ToList();
    }

    public IReadOnlyList<Treatment> GetList(DateTime from, DateTime to)
    {
        var (f, t) = Tx.Range(from, to);
        using var c = db.Open();
        return c.Query<HeaderRow>(SelectHeader + " WHERE t.time_in >= @f AND t.time_in < @t ORDER BY t.time_in DESC, t.id DESC", new { f, t })
            .Select(r => r.ToEntity()).ToList();
    }

    public Treatment? Get(long treatmentId)
    {
        using var c = db.Open();
        return Load(c, treatmentId);
    }

    public Treatment? GetByRegistration(long registrationId)
    {
        using var c = db.Open();
        var id = c.ExecuteScalar<long?>("SELECT id FROM treatment WHERE registration_id = @registrationId", new { registrationId });
        return id is null ? null : Load(c, id.Value);
    }

    private static Treatment? Load(SqliteConnection c, long id)
    {
        var t = c.QuerySingleOrDefault<HeaderRow>(SelectHeader + " WHERE t.id = @id", new { id })?.ToEntity();
        if (t is null) return null;
        t.Lines = RegistrationService.LoadLines(c, "treatment_line", "treatment_id", id);
        t.Payment = Tx.LoadPayment(c, "TREATMENT", id);
        return t;
    }

    public Treatment Start(long registrationId, string user)
    {
        var now = DbTime.ToDb(clock.Now);
        long id;
        using (var c = db.Open())
        using (var tx = DbConnectionFactory.BeginWrite(c))
        {
            var reg = c.QuerySingleOrDefault<(string RegNo, string Status)>(
                          "SELECT reg_no, status FROM registration WHERE id = @registrationId", new { registrationId }, tx);
            if (reg.RegNo is null) throw new BusinessException("Registrasi tidak ditemukan.");
            if (reg.Status != RegistrationStatus.Waiting)
                throw new BusinessException($"Registrasi {reg.RegNo} berstatus {reg.Status}; hanya registrasi MENUNGGU yang bisa dimulai.");

            id = c.ExecuteScalar<long>("""
                INSERT INTO treatment(registration_id, doc_no, time_in, status, created_by, created_at, updated_at)
                VALUES (@registrationId, @docNo, @now, @status, @user, @now, @now) RETURNING id
                """, new { registrationId, docNo = reg.RegNo, now, status = TreatmentStatus.InProgress, user }, tx);
            c.Execute("""
                INSERT INTO treatment_line(treatment_id, line_no, service_id, service_code, service_name, price,
                                           employee_id, employee_code, employee_name)
                SELECT @id, line_no, service_id, service_code, service_name, price, employee_id, employee_code, employee_name
                FROM registration_line WHERE registration_id = @registrationId ORDER BY line_no
                """, new { id, registrationId }, tx);
            c.Execute("UPDATE registration SET status = @s, updated_at = @now WHERE id = @registrationId",
                new { s = RegistrationStatus.InProcess, now, registrationId }, tx);
            Recalculate(c, tx, id, now);
            tx.Commit();
        }
        return Get(id)!;
    }

    /// <summary>
    /// Recomputes subtotal from the lines in ONE statement (the CHECK grand_total = subtotal − discount is evaluated per statement).
    /// The discount is only entered at posting, so it is 0 while the treatment is in progress.
    /// </summary>
    private static void Recalculate(SqliteConnection c, SqliteTransaction tx, long id, string now) =>
        c.Execute("""
            UPDATE treatment
               SET subtotal    = (SELECT COALESCE(SUM(price), 0) FROM treatment_line WHERE treatment_id = @id),
                   grand_total = (SELECT COALESCE(SUM(price), 0) FROM treatment_line WHERE treatment_id = @id),
                   discount = 0, updated_at = @now
             WHERE id = @id
            """, new { id, now }, tx);

    private static string RequireStatus(SqliteConnection c, SqliteTransaction tx, long id, params string[] allowed)
    {
        var status = c.ExecuteScalar<string?>("SELECT status FROM treatment WHERE id = @id", new { id }, tx)
                     ?? throw new BusinessException("Treatment tidak ditemukan.");
        if (!allowed.Contains(status)) throw new BusinessException($"Treatment berstatus {status}; aksi ini tidak diizinkan.");
        return status;
    }

    public void UpdateLines(long treatmentId, IReadOnlyList<ServiceLineInput> lines, string user)
    {
        var now = DbTime.ToDb(clock.Now);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        RequireStatus(c, tx, treatmentId, TreatmentStatus.InProgress);
        var errors = new List<string>();
        var resolved = Tx.ResolveServiceLines(c, tx, lines, errors);
        if (errors.Count > 0) throw new BusinessException(errors);

        // Keep the price snapshot of services already on the treatment (taken at registration); new services use the current master price.
        var existingPrices = c.Query<(long ServiceId, long Price)>(
                "SELECT service_id, price FROM treatment_line WHERE treatment_id = @treatmentId AND service_id IS NOT NULL ORDER BY line_no",
                new { treatmentId }, tx)
            .GroupBy(x => x.ServiceId).ToDictionary(g => g.Key, g => g.First().Price);

        c.Execute("DELETE FROM treatment_line WHERE treatment_id = @treatmentId", new { treatmentId }, tx);
        var n = 1;
        foreach (var (s, e) in resolved)
            c.Execute("""
                INSERT INTO treatment_line(treatment_id, line_no, service_id, service_code, service_name, price,
                                           employee_id, employee_code, employee_name)
                VALUES (@treatmentId, @n, @sid, @scode, @sname, @price, @eid, @ecode, @ename)
                """, new
            {
                treatmentId, n = n++, sid = s.Id, scode = s.Code, sname = s.Name,
                price = existingPrices.TryGetValue(s.Id, out var p) ? p : s.Price, eid = e.Id, ecode = e.Code, ename = e.Name,
            }, tx);
        Recalculate(c, tx, treatmentId, now);
        tx.Commit();
    }

    private static Dictionary<long, IReadOnlyList<(long ProductId, decimal Qty)>> LoadRecipes(
        SqliteConnection c, IEnumerable<long> serviceIds, SqliteTransaction? tx)
    {
        var ids = serviceIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return c.Query<(long ServiceId, long ProductId, long QtyX)>(
                "SELECT service_id, product_id, qty_x FROM service_material WHERE service_id IN @ids", new { ids }, tx)
            .GroupBy(r => r.ServiceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(long, decimal)>)g.Select(r => (r.ProductId, Quantity.FromStored(r.QtyX))).ToList());
    }

    private static List<BackflushItem> BuildBackflush(SqliteConnection c, IReadOnlyList<long> serviceIdsOfLines, SqliteTransaction? tx)
    {
        var totals = TransactionRules.Backflush(serviceIdsOfLines, LoadRecipes(c, serviceIdsOfLines, tx));
        var items = new List<BackflushItem>();
        foreach (var (productId, qty) in totals)
        {
            var p = c.QuerySingle<Tx.ProductRow>(
                "SELECT id, code, name, unit, cost_price, sell_price, is_sellable, is_active, stock_on_hand_x FROM product WHERE id = @productId",
                new { productId }, tx);
            items.Add(new BackflushItem
            {
                ProductId = p.Id, ProductCode = p.Code, ProductName = p.Name, Unit = p.Unit, Qty = qty,
                StockOnHand = p.Stock, UnitCost = p.CostPrice,
            });
        }
        return items.OrderBy(i => i.ProductName).ToList();
    }

    public IReadOnlyList<BackflushItem> PreviewBackflush(IReadOnlyList<long> serviceIdsOfLines)
    {
        using var c = db.Open();
        return BuildBackflush(c, serviceIdsOfLines, null);
    }

    public PostResult Complete(long treatmentId, decimal discount, PaymentInput payment, string user)
    {
        var at = clock.Now;
        var now = DbTime.ToDb(at);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        RequireStatus(c, tx, treatmentId, TreatmentStatus.InProgress);

        var head = c.QuerySingle<(string DocNo, long RegistrationId)>(
            "SELECT doc_no, registration_id FROM treatment WHERE id = @treatmentId", new { treatmentId }, tx);
        var lines = c.Query<(long Id, long? ServiceId, long Price)>(
            "SELECT id, service_id, price FROM treatment_line WHERE treatment_id = @treatmentId ORDER BY line_no", new { treatmentId }, tx).ToList();
        if (lines.Count == 0) throw new BusinessException("Treatment belum memiliki jasa (P3).");

        var errors = new List<string>();
        var subtotal = (decimal)lines.Sum(l => l.Price);
        var grand = TransactionRules.TreatmentTotal(subtotal, discount, errors);
        if (errors.Count > 0) throw new BusinessException(errors);

        // Commission snapshot from the service master at posting time, credited to the final capster (§11).
        foreach (var l in lines)
        {
            var commission = l.ServiceId is { } sid
                ? c.ExecuteScalar<long?>("SELECT commission FROM service WHERE id = @sid", new { sid }, tx) ?? 0
                : 0;
            c.Execute("UPDATE treatment_line SET commission = @commission WHERE id = @id", new { commission, id = l.Id }, tx);
        }

        // Backflush materials (B1–B7). Never blocks completion; negative stock is reported as a warning (A4).
        var warnings = new List<string>();
        var backflush = BuildBackflush(c, lines.Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value).ToList(), tx);
        foreach (var item in backflush)
        {
            StockLedger.Post(c, tx, item.ProductId, at, StockSource.Treatment, treatmentId, 0, head.DocNo, -item.Qty, item.UnitCost,
                "Backflush bahan treatment");
            if (item.IsShort)
                warnings.Add($"Stok {item.ProductName} menjadi {Quantity.Format(item.StockAfter)} {item.Unit} (minus). Lakukan stock opname / pembelian.");
        }

        c.Execute("""
            UPDATE treatment SET time_out = @now, status = @paid, subtotal = @subtotal, discount = @discount, grand_total = @grand,
                   posted_by = @user, posted_at = @now, updated_at = @now
            WHERE id = @treatmentId
            """, new
        {
            now, paid = TreatmentStatus.Paid, subtotal = Money.ToStored(subtotal), discount = Money.ToStored(discount),
            grand = Money.ToStored(grand), user, treatmentId,
        }, tx);
        Tx.WritePayment(c, tx, "TREATMENT", treatmentId, grand, payment, user, at);
        c.Execute("UPDATE registration SET status = @s, updated_at = @now WHERE id = @rid",
            new { s = RegistrationStatus.Done, now, rid = head.RegistrationId }, tx);
        tx.Commit();
        return new PostResult(treatmentId, head.DocNo, warnings);
    }

    public void Cancel(long treatmentId, string reason, bool allowPosted, string user)
    {
        var r = $"{TransactionRules.RequireReason(reason)} (oleh {user})";
        var now = DbTime.ToDb(clock.Now);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        var status = RequireStatus(c, tx, treatmentId, TreatmentStatus.InProgress, TreatmentStatus.Paid);
        if (status == TreatmentStatus.Paid)
        {
            if (!allowPosted) throw new BusinessException("Anda tidak memiliki hak untuk membatalkan treatment yang sudah LUNAS.");
            Tx.RemoveLedger(c, tx, StockSource.Treatment, treatmentId);
            c.Execute("DELETE FROM payment WHERE source_type = 'TREATMENT' AND source_id = @treatmentId", new { treatmentId }, tx);
            c.Execute("UPDATE treatment_line SET commission = 0 WHERE treatment_id = @treatmentId", new { treatmentId }, tx);
        }
        c.Execute("UPDATE treatment SET status = @s, cancel_reason = @r, updated_at = @now WHERE id = @treatmentId",
            new { s = TreatmentStatus.Cancelled, r, now, treatmentId }, tx);
        c.Execute("""
            UPDATE registration SET status = @s, cancel_reason = @r, updated_at = @now
            WHERE id = (SELECT registration_id FROM treatment WHERE id = @treatmentId)
            """, new { s = RegistrationStatus.Cancelled, r, now, treatmentId }, tx);
        tx.Commit();
    }
}
