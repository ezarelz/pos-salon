using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Tests.Framework;
using MSPASalon.Tests.UnitTests;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class AdministrationTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private Infrastructure.AppServices A => _f.Db.App;
    private string BackupDir => Path.Combine(_f.Db.Directory, "backup-test");
    public void Dispose() => _f.Dispose();

    private UserSession Admin => A.Auth.Login("admin", "admin");

    /// <summary>One paid treatment, one sale, one waiting registration, one cancelled sale; shampoo partly used.</summary>
    private void Seed()
    {
        var reg = A.Registrations.Create(_f.Customer, [new(_f.Creambath, _f.Capster1)], null, "admin");
        var t = A.Treatments.Start(reg.Id, "admin");
        A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 65000, null), "admin");
        A.Sales.Post(_f.Customer, [new(_f.Drink, 2, 5000, 0)], null, new PaymentInput(_f.Cash, 10000, null), "admin");
        var bad = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
        A.Sales.Cancel(bad.Id, "uji", "admin");
        A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, "admin");
        A.Branch.SaveLogo(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="));
        A.Branch.SaveVisitSettings(new Core.Reports.VisitSettings(20, 50, false));
    }

    private long Scalar(string sql) { using var c = A.Db.Open(); return c.ExecuteScalar<long>(sql); }

    [Test("M4-11", "Reset (stok nol): semua transaksi & ledger terhapus, master/customer/user/pengaturan/logo/audit tetap, nomor nota kembali 0001, backup berisi data lama")]
    public void ResetZero()
    {
        Seed();
        var preview = A.Administration.PreviewReset();
        Assert.Equal(2, preview.Counts.Registrations);
        Assert.Equal(1, preview.Counts.Treatments);
        Assert.Equal(2, preview.Counts.Sales);
        Assert.False(preview.LooksOperational, "all data from today");
        var customersBefore = A.Customers.GetAll().Count;

        var r = A.Administration.ResetTransactions(Admin, "admin", "RESET", StockResetMode.Zero, BackupDir);
        Assert.Equal(preview.Counts, r.Deleted);
        Assert.True(File.Exists(r.BackupPath) && r.BackupPath.Contains("sebelum-reset"), "mandatory backup");
        using (var b = new SqliteConnection($"Data Source={r.BackupPath};Mode=ReadOnly;Pooling=False"))
        {
            b.Open();
            Assert.Equal(2L, b.ExecuteScalar<long>("SELECT COUNT(*) FROM sale"), "backup holds the old data");
        }

        foreach (var table in new[] { "registration", "registration_line", "treatment", "treatment_line", "sale", "sale_line", "purchase",
                     "purchase_line", "stock_adjustment", "stock_adjustment_line", "payment", "stock_ledger", "doc_counter" })
            Assert.Equal(0L, Scalar($"SELECT COUNT(*) FROM {table}"), $"{table} empty");
        Assert.Equal(0m, _f.Stock(_f.Shampoo), "stock zero");
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM product WHERE stock_on_hand_x <> 0"));
        Assert.Equal(3, A.Products.GetAll().Count, "products kept");
        Assert.Equal(3, A.Services.GetAll().Count, "services kept");
        Assert.True(A.Services.Get(_f.Creambath)!.Materials.Count == 2, "recipes kept");
        Assert.Equal(customersBefore, A.Customers.GetAll().Count, "customers kept (R2)");
        Assert.True(A.Branch.GetLogo() is not null, "logo kept");
        Assert.Equal(20, A.Branch.GetVisitSettings().ActiveDays, "settings kept");
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM audit_log WHERE action = 'CANCEL'"), "earlier audit row (cancelled sale) kept");
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM audit_log WHERE action = 'RESET'"));
        A.Auth.Login("admin", "admin");   // users kept

        var next = A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, "admin");
        Assert.True(next.DocNo.EndsWith("/RG/0001"), $"numbering restarts: {next.DocNo}");
    }

    [Test("M4-12", "Reset (pertahankan stok): stok setiap produk menjadi saldo awal OPENING di ledger; kartu stok mulai dari saldo awal; stok minus diberi peringatan")]
    public void ResetKeepStock()
    {
        Seed();
        A.Adjustments.Post("uji minus", [new(_f.Mask, -0.5m)], "admin");   // mask: 1 − 0,15 − 0,5 = 0,35
        var before = new Dictionary<long, decimal> { [_f.Shampoo] = _f.Stock(_f.Shampoo), [_f.Mask] = _f.Stock(_f.Mask), [_f.Drink] = _f.Stock(_f.Drink) };
        using (var c = A.Db.Open())   // force a negative balance through the ledger to check the warning
            c.Execute("INSERT INTO stock_ledger(product_id, moved_at, source_type, source_id, source_line_id, doc_no, qty_in_x, qty_out_x, unit_cost) VALUES (@p, '2026-10-31 09:00:00', 'ADJUSTMENT', 999, 1, 'X', 0, 100000, 0)", new { p = _f.Drink });
        var drinkLedger = _f.LedgerStock(_f.Drink);

        var r = A.Administration.ResetTransactions(Admin, "admin", "RESET", StockResetMode.KeepAsOpening, BackupDir);
        Assert.Equal(before[_f.Shampoo], _f.Stock(_f.Shampoo));
        Assert.Equal(before[_f.Shampoo], _f.LedgerStock(_f.Shampoo), "cache = ledger");
        Assert.Equal(0.35m, _f.Stock(_f.Mask));
        Assert.Equal(drinkLedger, _f.Stock(_f.Drink), "ledger balance kept, even negative");
        Assert.Equal(3, r.OpeningRows);
        Assert.Equal(3L, Scalar("SELECT COUNT(*) FROM stock_ledger WHERE source_type = 'OPENING'"));
        Assert.Equal(1, r.Warnings.Count, "negative stock warned");
        Assert.Contains("Air Mineral", r.Warnings[0]);
        var card = A.StockCards.Get(_f.Shampoo, _f.Db.Clock.Now.Date, _f.Db.Clock.Now.Date);
        Assert.Equal(1, card.Rows.Count, "one opening row");
        Assert.Equal(before[_f.Shampoo], card.Closing);
    }

    [Test("M4-13", "Pengaman reset: password/konfirmasi salah, role bukan ADMIN, backup gagal → ditolak tanpa perubahan; data > 14 hari diberi peringatan")]
    public void ResetSafeguards()
    {
        Seed();
        var before = A.Administration.PreviewReset().Counts;
        Assert.Contains("Password salah", Assert.Throws<BusinessException>(() =>
            A.Administration.ResetTransactions(Admin, "salah", "RESET", StockResetMode.Zero, BackupDir)).Message);
        Assert.Contains("Ketik RESET", Assert.Throws<BusinessException>(() =>
            A.Administration.ResetTransactions(Admin, "admin", "reset", StockResetMode.Zero, BackupDir)).Message);

        // A role with EVERY catalogue permission is still not ADMIN.
        var all = A.Roles.Save(new Role { Code = "SUPER", Name = "Super", Permissions = Permissions.All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase) });
        A.Users.Create(new AppUser { Username = "super", DisplayName = "Super", RoleId = all }, "super-uji-99");
        var super = A.Auth.Login("super", "super-uji-99");
        Assert.Contains("hanya untuk role ADMIN", Assert.Throws<BusinessException>(() =>
            A.Administration.ResetTransactions(super, "super-uji-99", "RESET", StockResetMode.Zero, BackupDir)).Message);
        var shell = new ShellViewModel(ViewModelTests.Services(A), new FakeDialogs(), super);
        Assert.False(shell.NavItems.Any(n => n.Key.StartsWith("admin:")), "menu hidden for non-ADMIN");
        Assert.True(new ShellViewModel(ViewModelTests.Services(A), new FakeDialogs(), Admin).NavItems.Count(n => n.Key.StartsWith("admin:")) == 2);

        // Backup target is a file, not a folder → backup fails → nothing deleted (R3).
        var blocker = Path.Combine(_f.Db.Directory, "not-a-folder");
        File.WriteAllText(blocker, "x");
        Assert.Contains("Backup otomatis gagal", Assert.Throws<BusinessException>(() =>
            A.Administration.ResetTransactions(Admin, "admin", "RESET", StockResetMode.Zero, blocker)).Message);
        Assert.Equal(before, A.Administration.PreviewReset().Counts, "nothing changed");

        _f.Db.Clock.Now = _f.Db.Clock.Now.AddDays(15);
        Assert.True(A.Administration.PreviewReset().LooksOperational, "R4 warning after 14 days");
    }

    [Test("M4-14", "Restore: validasi file (bukan DB / bukan aplikasi ini / versi lebih baru), backup otomatis DB aktif, data kembali, backup versi lama dimigrasikan, audit")]
    public void Restore()
    {
        Seed();
        var backupFile = A.Backup.Backup(BackupDir);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");   // after the backup
        Assert.Equal(3L, Scalar("SELECT COUNT(*) FROM sale"));

        var junk = Path.Combine(_f.Db.Directory, "junk.db");
        File.WriteAllText(junk, "bukan database");
        Assert.Contains("bukan database SQLite", Assert.Throws<BusinessException>(() => A.Administration.InspectBackup(junk)).Message);
        var other = Path.Combine(_f.Db.Directory, "other.db");
        using (var o = new SqliteConnection($"Data Source={other};Pooling=False")) { o.Open(); o.Execute("CREATE TABLE x(a)"); }
        Assert.Contains("bukan backup aplikasi", Assert.Throws<BusinessException>(() => A.Administration.InspectBackup(other)).Message);
        var future = Path.Combine(_f.Db.Directory, "future.db");
        File.Copy(backupFile, future);
        using (var fc = new SqliteConnection($"Data Source={future};Pooling=False")) { fc.Open(); fc.Execute("INSERT INTO schema_version VALUES (99, 'x', 'x')"); }
        Assert.Contains("versi aplikasi yang lebih baru", Assert.Throws<BusinessException>(() => A.Administration.InspectBackup(future)).Message);
        Assert.Contains("database yang sedang dipakai", Assert.Throws<BusinessException>(() => A.Administration.InspectBackup(A.Db.DatabasePath)).Message);

        var info = A.Administration.InspectBackup(backupFile);
        Assert.Equal(2, info.Counts.Sales);
        Assert.Equal(3, info.SchemaVersion);
        Assert.Contains("Password salah", Assert.Throws<BusinessException>(() => A.Administration.Restore(Admin, "x", backupFile, BackupDir)).Message);

        var r = A.Administration.Restore(Admin, "admin", backupFile, BackupDir);
        Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM sale"), "data from the backup");
        Assert.True(File.Exists(r.SafetyBackupPath) && r.SafetyBackupPath.Contains("sebelum-restore"));
        using (var s = new SqliteConnection($"Data Source={r.SafetyBackupPath};Mode=ReadOnly;Pooling=False"))
        {
            s.Open();
            Assert.Equal(3L, s.ExecuteScalar<long>("SELECT COUNT(*) FROM sale"), "safety backup holds the replaced data");
        }
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM audit_log WHERE action = 'RESTORE'"));
        Assert.Equal("ok", A.Db.Open().ExecuteScalar<string>("PRAGMA integrity_check"));

        // Backup made by an older version (schema v1) is upgraded during the restore.
        var v1 = Path.Combine(_f.Db.Directory, "v1-backup.db");
        LegacySchema.CreateV1(v1, withCashierRole: true);
        var r1 = A.Administration.Restore(Admin, "admin", v1, BackupDir);
        Assert.Equal(1, r1.SchemaVersionOfBackup);
        Assert.Equal(3, r1.SchemaVersionNow);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM sale"));
        Assert.True(A.Branch.GetLogo() is null, "migrated logo column exists");
    }

    [Test("VM-17", "Halaman Reset & Restore: tombol aktif hanya dengan password + 'RESET', konfirmasi akhir, hasil tampil; restore memicu login ulang")]
    public void AdminPages()
    {
        Seed();
        var dialogs = new FakeDialogs { ConfirmAnswer = false };
        var page = new ResetDataPageViewModel(ViewModelTests.Services(A), dialogs, Admin) { BackupDirectory = BackupDir };
        Assert.Equal("1", page.CountRows.Single(x => x.Label == "Treatment").Value);
        Assert.False(page.ResetCommand.CanExecute(null));
        page.Password = "admin";
        page.ConfirmText = "reset";
        Assert.False(page.ResetCommand.CanExecute(null), "exact word RESET");
        page.ConfirmText = "RESET";
        Assert.True(page.ResetCommand.CanExecute(null));
        page.ResetCommand.Execute(null);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM treatment"), "final confirmation declined → nothing deleted");
        dialogs.ConfirmAnswer = true;
        page.KeepStock = true;
        Assert.False(page.ZeroStock);
        page.Password = "salah";
        page.ResetCommand.Execute(null);
        Assert.Contains("Password salah", page.ErrorText);
        Assert.Equal("", page.Password, "password cleared after a failure");
        page.Password = "admin";
        page.ResetCommand.Execute(null);
        Assert.Contains("Reset selesai", page.ResultText);
        Assert.Equal("0", page.CountRows.Single(x => x.Label == "Treatment").Value, "preview refreshed");
        Assert.True(_f.Stock(_f.Shampoo) > 0, "stock kept as opening");

        var backupFile = A.Backup.Backup(BackupDir);
        var restarted = false;
        dialogs.OpenFile = backupFile;
        var restore = new RestorePageViewModel(ViewModelTests.Services(A), dialogs, Admin, () => restarted = true);
        Assert.False(restore.RestoreCommand.CanExecute(null));
        restore.ChooseFileCommand.Execute(null);
        Assert.True(restore.HasInfo);
        restore.Password = "admin";
        restore.RestoreCommand.Execute(null);
        Assert.True(restarted, "logout → login again");
        dialogs.OpenFile = Path.Combine(_f.Db.Directory, "tidak-ada.db");
        restore.ChooseFileCommand.Execute(null);
        Assert.False(restore.HasInfo);
        Assert.Contains("tidak ditemukan", restore.ErrorText);
    }
}
