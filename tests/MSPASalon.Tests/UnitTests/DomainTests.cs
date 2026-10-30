using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Security;
using MSPASalon.Core.Validation;
using MSPASalon.Presentation.Layout;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.UnitTests;

public sealed class DomainTests
{
    [Test("UNIT-01", "Qty: koma dan titik sama-sama desimal; '1.5' = 1,5 (bug lama: 15)")]
    public void QuantityParsing()
    {
        Assert.True(Quantity.TryParse("0,1", out var a)); Assert.Equal(0.1m, a);
        Assert.True(Quantity.TryParse("0.1", out var b)); Assert.Equal(0.1m, b);
        Assert.True(Quantity.TryParse("1.5", out var c)); Assert.Equal(1.5m, c, "legacy D7");
        Assert.True(Quantity.TryParse(" 12 ", out var d)); Assert.Equal(12m, d);
        Assert.True(Quantity.TryParse("-2,5", out var e)); Assert.Equal(-2.5m, e);
        Assert.False(Quantity.TryParse("1.000,5", out _), "two separators");
        Assert.False(Quantity.TryParse("abc", out _));
        Assert.False(Quantity.TryParse("", out _));
        Assert.Equal("0,1", Quantity.Format(0.1m));
        Assert.Equal("2", Quantity.Format(2.0000m));
        Assert.Equal("0,0125", Quantity.Format(0.0125m));
    }

    [Test("UNIT-02", "Qty: maksimal 4 desimal; disimpan sebagai integer x10.000 tanpa error floating-point")]
    public void QuantityPrecisionAndStorage()
    {
        Assert.True(Quantity.TryParse("0,0001", out var q)); Assert.Equal(1L, Quantity.ToStored(q));
        Assert.False(Quantity.TryParse("0,00001", out _), "5 decimals rejected");
        Assert.Throws<ArgumentException>(() => Quantity.ToStored(0.00005m));
        Assert.Equal(1000L, Quantity.ToStored(0.1m));
        Assert.Equal(0.1m, Quantity.FromStored(1000));
        // 3 x 0,1 must be exactly 0,3 (with double this is 0.30000000000000004)
        Assert.Equal(3000L, Quantity.ToStored(0.1m) * 3);
        Assert.Equal(0.3m, Quantity.FromStored(Quantity.ToStored(0.1m) * 3));
    }

    [Test("UNIT-03", "Money: format Rupiah Indonesia diterima, sen/teks ditolak")]
    public void MoneyParsing()
    {
        Assert.True(Money.TryParse("28.000", out var a)); Assert.Equal(28000m, a);
        Assert.True(Money.TryParse("Rp 1.250.000", out var b)); Assert.Equal(1250000m, b);
        Assert.True(Money.TryParse("28000", out var c)); Assert.Equal(28000m, c);
        Assert.True(Money.TryParse("0", out var z)); Assert.Equal(0m, z);
        Assert.False(Money.TryParse("28.000,50", out _), "cents not allowed");
        Assert.False(Money.TryParse("12,5", out _), "cents not allowed");
        Assert.False(Money.TryParse("abc", out _));
        Assert.False(Money.TryParse("", out _));
        Assert.Throws<ArgumentException>(() => Money.ToStored(100.5m));
        Assert.Equal("28.000", Money.Format(28000m));
    }

    [Test("UNIT-04", "Money: nilai baris = round(qty x harga), pembulatan half-away-from-zero")]
    public void MoneyRounding()
    {
        Assert.Equal(1000m, Money.LineGross(0.3333m, 3000m), "999,9 -> 1.000");
        Assert.Equal(1m, Money.LineGross(0.5m, 1m), "0,5 -> 1");
        Assert.Equal(2000m, Money.LineGross(0.1m, 20000m), "Shampo 0,1 BTL x 20.000");
        Assert.Equal(56000m, Money.LineGross(2m, 28000m));
    }

    [Test("UNIT-05", "Nomor dokumen: format YYMM/KODE/0001")]
    public void DocumentNumberFormat()
    {
        Assert.Equal("2610/RG/0001", DocumentType.Format(DocumentType.Registration, new DateTime(2026, 10, 31), 1));
        Assert.Equal("2701/FJ/0123", DocumentType.Format(DocumentType.Sale, new DateTime(2027, 1, 2), 123));
        Assert.Equal("2610", DocumentType.Period(new DateTime(2026, 10, 1)));
    }

    [Test("UNIT-06", "Validasi produk: flag, stok min/max, isi per satuan, harga")]
    public void ProductValidation()
    {
        var ok = new Product { Code = "PRD001", Name = "Shampo", Unit = "BTL", CostPrice = 18000, SellPrice = 28000, IsSellable = true };
        Assert.Equal(0, MasterValidators.Validate(ok).Count, "valid product");

        var noFlag = new Product { Code = "P1", Name = "X", Unit = "PCS", IsSellable = false, IsConsumable = false };
        Assert.Contains("minimal sebagai", string.Join(" ", MasterValidators.Validate(noFlag)));

        var badStock = new Product { Code = "P1", Name = "X", Unit = "PCS", StockMin = 10, StockMax = 5 };
        Assert.Contains("maksimum", string.Join(" ", MasterValidators.Validate(badStock)));

        var content = new Product { Code = "P1", Name = "X", Unit = "BTL", ContentQty = 200 };
        Assert.Contains("Satuan isi", string.Join(" ", MasterValidators.Validate(content)));

        var negative = new Product { Code = "P 1", Name = "", Unit = "", CostPrice = -1, SellPrice = 10.5m };
        var errors = MasterValidators.Validate(negative);
        Assert.True(errors.Count >= 5, $"expected >=5 errors, got {errors.Count}");
    }

    [Test("UNIT-07", "Validasi jasa: komisi <= harga, resep tanpa duplikat, qty > 0")]
    public void ServiceValidation()
    {
        var s = new Service { Code = "SVC001", Name = "Cuci Rambut", Price = 40000, Commission = 50000 };
        s.Materials.Add(new ServiceMaterial { ProductId = 1, ProductName = "Shampo", Qty = 0.1m });
        s.Materials.Add(new ServiceMaterial { ProductId = 1, ProductName = "Shampo", Qty = 0m });
        var text = string.Join(" | ", MasterValidators.Validate(s));
        Assert.Contains("Komisi tidak boleh lebih besar", text);
        Assert.Contains("lebih dari sekali", text);
        Assert.Contains("harus lebih dari 0", text);
    }

    [Test("UNIT-08", "Password: PBKDF2 hash, verifikasi benar/salah, salt unik")]
    public void PasswordHashing()
    {
        var h1 = PasswordHasher.Hash("rahasia1", 1000);
        var h2 = PasswordHasher.Hash("rahasia1", 1000);
        Assert.True(PasswordHasher.Verify("rahasia1", h1.Hash, h1.Salt, h1.Iterations));
        Assert.False(PasswordHasher.Verify("rahasia2", h1.Hash, h1.Salt, h1.Iterations));
        Assert.False(h1.Hash == h2.Hash, "salt must make hashes differ");
        Assert.False(PasswordHasher.Verify("rahasia1", "not-base64", h1.Salt, h1.Iterations));
        Assert.False(h1.Hash.Contains("rahasia1"));
    }

    [Test("UNIT-09", "Responsif: ukuran jendela selalu muat di layar terkecil (1280x720 @150% = 853x440)")]
    public void WindowSizingFitsSmallestScreen()
    {
        // Legacy transaction window preferred 1160x780 — must now fit every target work area.
        var cases = new (double W, double H)[] { (1280, 680), (1366, 728), (1093, 574), (1024, 536), (853, 440), (1920, 1040) };
        foreach (var (w, h) in cases)
        {
            var p = WindowSizing.Initial(new(w, h), new(1160, 780));
            Assert.True(p.Width <= w && p.Height <= h, $"{w}x{h}: window {p.Width}x{p.Height} exceeds work area");
            Assert.True(p.MinWidth <= w && p.MinHeight <= h, $"{w}x{h}: min size exceeds work area");
            Assert.True(p.Width >= p.MinWidth && p.Height >= p.MinHeight, $"{w}x{h}: size below minimum");
        }
        var large = WindowSizing.Initial(new(1920, 1040), new(1160, 780));
        Assert.Equal(1160d, large.Width, "preferred width kept on large screen");
        Assert.Equal(780d, large.Height);
        var tiny = WindowSizing.Initial(new(853, 440), new(1160, 780));
        Assert.Equal(800d, tiny.MinWidth); Assert.Equal(440d, tiny.MinHeight);
        Assert.True(WindowSizing.Initial(new(853, 440), new(1280, 720), maximizeWhenSmall: true).Maximize, "main window maximizes on small screens");
    }

    [Test("UNIT-10", "Responsif: breakpoint ringkas di bawah 1000 DIP")]
    public void CompactBreakpoint()
    {
        Assert.True(WindowSizing.IsCompact(853));
        Assert.True(WindowSizing.IsCompact(999));
        Assert.False(WindowSizing.IsCompact(1000));
        Assert.False(WindowSizing.IsCompact(1280));
    }
}
