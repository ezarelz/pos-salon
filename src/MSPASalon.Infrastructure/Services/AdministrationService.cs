using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Infrastructure.Database;
using MSPASalon.Core.Security;

namespace MSPASalon.Infrastructure.Services;

/// <summary>Reset transaction data and restore a backup (BUSINESS_RULES §16, R1–R5). ADMIN role only.</summary>
public sealed class AdministrationService(DbConnectionFactory db, IClock clock, IBackupService backup) : IAdministrationService
{
    /// <summary>Transaction tables, children before parents (foreign keys). Masters, users, settings, audit_log are kept.</summary>
    private static readonly string[] TransactionTables =
    [
        "payment", "stock_ledger",
        "treatment_line", "treatment", "registration_line", "registration",
        "sale_line", "sale", "purchase_line", "purchase", "stock_adjustment_line", "stock_adjustment",
        "doc_counter",
    ];

    public string DefaultBackupDirectory => Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "backup");

    // ------------------------------------------------------------------ preview

    private static TransactionCounts Count(SqliteConnection c, SqliteTransaction? tx = null)
    {
        int N(string table) => c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}", transaction: tx);
        return new TransactionCounts(N("registration"), N("treatment"), N("sale"), N("purchase"), N("stock_adjustment"),
            N("payment"), N("stock_ledger"));
    }

    private static (DateTime? Oldest, DateTime? Newest) Span(SqliteConnection c)
    {
        var r = c.QuerySingle<(string? Oldest, string? Newest)>("""
            SELECT MIN(d), MAX(d) FROM (
                SELECT reg_at AS d FROM registration UNION ALL SELECT doc_at FROM sale
                UNION ALL SELECT doc_at FROM purchase UNION ALL SELECT doc_at FROM stock_adjustment)
            """);
        return (r.Oldest is null ? null : DbTime.FromDb(r.Oldest), r.Newest is null ? null : DbTime.FromDb(r.Newest));
    }

    public ResetPreview PreviewReset()
    {
        using var c = db.Open();
        var (oldest, newest) = Span(c);
        var stock = c.QuerySingle<(int WithStock, int Negative)>(
            "SELECT COALESCE(SUM(stock_on_hand_x <> 0), 0), COALESCE(SUM(stock_on_hand_x < 0), 0) FROM product");
        return new ResetPreview(Count(c), oldest, newest,
            oldest is { } o && (clock.Now.Date - o.Date).TotalDays > AdministrationRules.OperationalDataDays,
            stock.WithStock, stock.Negative);
    }

    // ------------------------------------------------------------------ security

    private sealed class Credential
    {
        public string RoleCode { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public int PasswordIterations { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Role and password are checked against the database, not trusted from the UI session.</summary>
    private static void RequireAdmin(SqliteConnection c, UserSession session, string password)
    {
        var row = c.QuerySingleOrDefault<Credential>("""
            SELECT r.code AS RoleCode, u.password_hash AS PasswordHash, u.password_salt AS PasswordSalt,
                   u.password_iterations AS PasswordIterations, u.is_active AS IsActive
            FROM app_user u JOIN role r ON r.id = u.role_id WHERE u.id = @id
            """, new { id = session.User.Id });
        if (row is null || !row.IsActive || !string.Equals(row.RoleCode, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Fitur ini hanya untuk role ADMIN.");
        if (!PasswordHasher.Verify(password ?? "", row.PasswordHash, row.PasswordSalt, row.PasswordIterations))
            throw new BusinessException("Password salah.");
    }

    // ------------------------------------------------------------------ reset

    public ResetResult ResetTransactions(UserSession session, string password, string confirmText, StockResetMode mode, string backupDirectory)
    {
        if (!string.Equals((confirmText ?? "").Trim(), AdministrationRules.ResetConfirmWord, StringComparison.Ordinal))
            throw new BusinessException($"Ketik {AdministrationRules.ResetConfirmWord} (huruf besar) untuk konfirmasi.");
        using (var check = db.Open()) RequireAdmin(check, session, password);

        // R3: the backup is mandatory. If it fails, nothing is deleted.
        string backupPath;
        try
        {
            backupPath = backup.Backup(backupDirectory, "sebelum-reset");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException or NotSupportedException)
        {
            throw new BusinessException($"Backup otomatis gagal, reset dibatalkan (tidak ada data yang dihapus): {ex.Message}");
        }

        var at = clock.Now;
        var warnings = new List<string>();
        TransactionCounts deleted;
        var openingRows = 0;
        using (var c = db.Open())
        {
            using var tx = DbConnectionFactory.BeginWrite(c);
            deleted = Count(c, tx);

            // R1 (b): remember every product's balance from the ledger (source of truth) before it is cleared.
            var balances = mode == StockResetMode.KeepAsOpening
                ? c.Query<(long ProductId, long QtyX, long Cost, string Name)>("""
                        SELECT p.id, COALESCE(SUM(l.qty_in_x - l.qty_out_x), 0), p.cost_price, p.name
                        FROM product p LEFT JOIN stock_ledger l ON l.product_id = p.id
                        GROUP BY p.id, p.cost_price, p.name
                        """, transaction: tx).ToList()
                : [];

            foreach (var table in TransactionTables) c.Execute($"DELETE FROM {table}", transaction: tx);

            var docNo = $"SALDO-AWAL-{at:yyyyMMdd}";
            foreach (var b in balances.Where(b => b.QtyX != 0))
            {
                var qty = Quantity.FromStored(b.QtyX);
                StockLedger.Post(c, tx, b.ProductId, at, StockSource.Opening, b.ProductId, 0, docNo, qty, Money.FromStored(b.Cost),
                    "Saldo awal dari reset data transaksi");
                openingRows++;
                if (qty < 0) warnings.Add($"{b.Name}: stok minus ({Quantity.Format(qty)}) dibawa sebagai saldo awal. Koreksi lewat Stock Adjustment.");
            }
            // Cache = ledger (0 for every product in mode Zero).
            c.Execute("UPDATE product SET stock_on_hand_x = 0", transaction: tx);
            StockLedger.RebuildCache(c, tx);

            Tx.Audit(c, tx, at, session.User.Username, "RESET", details:
                $"Mode stok: {(mode == StockResetMode.Zero ? "nol" : "stok saat ini jadi saldo awal")}; " +
                $"dihapus: {deleted.Registrations} registrasi, {deleted.Treatments} treatment, {deleted.Sales} penjualan, " +
                $"{deleted.Purchases} pembelian, {deleted.Adjustments} adjustment, {deleted.Payments} pembayaran, {deleted.LedgerRows} mutasi stok; " +
                $"saldo awal: {openingRows} produk; backup: {backupPath}");
            tx.Commit();
        }

        using (var c = db.Open())
        {
            c.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            c.Execute("VACUUM;");   // reclaim the space of the deleted rows
        }
        return new ResetResult(deleted, backupPath, mode, openingRows, warnings);
    }

    // ------------------------------------------------------------------ restore

    private static readonly string[] RequiredTables = ["schema_version", "branch", "app_user", "role", "product", "treatment", "sale"];

    public BackupInfo InspectBackup(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new BusinessException("File backup tidak ditemukan.");
        var full = Path.GetFullPath(path);
        if (string.Equals(full, db.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("File ini adalah database yang sedang dipakai, bukan file backup.");

        var header = new byte[16];
        using (var fs = File.OpenRead(full))
            if (fs.Read(header, 0, 16) < 16 || System.Text.Encoding.ASCII.GetString(header, 0, 15) != "SQLite format 3")
                throw new BusinessException("File ini bukan database SQLite.");

        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = full, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = RequiredTables.Where(t => !tables.Contains(t)).ToList();
            if (missing.Count > 0)
                throw new BusinessException($"File ini bukan backup aplikasi POS Salon (tabel tidak ada: {string.Join(", ", missing)}).");
            var version = c.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 0;
            var latest = DatabaseInitializer.LoadMigrations().Max(m => m.Version);
            if (version > latest)
                throw new BusinessException($"Backup ini dibuat oleh versi aplikasi yang lebih baru (skema v{version}; aplikasi ini v{latest}). Perbarui aplikasi dulu.");
            if (c.ExecuteScalar<string>("PRAGMA integrity_check") != "ok")
                throw new BusinessException("File backup rusak (integrity check gagal).");
            var (_, newest) = Span(c);
            return new BackupInfo(full, version, c.ExecuteScalar<string?>("SELECT name FROM branch WHERE id = 1") ?? "-",
                Count(c), newest, new FileInfo(full).Length);
        }
        catch (SqliteException ex)
        {
            throw new BusinessException($"File backup tidak bisa dibaca: {ex.Message}");
        }
    }

    public RestoreResult Restore(UserSession session, string password, string backupPath, string safetyBackupDirectory)
    {
        using (var check = db.Open()) RequireAdmin(check, session, password);
        var info = InspectBackup(backupPath);

        string safety;
        try
        {
            safety = backup.Backup(safetyBackupDirectory, "sebelum-restore");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException or NotSupportedException)
        {
            throw new BusinessException($"Backup database saat ini gagal, restore dibatalkan: {ex.Message}");
        }

        // Copy the backup INTO the live database with SQLite's backup API: no file is replaced while the app holds it,
        // and the copy is all-or-nothing.
        SqliteConnection.ClearAllPools();
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = info.Path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString()))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }
        SqliteConnection.ClearAllPools();

        // An older backup is brought up to the current schema; seed keeps permissions/roles in sync.
        var init = new DatabaseInitializer(db, clock);
        init.Initialize();
        using (var c = db.Open())
        {
            using var tx = DbConnectionFactory.BeginWrite(c);
            Tx.Audit(c, tx, clock.Now, session.User.Username, "RESTORE",
                details: $"Dipulihkan dari {info.Path} (skema v{info.SchemaVersion}); database sebelumnya dibackup ke {safety}");
            tx.Commit();
        }
        return new RestoreResult(info.Path, safety, info.SchemaVersion, init.CurrentVersion());
    }
}
