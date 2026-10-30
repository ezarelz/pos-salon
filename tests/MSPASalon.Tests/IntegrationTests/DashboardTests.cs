using MSPASalon.Core.Entities;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Transactions;
using MSPASalon.Tests.Framework;
using MSPASalon.Tests.UnitTests;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class DashboardTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private Infrastructure.AppServices A => _f.Db.App;
    public void Dispose() => _f.Dispose();

    /// <summary>
    /// Yesterday: 1 product sale (10.000 cash).
    /// Today: treatment A (Cuci, Capster 1) 40.000 cash; treatment B (Creambath C1 + Potong C2) 135.000 − 5.000 QRIS;
    /// product sale 5.000 QRIS; product sale 5.000 cash then CANCELLED; 1 registration waiting; 1 treatment in progress.
    /// </summary>
    private void Seed()
    {
        var clock = _f.Db.Clock;
        clock.Now = new DateTime(2026, 10, 30, 15, 0, 0);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 2, 5000, 0)], null, new PaymentInput(_f.Cash, 10000, null), "admin");

        clock.Now = new DateTime(2026, 10, 31, 10, 0, 0);
        var a = A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, "admin");
        var ta = A.Treatments.Start(a.Id, "admin");
        clock.Now = clock.Now.AddMinutes(40);
        A.Treatments.Complete(ta.Id, 0, new PaymentInput(_f.Cash, 50000, null), "admin");

        clock.Now = clock.Now.AddMinutes(5);
        var b = A.Registrations.Create(_f.Customer, [new(_f.Creambath, _f.Capster1), new(_f.Haircut, _f.Capster2)], null, "admin");
        var tb = A.Treatments.Start(b.Id, "admin");
        clock.Now = clock.Now.AddMinutes(60);
        A.Treatments.Complete(tb.Id, 5000, new PaymentInput(_f.Qris, 0, "QR-1"), "admin");

        clock.Now = clock.Now.AddMinutes(5);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Qris, 0, null), "admin");
        clock.Now = clock.Now.AddMinutes(5);
        var cancelled = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
        A.Sales.Cancel(cancelled.Id, "salah input", "admin");

        A.Registrations.Create(_f.Customer, [new(_f.Haircut, _f.Capster2)], null, "admin");          // waiting
        var d = A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster2)], null, "admin");
        A.Treatments.Start(d.Id, "admin");                                                         // in process
        clock.Now = clock.Now.AddMinutes(1);
    }

    [Test("DASH-01", "Dashboard harian: pendapatan jasa/produk, client selesai, antrian, rekap metode, capster; dokumen batal tidak dihitung")]
    public void DailySummary()
    {
        Seed();
        var s = A.Dashboard.GetSummary(_f.Db.Clock.Now);
        Assert.Equal(170000m, s.ServiceRevenue, "40.000 + 130.000 (setelah diskon)");
        Assert.Equal(5000m, s.ProductRevenue, "penjualan batal tidak dihitung");
        Assert.Equal(175000m, s.TotalRevenue);
        Assert.Equal(10000m, s.PreviousDayRevenue);
        Assert.Equal(2, s.ClientsServed);
        Assert.Equal(1, s.SalesCount);
        Assert.Equal(3, s.TransactionCount);
        Assert.Equal(85000m, s.AveragePerClient);
        Assert.Equal(1, s.Waiting);
        Assert.Equal(1, s.InProcess);
        Assert.Equal(1, s.CancelledCount);

        Assert.Equal(2, s.Methods.Count, "QRIS + Tunai; pembayaran dokumen batal sudah terhapus");
        Assert.Equal(135000m, s.Methods.Single(m => m.MethodName == "QRIS").Amount);
        Assert.Equal(2, s.Methods.Single(m => m.MethodName == "QRIS").Count);
        Assert.Equal(40000m, s.Methods.Single(m => m.IsCash).Amount, "kas laci = amount due, bukan uang diterima");
        Assert.Equal(s.TotalRevenue, s.Methods.Sum(m => m.Amount), "rekap metode = total pendapatan");

        Assert.Equal(3, s.Recent.Count);
        Assert.Equal("Penjualan", s.Recent[0].Kind, "terbaru di atas");
        Assert.Equal("QRIS", s.Recent[1].MethodName);

        var c1 = s.Capsters.Single(c => c.Name == "Capster Satu");
        Assert.Equal(2, c1.Clients);
        Assert.Equal(105000m, c1.Amount, "Cuci 40.000 + Creambath 65.000");
        Assert.Equal(1, s.Capsters.Single(c => c.Name == "Capster Dua").Services, "treatment yang masih dikerjakan tidak dihitung");

        var tomorrow = A.Dashboard.GetSummary(_f.Db.Clock.Now.AddDays(1));
        Assert.Equal(0m, tomorrow.TotalRevenue);
        Assert.Equal(175000m, tomorrow.PreviousDayRevenue);
    }

    [Test("DASH-02", "Dashboard VM: teks KPI, tanggal Indonesia, tautan cepat membuka layar; toggle mode gelap")]
    public void DashboardViewModelAndTheme()
    {
        Seed();
        var theme = new FakeTheme();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(ViewModelTests.Services(A, theme: theme), dialogs, A.Auth.Login("admin", "admin"));
        var dash = (DashboardViewModel)shell.SelectedPage!;
        Assert.Equal("Sabtu, 31 Oktober 2026", dash.DateText);
        Assert.Equal("Rp 175.000", dash.RevenueText);
        Assert.Equal("Jasa Rp 170.000 · Produk Rp 5.000", dash.RevenueDetailText);
        Assert.Contains("▲", dash.PreviousDayText);
        Assert.Equal("2", dash.ClientsText);
        Assert.Equal("1 menunggu · 1 dikerjakan", dash.QueueDetailText);
        Assert.Contains("1 batal", dash.TransactionsDetailText);
        Assert.Equal("Rp 40.000", dash.CashText);
        Assert.Equal(3, dash.QuickLinks.Count);

        dash.OpenCommand.Execute(dash.QuickLinks.Single(l => l.Key == "trans:treatment"));
        Assert.True(shell.SelectedPage is TreatmentPageViewModel, "tautan cepat membuka Proses Treatment");

        Assert.Equal("Mode Gelap", shell.ThemeLabel);
        shell.ToggleThemeCommand.Execute(null);
        Assert.True(theme.IsDark);
        Assert.Equal("Mode Terang", shell.ThemeLabel);
    }
}
