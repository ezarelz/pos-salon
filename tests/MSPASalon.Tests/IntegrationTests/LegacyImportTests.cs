using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Infrastructure.Services;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

/// <summary>
/// Builds a database with the EXACT legacy schema (copied from the decompiled MspaSalon.Data.Db) and
/// fictional rows shaped like the real data, then imports it.
/// </summary>
public sealed class LegacyImportTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _legacyPath;

    public LegacyImportTests()
    {
        _legacyPath = Path.Combine(_db.Directory, "salon.sqlite");
        using var c = new SqliteConnection($"Data Source={_legacyPath};Pooling=False");
        c.Open();
        c.Execute("""
            CREATE TABLE Tcabang (ccode INTEGER PRIMARY KEY, csname TEXT, cname TEXT, alm1 TEXT, telp TEXT, cpaper TEXT, csize INTEGER, mbserial TEXT, noserial TEXT, register TEXT);
            CREATE TABLE Tcateg (ccode INTEGER, kodecateg TEXT, namacateg TEXT, PRIMARY KEY(ccode, kodecateg));
            CREATE TABLE Tservice (ccode INTEGER, kodeservice TEXT, namaservice TEXT, harga REAL, komisi REAL, PRIMARY KEY(ccode, kodeservice));
            CREATE TABLE mproduct (ccode INTEGER, kodeprod TEXT, namaprod TEXT, kodecateg TEXT, unit TEXT, hargapokok REAL, hargajual REAL, stockmin INTEGER, stockmax INTEGER, stockakhir INTEGER, hpp REAL, lokasipic TEXT, discontinue INTEGER, PRIMARY KEY(ccode, kodeprod));
            CREATE TABLE mcustomer (ccode INTEGER, kodecust TEXT, namacust TEXT, alm TEXT, hp TEXT, jenis TEXT, lokasipic TEXT, member INTEGER, inactive INTEGER, PRIMARY KEY(ccode, kodecust));
            CREATE TABLE msupplier (ccode INTEGER, kodesupp TEXT, namasupp TEXT, alm TEXT, hp TEXT, telp TEXT, contact TEXT, inactive INTEGER, PRIMARY KEY(ccode, kodesupp));
            CREATE TABLE tkar (ccode INTEGER, kodekar TEXT, namakar TEXT, jenis TEXT, tmplahir TEXT, tgllahir TEXT, alm TEXT, telp TEXT, jabatan TEXT, gajipokok REAL, inactive INTEGER, chk INTEGER, capster INTEGER, lokasipic TEXT, PRIMARY KEY(ccode, kodekar));
            CREATE TABLE users (ccode INTEGER, kodekar TEXT, username TEXT, pwd TEXT, pa TEXT, sp INTEGER, PRIMARY KEY(ccode, username));
            CREATE TABLE penjualan (ccode INTEGER, nofj TEXT, tanggal TEXT, kodecust TEXT, lampiran TEXT, status TEXT, post TEXT, gtotal REAL, kodeopr TEXT, PRIMARY KEY(ccode, nofj));

            INSERT INTO Tcabang(ccode, csname, cname, alm1, telp, cpaper, csize) VALUES (1, 'CSS', 'Contoh Salon Studio', 'Jl. Contoh No. 1, Bekasi', '021-000-0000', 'Letter', 0);
            INSERT INTO Tcateg VALUES (1, '001', 'Umum'), (1, '004', 'Perawatan Rambut');
            INSERT INTO mproduct(ccode, kodeprod, namaprod, kodecateg, unit, hargapokok, hargajual, stockmin, stockmax, stockakhir, discontinue) VALUES
              (1, 'PRD001', 'Shampoo Anti Ketombe 200ml', '004', 'BTL', 18000.0, 28000.0, 10, 100, 41, 0),
              (1, 'PRD002', 'Conditioner 200ml',          '004', 'BTL', 20000.4, 32000.0, 10, 100, 0, 0),
              (1, 'PRD003', 'Produk Lama',                '999', 'PCS', 1000.0, 2000.0, 50, 10, -2, 1);
            INSERT INTO mcustomer VALUES (1, 'CUST001', 'Customer Satu', 'Jl. A', '081234567001', 'LAKI-LAKI', NULL, 1, 0),
                                         (1, 'CUST002', 'Customer Dua', NULL, 'tidak ada', 'PEREMPUAN', NULL, 0, 0),
                                         (1, 'CUST000', 'UMUM / WALK-IN', '-', '-', 'LAKI-LAKI', NULL, 0, 0);
            INSERT INTO msupplier VALUES (1, 'SUP001', 'Distributor Kosmetik', 'Jakarta', '0812000', NULL, 'Pak A', 0);
            INSERT INTO tkar(ccode, kodekar, namakar, jenis, jabatan, gajipokok, inactive, capster, telp) VALUES
              (1, 'KAR002', 'Capster Satu', 'LAKI-LAKI', 'Capster', 3000000, 0, 1, '0811'),
              (1, 'KAR004', 'Kasir Satu', 'PEREMPUAN', 'Kasir', 2800000, 0, 0, NULL);
            INSERT INTO Tservice VALUES (1, 'SVC001', 'Potong Rambut Pria', 70000.0, 30000.0), (1, 'SVC009', 'Jasa Aneh', 10000.0, 15000.0);
            INSERT INTO users VALUES (1, '000', 'admin', 'admin', 'SA', 1), (1, 'KAR002', 'kasir', 'kasir-uji-99', 'KASIR', 0);
            INSERT INTO penjualan VALUES (1, '2610/FJ/0001', '2026-10-30 00:00:00', 'CUST002', NULL, 'Y', 'Y', 28000.0, 'admin');
            """);
    }

    public void Dispose() => _db.Dispose();

    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Test("INT-20", "Import data lama: master terpetakan, saldo awal via ledger, password di-hash, file lama tidak berubah")]
    public void ImportLegacyMasterData()
    {
        var before = FileHash(_legacyPath);
        var r = _db.App.LegacyImport.Import(_legacyPath);
        Assert.Equal(before, FileHash(_legacyPath), "legacy file unchanged (read-only)");

        Assert.Equal(3, r.Counts["Produk"]); Assert.Equal(2, r.Counts["Customer"]); Assert.Equal(2, r.Counts["Karyawan"]);
        Assert.Equal(2, r.Counts["Jasa"]); Assert.Equal(1, r.Counts["Supplier"]); Assert.Equal(2, r.Counts["Kategori"]);
        Assert.Equal(1, r.Counts["User"], "legacy admin skipped: already exists");
        Assert.Equal(2, r.Counts["Saldo awal stok"]);

        Assert.Equal("Contoh Salon Studio", _db.App.Branch.Get().Name);
        Assert.Equal(58, _db.App.Branch.Get().PaperWidthMm);

        var products = _db.App.Products.GetAll().ToDictionary(p => p.Code);
        Assert.Equal(41m, products["PRD001"].StockOnHand, "opening stock");
        Assert.Equal("Perawatan Rambut", products["PRD001"].CategoryName);
        Assert.Equal(20000m, products["PRD002"].CostPrice, "REAL 20000.4 rounded to whole Rupiah");
        Assert.False(products["PRD003"].IsActive, "discontinued -> inactive");
        Assert.Null(products["PRD003"].CategoryId, "unknown category");
        Assert.Equal(0m, products["PRD003"].StockMax, "max < min cleared");
        Assert.Equal(-2m, products["PRD003"].StockOnHand, "negative opening kept");

        using (var c = _db.App.Db.Open())
        {
            Assert.Equal(41m, StockLedger.LedgerBalance(c, products["PRD001"].Id), "ledger = cache");
            Assert.Equal(0L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE password_hash IN ('admin', 'kasir-uji-99')"), "no plaintext");
            Assert.Equal(0L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM sale"), "legacy transactions NOT imported");
        }

        var customers = _db.App.Customers.GetAll().ToDictionary(x => x.Code);
        Assert.Equal(Gender.Male, customers["CUST001"].Gender);
        Assert.True(customers["CUST001"].IsMember);
        Assert.Null(customers["CUST002"].Phone, "invalid phone cleared");
        Assert.False(customers.ContainsKey("CUST000"), "legacy walk-in merged into built-in walk-in");
        Assert.Equal(1, customers.Values.Count(x => x.IsWalkIn));

        var capster = _db.App.Employees.GetAll().Single(e => e.Code == "KAR002");
        Assert.True(capster.IsCapster);
        var svc = _db.App.Services.GetAll().ToDictionary(s => s.Code);
        Assert.Equal(30000m, svc["SVC001"].Commission);
        Assert.Equal(10000m, svc["SVC009"].Commission, "commission > price capped");

        var kasir = _db.App.Auth.Login("kasir", "kasir-uji-99");
        Assert.Equal(RoleCodes.Cashier, kasir.User.RoleCode);
        Assert.True(kasir.User.MustChangePassword);
        Assert.Equal("Capster Satu", kasir.User.EmployeeName);

        Assert.True(r.Warnings.Any(w => w.Contains("PRD002")), "rounding reported");
        Assert.Contains("Import hanya bisa", Assert.Throws<BusinessException>(() => _db.App.LegacyImport.Import(_legacyPath)).Message);
    }

    [Test("INT-21", "Import data lama: file yang bukan database MSPA ditolak tanpa mengubah apa pun")]
    public void ImportRejectsForeignFile()
    {
        var other = Path.Combine(_db.Directory, "lain.sqlite");
        using (var c = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            c.Open();
            c.Execute("CREATE TABLE apa_saja (x INTEGER)");
        }
        Assert.Contains("bukan database MSPA", Assert.Throws<BusinessException>(() => _db.App.LegacyImport.Import(other)).Message);
        Assert.False(_db.App.LegacyImport.TargetHasMasterData());
        Assert.Contains("tidak ditemukan", Assert.Throws<BusinessException>(() => _db.App.LegacyImport.Import(Path.Combine(_db.Directory, "x.sqlite"))).Message);
    }
}
