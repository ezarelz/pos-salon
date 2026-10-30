using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.UnitTests;

public sealed class TransactionRuleTests
{
    [Test("UNIT-11", "Baris penjualan: gross = round(qty x harga), diskon <= gross, qty/harga valid")]
    public void SaleLineRules()
    {
        var e = new List<string>();
        Assert.Equal((56000m, 55000m), TransactionRules.SaleLine(2, 28000, 1000, "B1", e));
        Assert.Equal((2000m, 2000m), TransactionRules.SaleLine(0.1m, 20000, 0, "B2", e));
        Assert.Equal(0, e.Count, "no errors for valid lines");
        TransactionRules.SaleLine(1, 5000, 6000, "B3", e);
        Assert.Contains("melebihi nilai baris", string.Join(" ", e));
        e.Clear();
        TransactionRules.SaleLine(0, 5000, 0, "B4", e);
        TransactionRules.SaleLine(1, -1, 0, "B5", e);
        TransactionRules.SaleLine(0.00001m, 5000, 0, "B6", e);
        Assert.Equal(3, e.Count, "qty 0, negative price, 5 decimals");
    }

    [Test("UNIT-12", "Total treatment: diskon header 0..subtotal (P4)")]
    public void TreatmentTotalRules()
    {
        var e = new List<string>();
        Assert.Equal(100000m, TransactionRules.TreatmentTotal(105000, 5000, e));
        Assert.Equal(0, e.Count);
        TransactionRules.TreatmentTotal(105000, 110000, e);
        TransactionRules.TreatmentTotal(105000, -1, e);
        TransactionRules.TreatmentTotal(105000, 10.5m, e);
        Assert.Equal(3, e.Count);
    }

    [Test("UNIT-13", "Pembayaran: tunai pas/lebih/kurang; non-tunai selalu = total tanpa kembalian (K8)")]
    public void PaymentRules()
    {
        var cash = new PaymentMethod { Name = "Tunai", IsCash = true, IsActive = true };
        var qris = new PaymentMethod { Name = "QRIS", IsCash = false, IsActive = true };
        Assert.Equal((28000m, 0m), TransactionRules.ResolvePayment(cash, 28000, 28000), "exact");
        Assert.Equal((50000m, 22000m), TransactionRules.ResolvePayment(cash, 28000, 50000), "overpayment → change");
        Assert.Contains("kurang dari total", Assert.Throws<BusinessException>(() => TransactionRules.ResolvePayment(cash, 28000, 20000)).Message);
        Assert.Equal((28000m, 0m), TransactionRules.ResolvePayment(qris, 28000, 0), "QRIS ignores received input");
        Assert.Equal((28000m, 0m), TransactionRules.ResolvePayment(qris, 28000, 99999), "QRIS never gives change");
        Assert.Throws<BusinessException>(() => TransactionRules.ResolvePayment(new PaymentMethod { Name = "X", IsActive = false }, 1, 1));
        Assert.Contains("Alasan", Assert.Throws<BusinessException>(() => TransactionRules.RequireReason(" ")).Message);
    }

    [Test("UNIT-14", "Backflush: konsumsi = jumlah resep semua baris jasa; jasa dobel dihitung dua kali; tanpa resep = 0")]
    public void BackflushAggregation()
    {
        const long shampoo = 1, mask = 2, wash = 10, creambath = 11, haircut = 12;
        var recipes = new Dictionary<long, IReadOnlyList<(long, decimal)>>
        {
            [wash] = [(shampoo, 0.1m)],
            [creambath] = [(shampoo, 0.1m), (mask, 0.15m)],
        };
        var r = TransactionRules.Backflush([wash, creambath, haircut], recipes);
        Assert.Equal(0.2m, r[shampoo]);
        Assert.Equal(0.15m, r[mask]);
        Assert.Equal(2, r.Count, "haircut has no recipe");
        var twice = TransactionRules.Backflush([wash, wash], recipes);
        Assert.Equal(0.2m, twice[shampoo]);
    }

    [Test("UNIT-15", "Nota thermal: lebar 32 (58 mm) / 48 (80 mm), total & kembalian tercetak, status batal")]
    public void ReceiptLayout()
    {
        var t = new Treatment
        {
            DocNo = "2610/RG/0001", TimeIn = new DateTime(2026, 10, 31, 10, 0, 0), PostedAt = new DateTime(2026, 10, 31, 11, 5, 0),
            PostedBy = "kasir1", CustomerName = "Pelanggan Dengan Nama Yang Cukup Panjang Sekali", Subtotal = 135000, Discount = 5000,
            GrandTotal = 130000, Status = TreatmentStatus.Paid,
            Lines =
            [
                new ServiceLine { ServiceName = "Creambath Premium Dengan Masker Rambut Khusus", EmployeeName = "Capster A", Price = 65000 },
                new ServiceLine { ServiceName = "Potong Rambut", EmployeeName = "Capster B", Price = 70000 },
            ],
            Payment = new Payment { MethodName = "Tunai", IsCash = true, AmountDue = 130000, AmountReceived = 150000, ChangeAmount = 20000 },
        };
        var branch = new Branch { Name = "Salon Contoh", Address = "Jl. Contoh No. 1, Bekasi", Phone = "021-000", PaperWidthMm = 58 };
        foreach (var width in new[] { 32, 48 })
        {
            var lines = ReceiptBuilder.Build(ReceiptBuilder.ForTreatment(t, branch), width);
            Assert.True(lines.All(l => l.Length <= width), $"width {width}: a line is too long: {lines.FirstOrDefault(l => l.Length > width)}");
            Assert.True(lines.Any(l => l.StartsWith("TOTAL") && l.EndsWith("130.000")), "total line");
            Assert.True(lines.Any(l => l.StartsWith("Kembali") && l.EndsWith("20.000")), "change line");
            Assert.True(lines.Any(l => l.StartsWith("Diskon") && l.EndsWith("-5.000")), "discount line");
            Assert.False(lines.Any(l => l.Contains("Capster")), "capster not printed");
        }
        Assert.Equal(32, ReceiptBuilder.CharsPerLine(58));
        Assert.Equal(48, ReceiptBuilder.CharsPerLine(80));
        t.Status = TreatmentStatus.Cancelled;
        Assert.True(ReceiptBuilder.Build(ReceiptBuilder.ForTreatment(t, branch), 32).Any(l => l.Contains("DIBATALKAN")));
    }

    [Test("UNIT-16", "Nota: baris 'Jasa x qty  harga', tanpa nama capster; jasa sama digabung; header nama → alamat → telp")]
    public void ReceiptItemFormat()
    {
        var t = new Treatment
        {
            DocNo = "2610/RG/0001", TimeIn = new DateTime(2026, 10, 31, 2, 0, 0), PostedAt = new DateTime(2026, 10, 31, 2, 32, 0),
            PostedBy = "admin", CustomerName = "Ezar", Subtotal = 140000, GrandTotal = 140000, Status = TreatmentStatus.Paid,
            Lines =
            [
                new ServiceLine { ServiceName = "Barber", EmployeeName = "Capster Satu", Price = 60000 },
                new ServiceLine { ServiceName = "Cuci Rambut", EmployeeName = "Capster Satu", Price = 40000 },
                new ServiceLine { ServiceName = "Cuci Rambut", EmployeeName = "Capster Dua", Price = 40000 },
                new ServiceLine { ServiceName = "Creambath Premium Dengan Masker Rambut Khusus", EmployeeName = "Capster Dua", Price = 0 },
            ],
            Payment = new Payment { MethodName = "QRIS", AmountDue = 140000, AmountReceived = 140000 },
        };
        var branch = new Branch { Name = "Salon Contoh", Address = "Jl. Contoh No. 1", Phone = "021-000" };
        var lines = ReceiptBuilder.Build(ReceiptBuilder.ForTreatment(t, branch), 32);

        Assert.Equal("Barber x 1                60.000", lines.Single(l => l.StartsWith("Barber")));
        Assert.Equal("Cuci Rambut x 2           80.000", lines.Single(l => l.StartsWith("Cuci")), "jasa sama → qty 2");
        Assert.False(lines.Any(l => l.Contains("Capster")), "nama capster tidak dicetak");
        Assert.True(lines.All(l => l.Length <= 32));
        var longItem = lines.SkipWhile(l => !l.StartsWith("Creambath")).Take(2).ToList();
        Assert.True(longItem[1].EndsWith(" 0") && longItem[1].Contains("x 1"), "nama panjang dibungkus, harga di baris terakhir");

        var name = lines.ToList().FindIndex(l => l.Trim() == "SALON CONTOH");
        Assert.Equal(0, name, "nama salon baris pertama (logo dicetak sebagai gambar di atasnya)");
        Assert.Equal("Jl. Contoh No. 1", lines[1].Trim());
        Assert.Equal("Telp 021-000", lines[2].Trim());

        var sale = new Sale
        {
            DocNo = "2610/FJ/0001", DocAt = t.TimeIn, CustomerName = "Ezar", Subtotal = 10000, DiscountTotal = 1000, GrandTotal = 9000,
            Lines = [new ProductLine { ProductName = "Air Mineral", Unit = "BTL", Qty = 2, Price = 5000, Discount = 1000, LineTotal = 9000 }],
        };
        var saleLines = ReceiptBuilder.Build(ReceiptBuilder.ForSale(sale, branch), 32);
        Assert.Equal("Air Mineral x 2           10.000", saleLines.Single(l => l.StartsWith("Air")));
        Assert.True(saleLines.Any(l => l.Trim().StartsWith("Diskon") && l.EndsWith("-1.000")));
    }
}
