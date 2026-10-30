using Dapper;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class InventoryFlowTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private Infrastructure.AppServices A => _f.Db.App;
    public void Dispose() => _f.Dispose();

    [Test("M3-01", "Penjualan: total & diskon baris, stok keluar, pembayaran, header = jumlah detail")]
    public void PostSale()
    {
        var r = A.Sales.Post(_f.Customer, [new(_f.Shampoo, 1, 32000, 2000), new(_f.Drink, 3, 5000, 0)], "member",
            new PaymentInput(_f.Cash, 50000, null), SalonFixture.User);
        Assert.Equal("2610/FJ/0001", r.DocNo);
        var s = A.Sales.Get(r.Id)!;
        Assert.Equal(47000m, s.Subtotal); Assert.Equal(2000m, s.DiscountTotal); Assert.Equal(45000m, s.GrandTotal);
        Assert.Equal(s.GrandTotal, s.Lines.Sum(l => l.LineTotal), "header = Σ detail (legacy reconciliation)");
        Assert.Equal(5000m, s.Payment!.ChangeAmount);
        Assert.Equal(1m, _f.Stock(_f.Shampoo)); Assert.Equal(7m, _f.Stock(_f.Drink));
        Assert.Equal(20000m, s.Lines[0].CostPrice, "cost snapshot for HPP");
        Assert.Equal(DocStatus.Posted, s.Status);
    }

    [Test("M3-02", "Penjualan ditolak: stok kurang (akumulasi baris), produk tidak dijual, kurang bayar; nomor tidak terpakai")]
    public void SaleValidation()
    {
        var pay = new PaymentInput(_f.Cash, 1_000_000, null);
        Assert.Contains("Stok Shampo 200ml tidak cukup", Assert.Throws<BusinessException>(() =>
            A.Sales.Post(_f.Customer, [new(_f.Shampoo, 1.5m, 32000, 0), new(_f.Shampoo, 1, 32000, 0)], null, pay, SalonFixture.User)).Message);
        Assert.Contains("tidak ditandai 'Dijual'", Assert.Throws<BusinessException>(() =>
            A.Sales.Post(_f.Customer, [new(_f.Mask, 0.5m, 45000, 0)], null, pay, SalonFixture.User)).Message);
        Assert.Throws<BusinessException>(() =>
            A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 1000, null), SalonFixture.User));
        Assert.Equal(0L, _f.Count("SELECT COUNT(*) FROM sale"));
        Assert.Equal(10m, _f.Stock(_f.Drink));
        Assert.Equal("2610/FJ/0001", A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, pay, SalonFixture.User).DocNo);
    }

    [Test("M3-03", "Batal penjualan: stok kembali, pembayaran dihapus, status BATAL; batal dua kali ditolak")]
    public void CancelSale()
    {
        var r = A.Sales.Post(_f.Customer, [new(_f.Drink, 4, 5000, 0)], null, new PaymentInput(_f.Qris, 0, null), SalonFixture.User);
        Assert.Equal(6m, _f.Stock(_f.Drink));
        A.Sales.Cancel(r.Id, "salah produk", "admin");
        var s = A.Sales.Get(r.Id)!;
        Assert.Equal(DocStatus.Cancelled, s.Status);
        Assert.Null(s.Payment);
        Assert.Equal(10m, _f.Stock(_f.Drink));
        Assert.Equal(_f.Stock(_f.Drink), _f.LedgerStock(_f.Drink));
        Assert.Throws<BusinessException>(() => A.Sales.Cancel(r.Id, "lagi", "admin"));
    }

    [Test("M3-04", "Rollback atomik penjualan: gagal di tengah → tidak ada header/baris/stok, nomor tidak terpakai")]
    public void SaleRollback()
    {
        using (var c = A.Db.Open())
            c.Execute("CREATE TRIGGER fail_payment BEFORE INSERT ON payment BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            A.Sales.Post(_f.Customer, [new(_f.Drink, 2, 5000, 0)], null, new PaymentInput(_f.Cash, 10000, null), SalonFixture.User));
        Assert.Equal(0L, _f.Count("SELECT COUNT(*) FROM sale"));
        Assert.Equal(0L, _f.Count("SELECT COUNT(*) FROM sale_line"));
        Assert.Equal(10m, _f.Stock(_f.Drink));
        using (var c = A.Db.Open()) c.Execute("DROP TRIGGER fail_payment");
        Assert.Equal("2610/FJ/0001", A.Sales.Post(_f.Customer, [new(_f.Drink, 2, 5000, 0)], null,
            new PaymentInput(_f.Cash, 10000, null), SalonFixture.User).DocNo);
    }

    [Test("M3-05", "Pembelian: stok masuk, harga pokok master tidak berubah (A8); batal ditolak bila barang sudah terpakai")]
    public void PurchaseAndCancel()
    {
        var r = A.Purchases.Post(_f.Supplier, "INV-2", [new(_f.Drink, 5, 2700)], null, SalonFixture.User);
        Assert.Equal("2610/FB/0002", r.DocNo, "fixture already posted FB/0001");
        Assert.Equal(15m, _f.Stock(_f.Drink));
        Assert.Equal(2500m, A.Products.Get(_f.Drink)!.CostPrice, "master cost unchanged");
        Assert.Equal(13500m, A.Purchases.Get(r.Id)!.GrandTotal);

        A.Sales.Post(_f.Customer, [new(_f.Drink, 12, 5000, 0)], null, new PaymentInput(_f.Cash, 60000, null), SalonFixture.User);
        Assert.Contains("stok akan minus", Assert.Throws<BusinessException>(() => A.Purchases.Cancel(r.Id, "retur", "admin")).Message);
        Assert.Equal(3m, _f.Stock(_f.Drink), "rejected cancel changed nothing");
        Assert.Equal(DocStatus.Posted, A.Purchases.Get(r.Id)!.Status);

        var r2 = A.Purchases.Post(_f.Supplier, null, [new(_f.Drink, 1, 2500)], null, SalonFixture.User);
        A.Purchases.Cancel(r2.Id, "double input", "admin");
        Assert.Equal(3m, _f.Stock(_f.Drink));
        Assert.Throws<BusinessException>(() => A.Purchases.Post(_f.Supplier, null, [], null, SalonFixture.User));
    }

    [Test("M3-06", "Stock adjustment: qty +/−, nilai harga pokok, stok minus ditolak, batal")]
    public void Adjustment()
    {
        var r = A.Adjustments.Post("Stock opname Oktober", [new(_f.Shampoo, -0.35m), new(_f.Drink, 2)], SalonFixture.User);
        var a = A.Adjustments.Get(r.Id)!;
        Assert.Equal(-7000m + 5000m, a.TotalValue, "-0,35 x 20.000 + 2 x 2.500");
        Assert.Equal(1.65m, _f.Stock(_f.Shampoo));
        Assert.Contains("minus", Assert.Throws<BusinessException>(() =>
            A.Adjustments.Post("opname", [new(_f.Mask, -2)], SalonFixture.User)).Message);
        Assert.Throws<BusinessException>(() => A.Adjustments.Post("", [new(_f.Mask, 1)], SalonFixture.User));
        Assert.Throws<BusinessException>(() => A.Adjustments.Post("opname", [new(_f.Mask, 0)], SalonFixture.User));
        A.Adjustments.Cancel(r.Id, "salah hitung", "admin");
        Assert.Equal(2m, _f.Stock(_f.Shampoo));
        Assert.Equal(DocStatus.Cancelled, A.Adjustments.Get(r.Id)!.Status);
    }

    [Test("M3-07", "Kartu stok: saldo awal, mutasi berjalan, saldo akhir = stok produk = ledger")]
    public void StockCard()
    {
        var day1 = _f.Db.Clock.Now;
        _f.Db.Clock.Now = day1.AddDays(1);
        A.Sales.Post(_f.Customer, [new(_f.Shampoo, 0.5m, 32000, 0)], null, new PaymentInput(_f.Cash, 16000, null), SalonFixture.User);
        var reg = A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, SalonFixture.User);
        var t = A.Treatments.Start(reg.Id, SalonFixture.User);
        A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 40000, null), SalonFixture.User);

        var card = A.StockCards.Get(_f.Shampoo, day1.AddDays(1), day1.AddDays(1));
        Assert.Equal(2m, card.Opening, "purchase on day 1 is the opening of day 2");
        Assert.Equal(2, card.Rows.Count);
        Assert.Equal(1.5m, card.Rows[0].Balance);
        Assert.Equal(StockSource.Treatment, card.Rows[1].SourceType);
        Assert.Equal(1.4m, card.Closing);
        Assert.Equal(_f.Stock(_f.Shampoo), card.Closing);
        Assert.Equal(_f.LedgerStock(_f.Shampoo), card.Closing);
        var all = A.StockCards.Get(_f.Shampoo, day1, day1.AddDays(1));
        Assert.Equal(0m, all.Opening); Assert.Equal(3, all.Rows.Count);
    }
}
