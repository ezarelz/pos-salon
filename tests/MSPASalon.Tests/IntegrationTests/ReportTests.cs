using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Reports;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Reports;
using MSPASalon.Tests.Framework;
using MSPASalon.Tests.UnitTests;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class ReportTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private readonly string _out = Path.Combine(Path.GetTempPath(), "mspasalon-report-" + Guid.NewGuid().ToString("N"));
    private Infrastructure.AppServices A => _f.Db.App;

    public void Dispose()
    {
        _f.Dispose();
        try { Directory.Delete(_out, true); } catch (IOException) { }
    }

    private static readonly DateTime Day1 = new(2026, 10, 30), Day2 = new(2026, 10, 31);

    /// <summary>
    /// 30-10: treatment Cuci (Capster1) 40.000 cash.
    /// 31-10: treatment Creambath (C1) + Potong (C2) = 135.000 − 5.000 QRIS ref QR-9; sale Drink x2 10.000 − 1.000 cash;
    ///        sale cancelled; treatment cancelled after LUNAS (Capster2); registration waiting (no effect).
    /// </summary>
    private void Seed()
    {
        var clock = _f.Db.Clock;
        clock.Now = Day1.AddHours(10);
        Complete([new(_f.Wash, _f.Capster1)], 0, new PaymentInput(_f.Cash, 50000, null));

        clock.Now = Day2.AddHours(9).AddMinutes(45);
        Complete([new(_f.Creambath, _f.Capster1), new(_f.Haircut, _f.Capster2)], 5000, new PaymentInput(_f.Qris, 0, "QR-9"));
        clock.Now = Day2.AddHours(11);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 2, 5000, 1000)], null, new PaymentInput(_f.Cash, 10000, null), "admin");
        clock.Now = Day2.AddHours(12);
        var bad = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
        A.Sales.Cancel(bad.Id, "dobel", "admin");
        clock.Now = Day2.AddHours(13);
        var cancelled = Complete([new(_f.Haircut, _f.Capster2)], 0, new PaymentInput(_f.Cash, 70000, null));
        A.Treatments.Cancel(cancelled, "salah input", allowPosted: true, "admin");
        A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, "admin");
        clock.Now = Day2.AddHours(20);
    }

    private long Complete(IReadOnlyList<ServiceLineInput> lines, decimal discount, PaymentInput pay)
    {
        var reg = A.Registrations.Create(_f.Customer, lines, null, "admin");
        var t = A.Treatments.Start(reg.Id, "admin");
        A.Treatments.Complete(t.Id, discount, pay, "admin");
        return t.Id;
    }

    [Test("M4-03", "Rekap Kas Harian: per metode (tagihan, bukan uang diterima), tunai di laci, daftar nota + referensi, batal tidak dihitung")]
    public void CashDaily()
    {
        Seed();
        var r = A.Reports.GetCashDaily(Day2);
        Assert.Equal(139000m, r.Total, "130.000 QRIS + 9.000 tunai");
        Assert.Equal(9000m, r.CashTotal, "cash drawer = amount due, not 10.000 received");
        Assert.Equal(130000m, r.NonCashTotal);
        Assert.Equal(2, r.Documents.Count, "cancelled sale & treatment excluded");
        Assert.Equal("QR-9", r.Documents.Single(d => d.Kind == "Treatment").Reference);
        Assert.Equal(2, r.CancelledCount, "sale + treatment cancelled today");
        Assert.Equal("Tunai", r.Methods[0].Method, "cash first");

        var doc = ReportBuilders.CashDaily(r, "Salon", "Admin", Day2.AddHours(20));
        Assert.Equal("Rp 9.000", doc.Summary.Single(s => s.Label.StartsWith("Tunai")).Value);
        Assert.Equal(139000m, (decimal)doc.Sections[1].Totals![6]!);
        Assert.Contains("2 dokumen dibatalkan", doc.Notes.Single());
        Assert.Equal(0, A.Reports.GetCashDaily(new DateTime(2026, 10, 29)).Documents.Count, "empty day");
    }

    [Test("M4-04", "Laporan Pendapatan: semua hari dalam periode (termasuk 0), jasa bruto/diskon/neto, produk, total = rekap metode = dashboard")]
    public void Revenue()
    {
        Seed();
        var r = A.Reports.GetRevenue(new DateTime(2026, 10, 29), Day2);
        Assert.Equal(3, r.Days.Count, "29, 30, 31 — including a day without sales");
        Assert.Equal(0m, r.Days[0].Total);
        var d2 = r.Days[2];
        Assert.Equal(1, d2.Clients, "cancelled treatment excluded");
        Assert.Equal(135000m, d2.ServiceGross);
        Assert.Equal(5000m, d2.ServiceDiscount);
        Assert.Equal(130000m, d2.ServiceNet);
        Assert.Equal(9000m, d2.ProductNet, "10.000 − 1.000 line discount");
        Assert.Equal(179000m, r.Total, "40.000 + 130.000 + 9.000");
        Assert.Equal(r.Total, r.Methods.Sum(m => m.Amount), "payment recap = revenue");
        Assert.Equal(6000m, r.Discount);
        Assert.Equal(3, r.Documents.Count);
        Assert.Equal(A.Dashboard.GetSummary(Day2).TotalRevenue, d2.Total, "same figure as the dashboard");

        var doc = ReportBuilders.Revenue(r, "Salon", "Admin", Day2);
        Assert.Equal(3, doc.Sections.Count);
        Assert.Equal(179000m, (decimal)doc.Sections[0].Totals![7]!);
        Assert.Equal("Rp 85.000", doc.Summary.Single(s => s.Label.StartsWith("Rata-rata")).Value, "170.000 / 2 clients");
    }

    [Test("M4-05", "Laporan Komisi: ringkasan & rincian per capster, filter capster = slip bertanda tangan, diskon tidak memotong komisi, batal = 0")]
    public void Commission()
    {
        Seed();
        var r = A.Reports.GetCommission(Day1, Day2, null);
        var c1 = r.Capsters.Single(c => c.EmployeeName == "Capster Satu");
        Assert.Equal(2, c1.Clients);
        Assert.Equal(32000m, c1.Commission, "Cuci 12.000 + Creambath 20.000 (discount ignored, K20)");
        Assert.Equal(105000m, c1.Value);
        var c2 = r.Capsters.Single(c => c.EmployeeName == "Capster Dua");
        Assert.Equal(1, c2.Services, "cancelled haircut excluded");
        Assert.Equal(30000m, c2.Commission);
        Assert.Equal(62000m, r.TotalCommission);
        Assert.Equal(3, r.Lines.Count);

        var only = A.Reports.GetCommission(Day1, Day2, _f.Capster2);
        Assert.Equal("Capster Dua", only.EmployeeFilterName);
        Assert.Equal(1, only.Lines.Count);
        var slip = ReportBuilders.Commission(only, "Salon", "Admin", Day2);
        Assert.Contains("Slip Komisi", slip.Title);
        Assert.Equal(2, slip.Signatures.Count);
        Assert.Equal(0, ReportBuilders.Commission(r, "Salon", "Admin", Day2).Signatures.Count, "no signatures for the overview");
    }

    [Test("VM-15", "Halaman laporan: hak akses menu, periode cepat/manual, validasi tanggal, export Excel, PDF, struk rekap 58 mm")]
    public void ReportPages()
    {
        Seed();
        var print = new FakePrint();
        var dialogs = new FakeDialogs();
        var cashierRole = A.Roles.GetAll().Single(r => r.Code == RoleCodes.Cashier).Id;
        A.Users.Create(new AppUser { Username = "kasirr", DisplayName = "Kasir", RoleId = cashierRole }, "kasir-uji-99");
        var cashierShell = new ShellViewModel(ViewModelTests.Services(A, print), dialogs, A.Auth.Login("kasirr", "kasir-uji-99"));
        var keys = cashierShell.NavItems.Select(n => n.Key).ToHashSet();
        Assert.True(keys.Contains("report:cash"), "kasir: rekap kas");
        Assert.False(keys.Contains("report:revenue") || keys.Contains("report:commission"), "kasir: no revenue / commission");

        var shell = new ShellViewModel(ViewModelTests.Services(A, print), dialogs, A.Auth.Login("admin", "admin"));
        shell.Navigate(shell.NavItems.Single(n => n.Key == "report:cash"));
        var cash = (CashDailyReportViewModel)shell.SelectedPage!;
        Assert.True(cash.IsSingleDay);
        Assert.Equal(Day2, cash.DateFrom!.Value, "default today");
        Assert.Equal("Rp 139.000", cash.Summary[0].Value);
        Assert.Equal(2, cash.Sections.Count);
        Assert.True(cash.Sections[1].Rows.Last().IsTotal, "bold total row last");

        cash.SelectedPreset = PeriodPreset.Yesterday;
        Assert.Equal(Day1, cash.DateFrom!.Value);
        Assert.Equal("Rp 40.000", cash.Summary[0].Value);
        cash.DateFrom = new DateTime(2026, 10, 29);
        Assert.Equal(PeriodPreset.Custom, cash.SelectedPreset, "manual date → 'Pilih tanggal'");
        Assert.True(cash.Sections[1].IsEmpty);

        cash.ExtraPrintCommand!.Execute(null);
        Assert.Equal(1, print.Jobs.Count, "thermal recap");
        Assert.True(print.Jobs[0].Lines.All(l => l.Length <= 32));

        shell.Navigate(shell.NavItems.Single(n => n.Key == "report:revenue"));
        var revenue = (RevenueReportViewModel)shell.SelectedPage!;
        Assert.Equal(new DateTime(2026, 10, 1), revenue.DateFrom!.Value, "default this month");
        revenue.DateTo = new DateTime(2026, 9, 1);
        Assert.True(revenue.HasError, "to < from");
        revenue.SelectedPreset = PeriodPreset.ThisMonth;
        Assert.False(revenue.HasError);
        Assert.Equal(31, revenue.Sections[0].Rows.Count - 1, "31 days + total row");

        dialogs.SaveFolder = null;
        revenue.ExcelCommand.Execute(null);
        Assert.True(revenue.StatusText is null, "save cancelled: nothing written");
        Directory.CreateDirectory(_out);
        dialogs.SaveFolder = _out;
        revenue.ExcelCommand.Execute(null);
        var file = Path.Combine(_out, "Pendapatan-20261001-20261031.xlsx");
        Assert.True(File.Exists(file), "xlsx written");
        Assert.Contains("Excel tersimpan", revenue.StatusText);
        revenue.PdfCommand.Execute(null);
        Assert.True(print.Reports.Single().ToPdf, "PDF via print dialog");

        shell.Navigate(shell.NavItems.Single(n => n.Key == "report:commission"));
        var commission = (CommissionReportViewModel)shell.SelectedPage!;
        Assert.Equal(PeriodPreset.SecondHalfThisMonth, commission.SelectedPreset, "31st → 16–akhir");
        Assert.True(commission.HasFilter);
        Assert.Equal("Semua capster", commission.FilterOptions[0].Label);
        commission.FilterValue = _f.Capster1;
        Assert.Contains("Capster Satu", commission.Document!.Title);
        Assert.Equal("Rp 32.000", commission.Summary[0].Value);
    }

    [Test("DASH-03", "Dashboard: komisi capster hanya tampil bagi user dengan izin laporan komisi")]
    public void DashboardCommissionVisibility()
    {
        Seed();
        var admin = new ShellViewModel(ViewModelTests.Services(A), new FakeDialogs(), A.Auth.Login("admin", "admin"));
        var adminDash = (DashboardViewModel)admin.SelectedPage!;
        Assert.Contains("komisi Rp 20.000", adminDash.Capsters.Single(c => c.Name == "Capster Satu").WorkText);

        var cashierRole = A.Roles.GetAll().Single(r => r.Code == RoleCodes.Cashier).Id;
        A.Users.Create(new AppUser { Username = "kasird", DisplayName = "Kasir", RoleId = cashierRole }, "kasir-uji-99");
        var cashier = new ShellViewModel(ViewModelTests.Services(A), new FakeDialogs(), A.Auth.Login("kasird", "kasir-uji-99"));
        var dash = (DashboardViewModel)cashier.SelectedPage!;
        Assert.False(dash.Capsters.Any(c => c.WorkText.Contains("komisi")), "kasir: no commission");
    }
}
