using Dapper;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Infrastructure.Services;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

/// <summary>Shared master data for transaction tests: realistic salon set-up with recipes and opening stock.</summary>
public sealed class SalonFixture : IDisposable
{
    public TestDatabase Db { get; } = new();
    public long Customer, Capster1, Capster2, Cashier, Shampoo, Mask, Drink, Wash, Creambath, Haircut, Supplier, Cash, Qris;
    public const string User = "kasir1";

    public SalonFixture()
    {
        var a = Db.App;
        Customer = a.Customers.Save(new Customer { Code = "CST001", Name = "Budi" });
        Capster1 = a.Employees.Save(new Employee { Code = "KAR001", Name = "Capster Satu", IsCapster = true });
        Capster2 = a.Employees.Save(new Employee { Code = "KAR002", Name = "Capster Dua", IsCapster = true });
        Cashier = a.Employees.Save(new Employee { Code = "KAR003", Name = "Kasir", IsCapster = false });
        Shampoo = a.Products.Save(new Product { Code = "PRD001", Name = "Shampo 200ml", Unit = "BTL", CostPrice = 20000, SellPrice = 32000, IsSellable = true, IsConsumable = true });
        Mask = a.Products.Save(new Product { Code = "PRD002", Name = "Masker Rambut", Unit = "BTL", CostPrice = 30000, SellPrice = 45000, IsConsumable = true, IsSellable = false });
        Drink = a.Products.Save(new Product { Code = "PRD003", Name = "Air Mineral", Unit = "BTL", CostPrice = 2500, SellPrice = 5000, IsSellable = true });
        Wash = SaveService("SVC001", "Cuci Rambut", 40000, 12000, (Shampoo, 0.1m));
        Creambath = SaveService("SVC002", "Creambath", 65000, 20000, (Shampoo, 0.1m), (Mask, 0.15m));
        Haircut = SaveService("SVC003", "Potong Rambut", 70000, 30000);
        Supplier = a.Suppliers.Save(new Supplier { Code = "SUP001", Name = "Distributor" });
        Cash = a.PaymentMethods.GetAll().Single(p => p.Code == "TUNAI").Id;
        Qris = a.PaymentMethods.GetAll().Single(p => p.Code == "QRIS").Id;
        // opening stock through a real purchase document
        a.Purchases.Post(Supplier, "INV-1", [new(Shampoo, 2, 20000), new(Mask, 1, 30000), new(Drink, 10, 2500)], null, User);
    }

    private long SaveService(string code, string name, decimal price, decimal commission, params (long Product, decimal Qty)[] recipe)
    {
        var s = new Service { Code = code, Name = name, Price = price, Commission = commission };
        foreach (var (p, q) in recipe) s.Materials.Add(new ServiceMaterial { ProductId = p, Qty = q });
        return Db.App.Services.Save(s);
    }

    public decimal Stock(long productId) => Db.App.Products.Get(productId)!.StockOnHand;

    public decimal LedgerStock(long productId)
    {
        using var c = Db.App.Db.Open();
        return StockLedger.LedgerBalance(c, productId);
    }

    public long Count(string sql, object? p = null)
    {
        using var c = Db.App.Db.Open();
        return c.ExecuteScalar<long>(sql, p);
    }

    public void Dispose() => Db.Dispose();
}

public sealed class SalonFlowTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private Infrastructure.AppServices A => _f.Db.App;
    public void Dispose() => _f.Dispose();

    private PostResult Register(params (long Service, long Capster)[] lines) =>
        A.Registrations.Create(_f.Customer, lines.Select(l => new ServiceLineInput(l.Service, l.Capster)).ToList(), null, SalonFixture.User);

    [Test("M2-01", "Registrasi: nomor RG, status MENUNGGU, snapshot harga & capster; input salah ditolak tanpa memakan nomor")]
    public void CreateRegistration()
    {
        var r = Register((_f.Haircut, _f.Capster1), (_f.Creambath, _f.Capster2));
        Assert.Equal("2610/RG/0001", r.DocNo);
        var reg = A.Registrations.Get(r.Id)!;
        Assert.Equal(RegistrationStatus.Waiting, reg.Status);
        Assert.Equal(135000m, reg.Total);
        Assert.Equal("Capster Dua", reg.Lines[1].EmployeeName, "name stored as name (legacy D11 stored the code)");
        Assert.Contains("Potong Rambut (Capster Satu)", reg.ServiceSummary);

        Assert.Contains("Minimal satu jasa", Assert.Throws<BusinessException>(() =>
            A.Registrations.Create(_f.Customer, [], null, SalonFixture.User)).Message);
        Assert.Contains("bukan capster", Assert.Throws<BusinessException>(() => Register((_f.Haircut, _f.Cashier))).Message);
        Assert.Equal("2610/RG/0002", Register((_f.Wash, _f.Capster1)).DocNo, "failed attempts consumed no number");
    }

    [Test("M2-02", "Registrasi: ubah & batal hanya saat MENUNGGU; alasan wajib")]
    public void UpdateAndCancelRegistration()
    {
        var r = Register((_f.Haircut, _f.Capster1));
        A.Registrations.Update(r.Id, _f.Customer, [new(_f.Wash, _f.Capster2)], "rambut panjang", SalonFixture.User);
        var reg = A.Registrations.Get(r.Id)!;
        Assert.Equal("Cuci Rambut", reg.Lines.Single().ServiceName);
        Assert.Equal("rambut panjang", reg.Notes);

        Assert.Throws<BusinessException>(() => A.Registrations.Cancel(r.Id, "", SalonFixture.User));
        A.Treatments.Start(r.Id, SalonFixture.User);
        Assert.Contains("sedang diproses", Assert.Throws<BusinessException>(() => A.Registrations.Cancel(r.Id, "batal", SalonFixture.User)).Message);
        Assert.Throws<BusinessException>(() => A.Registrations.Update(r.Id, _f.Customer, [new(_f.Haircut, _f.Capster1)], null, SalonFixture.User));

        var r2 = Register((_f.Haircut, _f.Capster1));
        A.Registrations.Cancel(r2.Id, "customer tidak jadi", SalonFixture.User);
        Assert.Equal(RegistrationStatus.Cancelled, A.Registrations.Get(r2.Id)!.Status);
        Assert.Contains("customer tidak jadi", A.Registrations.Get(r2.Id)!.CancelReason);
    }

    [Test("M2-03", "Mulai treatment: nomor = nomor registrasi, jasa tersalin, status DIPROSES, tidak bisa dimulai dua kali")]
    public void StartTreatment()
    {
        var r = Register((_f.Haircut, _f.Capster1), (_f.Wash, _f.Capster2));
        Assert.True(A.Treatments.GetQueue().Any(q => q.Id == r.Id && q.Status == RegistrationStatus.Waiting), "in queue as waiting");
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        Assert.Equal(r.DocNo, t.DocNo, "P1: same number");
        Assert.Equal(2, t.Lines.Count);
        Assert.Equal(110000m, t.Subtotal);
        Assert.Equal(TreatmentStatus.InProgress, t.Status);
        Assert.Equal(RegistrationStatus.InProcess, A.Registrations.Get(r.Id)!.Status);
        Assert.Equal(t.Id, A.Treatments.GetByRegistration(r.Id)!.Id);
        Assert.Throws<BusinessException>(() => A.Treatments.Start(r.Id, SalonFixture.User));
    }

    [Test("M2-04", "Ubah jasa saat dikerjakan: jasa baru harga master, jasa lama tetap harga snapshot (K4)")]
    public void ChangeServicesDuringTreatment()
    {
        var r = Register((_f.Haircut, _f.Capster1));
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        var haircut = A.Services.Get(_f.Haircut)!;
        haircut.Price = 80000;                                    // master price changes after registration
        A.Services.Save(haircut);
        A.Treatments.UpdateLines(t.Id, [new(_f.Haircut, _f.Capster2), new(_f.Creambath, _f.Capster1)], SalonFixture.User);
        t = A.Treatments.Get(t.Id)!;
        Assert.Equal(70000m, t.Lines[0].Price, "existing service keeps registration snapshot");
        Assert.Equal("Capster Dua", t.Lines[0].EmployeeName, "capster changed");
        Assert.Equal(65000m, t.Lines[1].Price, "new service uses master price");
        Assert.Equal(135000m, t.Subtotal);
        Assert.Throws<BusinessException>(() => A.Treatments.UpdateLines(t.Id, [], SalonFixture.User));
    }

    [Test("M2-05", "Selesai & bayar: backflush 0,2 shampo + 0,15 masker, komisi snapshot, kembalian, status LUNAS/SELESAI")]
    public void CompleteTreatment()
    {
        var r = Register((_f.Wash, _f.Capster1), (_f.Creambath, _f.Capster2));
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        var preview = A.Treatments.PreviewBackflush([_f.Wash, _f.Creambath]);
        Assert.Equal(0.2m, preview.Single(p => p.ProductId == _f.Shampoo).Qty);

        _f.Db.Clock.Now = _f.Db.Clock.Now.AddMinutes(45);
        var res = A.Treatments.Complete(t.Id, 5000, new PaymentInput(_f.Cash, 150000, null), SalonFixture.User);
        Assert.Equal(0, res.Warnings.Count);
        t = A.Treatments.Get(t.Id)!;
        Assert.Equal(TreatmentStatus.Paid, t.Status);
        Assert.Equal(105000m, t.Subtotal); Assert.Equal(5000m, t.Discount); Assert.Equal(100000m, t.GrandTotal);
        Assert.Equal(TimeSpan.FromMinutes(45), t.Duration!.Value, "time in/out");
        Assert.Equal(50000m, t.Payment!.ChangeAmount);
        Assert.Equal(RegistrationStatus.Done, A.Registrations.Get(r.Id)!.Status);
        Assert.Equal(12000m, t.Lines[0].Commission); Assert.Equal(20000m, t.Lines[1].Commission);

        Assert.Equal(1.8m, _f.Stock(_f.Shampoo), "2 - 0,2");
        Assert.Equal(0.85m, _f.Stock(_f.Mask), "1 - 0,15");
        Assert.Equal(_f.Stock(_f.Shampoo), _f.LedgerStock(_f.Shampoo), "cache = ledger");

        var wash = A.Services.Get(_f.Wash)!;                      // later commission change must not alter history (§11, legacy D10)
        wash.Commission = 99;
        A.Services.Save(wash);
        Assert.Equal(12000m, A.Treatments.Get(t.Id)!.Lines[0].Commission);
        Assert.Throws<BusinessException>(() => A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 999999, null), SalonFixture.User));
    }

    [Test("M2-06", "Kurang bayar / diskon > subtotal ditolak tanpa efek; QRIS dicatat sebesar total")]
    public void CompleteValidation()
    {
        var r = Register((_f.Wash, _f.Capster1));
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        Assert.Contains("kurang dari total", Assert.Throws<BusinessException>(() =>
            A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 30000, null), SalonFixture.User)).Message);
        Assert.Throws<BusinessException>(() => A.Treatments.Complete(t.Id, 50000, new PaymentInput(_f.Cash, 50000, null), SalonFixture.User));
        Assert.Equal(TreatmentStatus.InProgress, A.Treatments.Get(t.Id)!.Status, "still in progress");
        Assert.Equal(2m, _f.Stock(_f.Shampoo), "no backflush");
        Assert.Equal(0L, _f.Count("SELECT COUNT(*) FROM payment WHERE source_type = 'TREATMENT'"));

        A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Qris, 0, "QR-123"), SalonFixture.User);
        var p = A.Treatments.Get(t.Id)!.Payment!;
        Assert.Equal(40000m, p.AmountReceived); Assert.Equal(0m, p.ChangeAmount); Assert.Equal("QR-123", p.Reference);
    }

    [Test("M2-07", "Backflush tidak menggagalkan treatment saat stok kurang: stok minus + peringatan (A4)")]
    public void BackflushNegativeStock()
    {
        for (var i = 0; i < 7; i++)
        {
            var r = Register((_f.Creambath, _f.Capster1));
            var t = A.Treatments.Start(r.Id, SalonFixture.User);
            var res = A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 65000, null), SalonFixture.User);
            if (i == 6)
            {
                Assert.Equal(-0.05m, _f.Stock(_f.Mask), "1 - 7 x 0,15");
                Assert.Contains("Masker Rambut", res.Warnings.Single());
            }
        }
    }

    [Test("M2-08", "Batal treatment LUNAS: butuh hak akses; stok & pembayaran dikembalikan, komisi 0, nomor tidak dipakai ulang")]
    public void CancelPaidTreatment()
    {
        var r = Register((_f.Creambath, _f.Capster1));
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 65000, null), SalonFixture.User);
        Assert.Contains("tidak memiliki hak", Assert.Throws<BusinessException>(() =>
            A.Treatments.Cancel(t.Id, "salah input", allowPosted: false, SalonFixture.User)).Message);

        A.Treatments.Cancel(t.Id, "salah input", allowPosted: true, "admin");
        t = A.Treatments.Get(t.Id)!;
        Assert.Equal(TreatmentStatus.Cancelled, t.Status);
        Assert.Null(t.Payment, "payment removed");
        Assert.Equal(0m, t.Lines.Single().Commission);
        Assert.Equal(2m, _f.Stock(_f.Shampoo)); Assert.Equal(1m, _f.Stock(_f.Mask));
        Assert.Equal(_f.Stock(_f.Mask), _f.LedgerStock(_f.Mask));
        Assert.Equal(RegistrationStatus.Cancelled, A.Registrations.Get(r.Id)!.Status);
        Assert.Equal("2610/RG/0002", Register((_f.Wash, _f.Capster1)).DocNo, "cancelled number not reused");
    }

    [Test("M2-09", "Batal treatment yang masih DIKERJAKAN: tanpa efek stok")]
    public void CancelInProgressTreatment()
    {
        var r = Register((_f.Wash, _f.Capster1));
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        A.Treatments.Cancel(t.Id, "customer pulang", allowPosted: false, SalonFixture.User);
        Assert.Equal(TreatmentStatus.Cancelled, A.Treatments.Get(t.Id)!.Status);
        Assert.False(A.Treatments.GetQueue().Any(q => q.Id == r.Id), "removed from queue");
        Assert.Throws<BusinessException>(() => A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 40000, null), SalonFixture.User));
    }

    [Test("M2-10", "Rollback atomik: kegagalan saat tulis pembayaran membatalkan seluruh posting treatment")]
    public void CompleteRollsBackOnFailure()
    {
        var r = Register((_f.Creambath, _f.Capster1));
        var t = A.Treatments.Start(r.Id, SalonFixture.User);
        using (var c = A.Db.Open())
            c.Execute("CREATE TRIGGER fail_payment BEFORE INSERT ON payment BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 65000, null), SalonFixture.User));
        t = A.Treatments.Get(t.Id)!;
        Assert.Equal(TreatmentStatus.InProgress, t.Status, "status unchanged");
        Assert.Equal(0m, t.Lines.Single().Commission, "commission not written");
        Assert.Equal(2m, _f.Stock(_f.Shampoo), "backflush rolled back");
        Assert.Equal(0L, _f.Count("SELECT COUNT(*) FROM stock_ledger WHERE source_type = 'TREATMENT'"));
        Assert.Equal(RegistrationStatus.InProcess, A.Registrations.Get(r.Id)!.Status);
    }
}
