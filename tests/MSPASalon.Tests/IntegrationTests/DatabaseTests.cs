using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Infrastructure.Database;
using MSPASalon.Infrastructure.Services;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class DatabaseTests : IDisposable
{
    private readonly TestDatabase _db = new();
    public void Dispose() => _db.Dispose();

    [Test("INT-01", "Migrasi: skema terbaru (v3) terbentuk, upgrade database v1 → v3, idempotent, foreign_keys ON, WAL")]
    public void MigrationCreatesSchema()
    {
        var init = new DatabaseInitializer(_db.App.Db, _db.Clock);
        var latest = DatabaseInitializer.LoadMigrations().Max(m => m.Version);
        Assert.Equal(3, latest, "migrations 001 + 002 (logo nota) + 003 (laporan & audit)");
        Assert.Equal(latest, init.CurrentVersion(), "schema version");
        init.Initialize();                                   // second run must be a no-op
        Assert.Equal(latest, init.CurrentVersion(), "schema version after re-init");

        // Upgrade path of a real M1–M3.1 database (schema v1, with data): 002 + 003 are applied, data stays.
        var oldPath = Path.Combine(_db.Directory, "v1.db");
        LegacySchema.CreateV1(oldPath, withCashierRole: true);
        using (var v1 = new SqliteConnection($"Data Source={oldPath}"))
        {
            v1.Open();
            v1.Execute("UPDATE branch SET name = 'Salon Lama' WHERE id = 1");
        }
        var upgraded = new Infrastructure.AppServices(oldPath, _db.Clock);
        Assert.Equal(3, new DatabaseInitializer(upgraded.Db, _db.Clock).CurrentVersion(), "v1 → v3");
        Assert.Equal("Salon Lama", upgraded.Branch.Get().Name, "branch data kept");
        Assert.True(upgraded.Branch.GetLogo() is null);
        using (var u = upgraded.Db.Open())
        {
            Assert.Equal(0L, u.ExecuteScalar<long>("SELECT COUNT(*) FROM audit_log"));
            Assert.Equal("ok", u.ExecuteScalar<string>("PRAGMA integrity_check"));
        }

        using var c = _db.App.Db.Open();
        Assert.Equal(1L, c.ExecuteScalar<long>("PRAGMA foreign_keys"), "foreign_keys");
        Assert.Equal("wal", c.ExecuteScalar<string>("PRAGMA journal_mode"), "journal_mode");
        var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToHashSet();
        foreach (var t in new[] { "customer", "product", "service", "service_material", "registration", "registration_line",
                     "treatment", "treatment_line", "sale", "sale_line", "purchase", "purchase_line", "stock_adjustment",
                     "stock_adjustment_line", "payment", "stock_ledger", "doc_counter", "role", "role_permission", "app_user" })
            Assert.True(tables.Contains(t), $"table {t} missing");
        Assert.Equal("ok", c.ExecuteScalar<string>("PRAGMA integrity_check"));
        Assert.Equal(0, c.Query("PRAGMA foreign_key_check").Count(), "foreign_key_check");
    }

    [Test("INT-02", "Seed: role ADMIN/KASIR, admin wajib ganti password, metode bayar, walk-in, TANPA data sampel")]
    public void SeedReferenceDataOnly()
    {
        using var c = _db.App.Db.Open();
        Assert.Equal(2L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM role WHERE is_system = 1"));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT must_change_password FROM app_user WHERE username = 'admin'"));
        Assert.Equal("TUNAI,QRIS,TRANSFER", string.Join(",", c.Query<string>("SELECT code FROM payment_method ORDER BY sort_order")));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM customer WHERE is_walk_in = 1"));
        Assert.Equal(0L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM product"), "no sample products (legacy D17)");
        Assert.Equal(0L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sale"), "no sample sales");
        var adminPerms = c.ExecuteScalar<long>("SELECT COUNT(*) FROM role_permission rp JOIN role r ON r.id = rp.role_id WHERE r.code = 'ADMIN'");
        Assert.Equal((long)Permissions.All.Count, adminPerms, "ADMIN has every permission");
    }

    [Test("INT-03", "Penomoran: berurutan per bulan, reset di bulan baru")]
    public void NumberingSequentialAndMonthlyReset()
    {
        using var c = _db.App.Db.Open();
        string Next(DateTime at)
        {
            using var tx = DbConnectionFactory.BeginWrite(c);
            var n = DocumentNumberGenerator.Next(c, tx, DocumentType.Registration, at);
            tx.Commit();
            return n;
        }
        Assert.Equal("2610/RG/0001", Next(new DateTime(2026, 10, 1)));
        Assert.Equal("2610/RG/0002", Next(new DateTime(2026, 10, 31, 23, 59, 59)));
        Assert.Equal("2611/RG/0001", Next(new DateTime(2026, 11, 1)), "monthly reset");
        using var tx2 = DbConnectionFactory.BeginWrite(c);
        Assert.Equal("2610/FJ/0001", DocumentNumberGenerator.Next(c, tx2, DocumentType.Sale, new DateTime(2026, 10, 5)), "independent per type");
        tx2.Commit();
    }

    [Test("INT-04", "Penomoran: transaksi yang gagal/rollback TIDAK memakan nomor (tanpa gap)")]
    public void NumberingRollbackLeavesNoGap()
    {
        var at = new DateTime(2026, 10, 31);
        using var c = _db.App.Db.Open();
        using (var tx = DbConnectionFactory.BeginWrite(c))
        {
            Assert.Equal("2610/RG/0001", DocumentNumberGenerator.Next(c, tx, DocumentType.Registration, at));
            tx.Rollback();                                    // simulated failure while saving the document
        }
        using var tx2 = DbConnectionFactory.BeginWrite(c);
        Assert.Equal("2610/RG/0001", DocumentNumberGenerator.Next(c, tx2, DocumentType.Registration, at), "number re-used, no gap");
        tx2.Commit();
    }

    [Test("INT-05", "Penomoran: nomor dokumen ganda ditolak oleh constraint UNIQUE")]
    public void DuplicateDocumentNumberRejected()
    {
        using var c = _db.App.Db.Open();
        const string sql = """
            INSERT INTO registration(reg_no, reg_at, customer_code, customer_name, status, created_by, created_at, updated_at)
            VALUES ('2610/RG/0001', '2026-10-31 09:00:00', 'UMUM', 'UMUM', 'MENUNGGU', 'admin', '2026-10-31 09:00:00', '2026-10-31 09:00:00')
            """;
        c.Execute(sql);
        var ex = Assert.Throws<SqliteException>(() => c.Execute(sql));
        Assert.Contains("UNIQUE", ex.Message);
    }

    [Test("INT-06", "Foreign key ditegakkan (aplikasi lama mematikan FK)")]
    public void ForeignKeysEnforced()
    {
        using var c = _db.App.Db.Open();
        var ex = Assert.Throws<SqliteException>(() => c.Execute("""
            INSERT INTO registration_line(registration_id, line_no, service_code, service_name, price, employee_code, employee_name)
            VALUES (999, 1, 'X', 'X', 0, 'X', 'X')
            """));
        Assert.Contains("FOREIGN KEY", ex.Message);
    }

    [Test("INT-07", "Constraint pembayaran: kurang bayar & kembalian non-tunai ditolak di level database")]
    public void PaymentConstraints()
    {
        using var c = _db.App.Db.Open();
        string Sql(long due, long received, long change, int cash) => $"""
            INSERT INTO payment(source_type, source_id, method_code, method_name, is_cash, amount_due, amount_received,
                                change_amount, paid_at, created_by)
            VALUES ('SALE', {due + received}, 'X', 'X', {cash}, {due}, {received}, {change}, '2026-10-31 10:00:00', 'admin')
            """;
        Assert.Throws<SqliteException>(() => c.Execute(Sql(28000, 20000, 0, 1)));     // underpayment
        Assert.Throws<SqliteException>(() => c.Execute(Sql(28000, 30000, 2000, 0)));  // change on non-cash
        Assert.Throws<SqliteException>(() => c.Execute(Sql(28000, 30000, 1000, 1)));  // wrong change
        c.Execute(Sql(28000, 30000, 2000, 1));                                         // valid cash
        c.Execute(Sql(28000, 28000, 0, 0));                                            // valid QRIS
    }

    [Test("INT-08", "Stock ledger: posting memperbarui cache, saldo = ledger, double posting ditolak")]
    public void StockLedgerIsSourceOfTruth()
    {
        var id = _db.App.Products.Save(new Product { Code = "PRD001", Name = "Shampo", Unit = "BTL", IsSellable = true, IsConsumable = true, CostPrice = 18000 });
        using var c = _db.App.Db.Open();
        using (var tx = DbConnectionFactory.BeginWrite(c))
        {
            StockLedger.Post(c, tx, id, _db.Clock.Now, "OPENING", id, 0, "SALDO-AWAL", 5m, 18000);
            StockLedger.Post(c, tx, id, _db.Clock.Now, "TREATMENT", 1, 0, "2610/RG/0001", -0.1m, 18000);
            StockLedger.Post(c, tx, id, _db.Clock.Now, "TREATMENT", 2, 0, "2610/RG/0002", -0.2m, 18000);
            tx.Commit();
        }
        Assert.Equal(4.7m, StockLedger.LedgerBalance(c, id), "ledger balance");
        Assert.Equal(4.7m, _db.App.Products.Get(id)!.StockOnHand, "cache equals ledger");

        using (var tx = DbConnectionFactory.BeginWrite(c))
        {
            Assert.Throws<SqliteException>(() =>
                StockLedger.Post(c, tx, id, _db.Clock.Now, "TREATMENT", 1, 0, "2610/RG/0001", -0.1m, 18000));
            tx.Rollback();
        }
        Assert.Equal(4.7m, _db.App.Products.Get(id)!.StockOnHand, "unchanged after rejected double posting");

        using (var tx = DbConnectionFactory.BeginWrite(c))
        {
            c.Execute("UPDATE product SET stock_on_hand_x = 0 WHERE id = @id", new { id }, tx);   // corrupt the cache
            Assert.Equal(1, StockLedger.RebuildCache(c, tx), "one product corrected");
            tx.Commit();
        }
        Assert.Equal(4.7m, _db.App.Products.Get(id)!.StockOnHand, "cache rebuilt from ledger");
    }

    [Test("INT-09", "Backup online menghasilkan salinan database yang utuh")]
    public void BackupProducesConsistentCopy()
    {
        _db.App.Customers.Save(new Customer { Code = "CST001", Name = "Budi" });
        var file = _db.App.Backup.Backup(Path.Combine(_db.Directory, "backup"));
        Assert.True(File.Exists(file), "backup file exists");
        using var b = new SqliteConnection($"Data Source={file};Pooling=False");
        b.Open();
        Assert.Equal(2L, b.ExecuteScalar<long>("SELECT COUNT(*) FROM customer"), "walk-in + CST001");
        Assert.Equal("ok", b.ExecuteScalar<string>("PRAGMA integrity_check"));
    }
}
