using Dapper;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class MasterDataTests : IDisposable
{
    private readonly TestDatabase _db = new();
    public void Dispose() => _db.Dispose();

    private long Shampoo(bool consumable = true) => _db.App.Products.Save(new Product
    {
        Code = "PRD001", Name = "Shampo 200ml", Unit = "BTL", ContentQty = 200, ContentUnit = "ml",
        CostPrice = 20000, SellPrice = 32000, IsSellable = true, IsConsumable = consumable,
    });

    [Test("INT-10", "Customer: simpan, ubah, kode dinormalisasi, kode ganda (beda huruf besar/kecil) ditolak")]
    public void CustomerCrud()
    {
        var svc = _db.App.Customers;
        var id = svc.Save(new Customer { Code = " cst001 ", Name = " Budi ", Phone = "0812-3456", Gender = Gender.Male });
        var c = svc.Get(id)!;
        Assert.Equal("CST001", c.Code); Assert.Equal("Budi", c.Name);
        c.Name = "Budi Santoso"; c.IsMember = true;
        svc.Save(c);
        Assert.Equal("Budi Santoso", svc.Get(id)!.Name);
        Assert.True(svc.Get(id)!.IsMember);

        var ex = Assert.Throws<BusinessException>(() => svc.Save(new Customer { Code = "Cst001", Name = "Lain" }));
        Assert.Contains("sudah dipakai", ex.Message);
        Assert.Equal("CST002", svc.SuggestNextCode());
        Assert.Throws<BusinessException>(() => svc.Save(new Customer { Code = "", Name = "" }));
    }

    [Test("INT-11", "Produk: qty 4 desimal & uang tersimpan sebagai INTEGER (bukan REAL/TEXT)")]
    public void ProductStorageTypes()
    {
        var id = _db.App.Products.Save(new Product
        {
            Code = "PRD001", Name = "Masker", Unit = "pcs", CostPrice = 12000, SellPrice = 22000,
            StockMin = 0.0125m, StockMax = 10.5m, ContentQty = 250, ContentUnit = "gr", IsSellable = true,
        });
        var p = _db.App.Products.Get(id)!;
        Assert.Equal("PCS", p.Unit, "unit normalized");
        Assert.Equal(0.0125m, p.StockMin); Assert.Equal(10.5m, p.StockMax); Assert.Equal(250m, p.ContentQty);
        Assert.Equal(12000m, p.CostPrice); Assert.Equal(0m, p.StockOnHand);
        using var c = _db.App.Db.Open();
        var types = c.QuerySingle<(string A, string B, string C)>(
            "SELECT typeof(cost_price), typeof(stock_min_x), typeof(sell_price) FROM product WHERE id = @id", new { id });
        Assert.Equal(("integer", "integer", "integer"), types);
        Assert.Equal(125L, c.ExecuteScalar<long>("SELECT stock_min_x FROM product WHERE id = @id", new { id }));
    }

    [Test("INT-12", "Resep jasa: tersimpan & terbaca; produk bukan bahan ditolak; simpan gagal = rollback total")]
    public void ServiceRecipe()
    {
        var shampoo = Shampoo();
        var drink = _db.App.Products.Save(new Product { Code = "PRD002", Name = "Air Mineral", Unit = "BTL", IsSellable = true });

        var svc = new Service { Code = "SVC001", Name = "Cuci Rambut", Price = 40000, Commission = 12000 };
        svc.Materials.Add(new ServiceMaterial { ProductId = shampoo, Qty = 0.1m });
        var id = _db.App.Services.Save(svc);
        var loaded = _db.App.Services.Get(id)!;
        Assert.Equal(1, loaded.Materials.Count);
        Assert.Equal(0.1m, loaded.Materials[0].Qty);
        Assert.Equal("BTL", loaded.Materials[0].Unit);

        var bad = new Service { Code = "SVC002", Name = "Creambath", Price = 65000, Commission = 20000 };
        bad.Materials.Add(new ServiceMaterial { ProductId = drink, Qty = 0.1m });
        var ex = Assert.Throws<BusinessException>(() => _db.App.Services.Save(bad));
        Assert.Contains("bukan bahan habis pakai", ex.Message);
        using var c = _db.App.Db.Open();
        Assert.Equal(0L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM service WHERE code = 'SVC002'"), "header rolled back");

        // replacing the recipe
        loaded.Materials[0].Qty = 0.15m;
        _db.App.Services.Save(loaded);
        Assert.Equal(0.15m, _db.App.Services.Get(id)!.Materials.Single().Qty);
    }

    [Test("INT-13", "Produk yang dipakai resep tidak boleh dilepas flag 'bahan habis pakai'")]
    public void ConsumableFlagProtectedByRecipe()
    {
        var shampoo = Shampoo();
        var svc = new Service { Code = "SVC001", Name = "Cuci Rambut", Price = 40000 };
        svc.Materials.Add(new ServiceMaterial { ProductId = shampoo, Qty = 0.1m });
        _db.App.Services.Save(svc);
        var p = _db.App.Products.Get(shampoo)!;
        p.IsConsumable = false;
        var ex = Assert.Throws<BusinessException>(() => _db.App.Products.Save(p));
        Assert.Contains("Cuci Rambut", ex.Message);
    }

    [Test("INT-14", "Hapus master diperbolehkan (K10): resep ikut terhapus, kategori produk jadi kosong, ada peringatan")]
    public void DeleteMasters()
    {
        var cat = _db.App.Categories.Save(new Category { Code = "KTG001", Name = "Perawatan Rambut" });
        var shampoo = _db.App.Products.Save(new Product { Code = "PRD001", Name = "Shampo", Unit = "BTL", CategoryId = cat, IsConsumable = true, IsSellable = true });
        var svc = new Service { Code = "SVC001", Name = "Cuci Rambut", Price = 40000 };
        svc.Materials.Add(new ServiceMaterial { ProductId = shampoo, Qty = 0.1m });
        var svcId = _db.App.Services.Save(svc);

        Assert.Contains("1 produk", _db.App.Categories.DeleteWarning(cat));
        _db.App.Categories.Delete(cat);
        Assert.Null(_db.App.Products.Get(shampoo)!.CategoryId, "category set null");

        Assert.Contains("Cuci Rambut", _db.App.Products.DeleteWarning(shampoo));
        _db.App.Products.Delete(shampoo);
        Assert.Equal(0, _db.App.Services.Get(svcId)!.Materials.Count, "recipe line cascaded");
        Assert.Throws<BusinessException>(() => _db.App.Products.Delete(shampoo));   // already gone
    }

    [Test("INT-15", "Perlindungan data wajib: walk-in terakhir & metode Tunai terakhir tidak bisa dihapus/dinonaktifkan")]
    public void ProtectedReferenceRows()
    {
        var walkIn = _db.App.Customers.GetAll().Single(c => c.IsWalkIn);
        Assert.Throws<BusinessException>(() => _db.App.Customers.Delete(walkIn.Id));
        walkIn.IsActive = false;
        Assert.Throws<BusinessException>(() => _db.App.Customers.Save(walkIn));

        var cash = _db.App.PaymentMethods.GetAll().Single(p => p.IsCash);
        Assert.Throws<BusinessException>(() => _db.App.PaymentMethods.Delete(cash.Id));
        cash.IsCash = false;
        Assert.Throws<BusinessException>(() => _db.App.PaymentMethods.Save(cash));

        var qris = _db.App.PaymentMethods.GetAll().Single(p => p.Code == "QRIS");
        Assert.Null(_db.App.PaymentMethods.Delete(qris.Id), "non-cash method can be deleted");
    }
}
