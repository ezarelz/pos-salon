using Dapper;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Reports;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Masters;
using MSPASalon.Presentation.ViewModels.Reports;
using MSPASalon.Presentation.ViewModels.Transactions;
using MSPASalon.Tests.Framework;
using MSPASalon.Tests.UnitTests;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class ReportM4bcTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private Infrastructure.AppServices A => _f.Db.App;
    public void Dispose() => _f.Dispose();

    private static readonly DateTime Oct1 = new(2026, 10, 1), Oct31 = new(2026, 10, 31);

    private long Complete(DateTime at, IReadOnlyList<ServiceLineInput> lines, decimal discount, long? customer = null)
    {
        _f.Db.Clock.Now = at;
        var reg = A.Registrations.Create(customer ?? _f.Customer, lines, null, "admin");
        var t = A.Treatments.Start(reg.Id, "admin");
        A.Treatments.Complete(t.Id, discount, new PaymentInput(_f.Qris, 0, null), "admin");
        return t.Id;
    }

    [Test("M4-06", "Jasa & Margin: per jasa bruto − komisi − bahan (snapshot); baris lama tanpa snapshot diestimasi & ditandai; diskon hanya di total")]
    public void ServiceMargin()
    {
        Complete(new DateTime(2026, 10, 10, 10, 0, 0), [new(_f.Creambath, _f.Capster1), new(_f.Wash, _f.Capster2)], 5000);
        var old = Complete(new DateTime(2026, 10, 12, 10, 0, 0), [new(_f.Creambath, _f.Capster1)], 0);
        using (var c = A.Db.Open())   // simulate a line posted before migration 003
            c.Execute("UPDATE treatment_line SET material_cost = NULL WHERE treatment_id = @old", new { old });
        var shampoo = A.Products.Get(_f.Shampoo)!;   // current cost changes → only the estimate follows it
        shampoo.CostPrice = 30000;
        A.Products.Save(shampoo);

        var r = A.Reports.GetServiceMargin(Oct1, Oct31);
        var cb = r.Rows.Single(x => x.ServiceName == "Creambath");
        Assert.Equal(2, cb.Count);
        Assert.Equal(130000m, cb.Gross);
        Assert.Equal(40000m, cb.Commission);
        Assert.Equal(6500m + 7500m, cb.MaterialCost, "snapshot 6.500 + estimate 0,1×30.000 + 0,15×30.000 = 7.500");
        Assert.Equal(1, cb.EstimatedLines);
        Assert.Equal(76000m, cb.Margin);
        var wash = r.Rows.Single(x => x.ServiceName == "Cuci Rambut");
        Assert.Equal(2000m, wash.MaterialCost, "snapshot at old shampoo cost 20.000 × 0,1");
        Assert.Equal(5000m, r.InvoiceDiscount);
        Assert.Equal(r.Net - r.Commission - r.MaterialCost, r.MarginAfterDiscount);

        var doc = ReportBuilders.ServiceMargin(r, "Salon", "Admin", Oct31);
        Assert.True(doc.Notes.Any(n => n.Contains("diestimasi")), "estimate disclosed");
        Assert.Equal(r.MarginAfterDiscount, (decimal)doc.Sections[1].Rows[^1][1]!, "reconciliation ends at margin after discount");
    }

    [Test("M4-07", "Produk & Laba Kotor: neto = bruto − diskon, HPP dari snapshot harga pokok (tidak berubah bila master diubah)")]
    public void ProductProfit()
    {
        _f.Db.Clock.Now = new DateTime(2026, 10, 15, 11, 0, 0);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 3, 5000, 1000), new(_f.Shampoo, 1, 32000, 0)], null, new PaymentInput(_f.Cash, 50000, null), "admin");
        var cancelled = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
        A.Sales.Cancel(cancelled.Id, "batal", "admin");
        var drink = A.Products.Get(_f.Drink)!;
        drink.CostPrice = 4000;
        A.Products.Save(drink);

        var r = A.Reports.GetProductProfit(Oct1, Oct31);
        var d = r.Rows.Single(x => x.Name == "Air Mineral");
        Assert.Equal(3m, d.Qty, "cancelled sale excluded");
        Assert.Equal(14000m, d.Net);
        Assert.Equal(7500m, d.Cogs, "3 × 2.500 snapshot, not 4.000");
        Assert.Equal(6500m, d.Profit);
        Assert.Equal(32000m - 20000m, r.Rows.Single(x => x.Name == "Shampo 200ml").Profit);
        Assert.Equal(18500m, r.Profit);
    }

    [Test("M4-08", "Laporan Stok: saldo awal/mutasi/akhir per produk = ledger, nilai stok, pemakaian bahan treatment, filter di bawah minimum")]
    public void Stock()
    {
        // fixture (31-10 09:30): purchase shampoo 2, mask 1, drink 10 → move it before the period to test 'Awal'
        using (var c = A.Db.Open()) c.Execute("UPDATE stock_ledger SET moved_at = '2026-09-30 09:30:00'");
        Complete(new DateTime(2026, 10, 10, 10, 0, 0), [new(_f.Creambath, _f.Capster1)], 0);
        _f.Db.Clock.Now = new DateTime(2026, 10, 11, 10, 0, 0);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 4, 5000, 0)], null, new PaymentInput(_f.Cash, 20000, null), "admin");
        A.Adjustments.Post("opname", [new(_f.Drink, -1)], "admin");
        _f.Db.Clock.Now = new DateTime(2026, 11, 2, 10, 0, 0);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");   // after period
        var drinkMaster = A.Products.Get(_f.Drink)!;
        drinkMaster.StockMin = 6;
        A.Products.Save(drinkMaster);

        var r = A.Reports.GetStock(Oct1, Oct31);
        var drink = r.Rows.Single(x => x.Name == "Air Mineral");
        Assert.Equal(10m, drink.Opening);
        Assert.Equal(4m, drink.Sale);
        Assert.Equal(-1m, drink.Adjustment);
        Assert.Equal(5m, drink.Closing, "end of period, not today's 4");
        Assert.True(drink.BelowMin);
        Assert.Equal(12500m, drink.Value, "5 × 2.500");
        var shampoo = r.Rows.Single(x => x.Name == "Shampo 200ml");
        Assert.Equal(0.1m, shampoo.Treatment);
        Assert.Equal(1.9m, shampoo.Closing);
        var usage = r.Usage.Single(u => u.Name == "Masker Rambut");
        Assert.Equal(0.15m, usage.Qty);
        Assert.Equal(4500m, usage.Value);
        Assert.Equal(1, usage.Treatments);
        Assert.Equal(_f.Stock(_f.Drink) + 1, drink.Closing, "closing + later sale = current stock");

        var below = ReportBuilders.Stock(r, ReportBuilders.StockBelowMin, "Salon", "Admin", Oct31);
        Assert.Equal(1, below.Sections[0].Rows.Count);
    }

    [Test("M4-09", "Laporan Pembatalan: semua jenis dokumen, waktu & user pembatal, alasan; registrasi dari treatment batal tidak dobel")]
    public void Cancellations()
    {
        _f.Db.Clock.Now = new DateTime(2026, 10, 20, 9, 0, 0);
        var t = Complete(_f.Db.Clock.Now, [new(_f.Haircut, _f.Capster1)], 0);
        A.Treatments.Cancel(t, "salah capster", allowPosted: true, "admin");
        var waiting = A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, "kasir1");
        A.Registrations.Cancel(waiting.Id, "pulang", "kasir1");
        var p = A.Purchases.Post(_f.Supplier, "INV-9", [new(_f.Drink, 1, 2500)], null, "admin");
        A.Purchases.Cancel(p.Id, "salah supplier", "admin");
        var adj = A.Adjustments.Post("opname", [new(_f.Drink, 1)], "admin");
        A.Adjustments.Cancel(adj.Id, "dobel", "admin");
        using (var c = A.Db.Open())   // a sale cancelled by an older version: no cancelled_by, user only in the reason
        {
            var s = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
            A.Sales.Cancel(s.Id, "lama", "kasirlama");
            c.Execute("UPDATE sale SET cancelled_by = NULL, cancelled_at = NULL, cancel_reason = 'lama (oleh kasirlama)' WHERE id = @id", new { id = s.Id });
        }

        var r = A.Reports.GetCancellations(Oct1, Oct31);
        Assert.Equal(5, r.Rows.Count, "treatment, registration, purchase, adjustment, sale");
        Assert.Equal(1, r.Rows.Count(x => x.Kind == "Registrasi"), "treatment's own registration not repeated");
        var tr = r.Rows.Single(x => x.Kind == "Treatment");
        Assert.Equal(70000m, tr.Amount!.Value);
        Assert.Equal("salah capster", tr.Reason, "'(oleh …)' suffix removed");
        Assert.Equal("kasir1", r.Rows.Single(x => x.Kind == "Registrasi").CancelledBy);
        var legacy = r.Rows.Single(x => x.Kind == "Penjualan");
        Assert.Equal("kasirlama", legacy.CancelledBy, "user parsed from old reason text");
        Assert.Equal("lama", legacy.Reason);
        Assert.Equal(0, A.Reports.GetCancellations(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)).Rows.Count);
    }

    [Test("M4-10", "Kunjungan Customer: status 30/60 hari, rata-rata jarak, total belanja (jasa+produk), jasa & capster terakhir, walk-in tidak ikut")]
    public void CustomerVisits()
    {
        var ani = A.Customers.Save(new Customer { Code = "CST002", Name = "Ani", Phone = "0812" });
        var dodi = A.Customers.Save(new Customer { Code = "CST003", Name = "Dodi" });
        A.Customers.Save(new Customer { Code = "CST004", Name = "Eka" });                                   // never visited
        var walkIn = A.Customers.GetAll().Single(c => c.IsWalkIn).Id;
        Complete(new DateTime(2026, 8, 1, 10, 0, 0), [new(_f.Haircut, _f.Capster1)], 0);                     // Budi: 2 visits
        Complete(new DateTime(2026, 9, 10, 10, 0, 0), [new(_f.Haircut, _f.Capster2), new(_f.Wash, _f.Capster2)], 0);   // 51 days before 31-10
        Complete(new DateTime(2026, 10, 25, 10, 0, 0), [new(_f.Wash, _f.Capster1)], 0, ani);                   // 6 days
        Complete(new DateTime(2026, 8, 20, 10, 0, 0), [new(_f.Wash, _f.Capster1)], 0, dodi);                   // 72 days
        Complete(new DateTime(2026, 10, 30, 10, 0, 0), [new(_f.Wash, _f.Capster1)], 0, walkIn);
        var cancelled = Complete(new DateTime(2026, 10, 29, 10, 0, 0), [new(_f.Wash, _f.Capster1)], 0, dodi);
        A.Treatments.Cancel(cancelled, "batal", allowPosted: true, "admin");                                    // not a visit (K22)
        _f.Db.Clock.Now = new DateTime(2026, 10, 30, 12, 0, 0);
        A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");

        var r = A.Reports.GetCustomerVisits(Oct31, 30, 60);
        Assert.False(r.Rows.Any(x => x.Name.Contains("Walk-in")), "walk-in excluded");
        var budi = r.Rows.Single(x => x.Name == "Budi");
        Assert.Equal(VisitStatus.Remind, budi.Status, "51 days");
        Assert.Equal(2, budi.Visits);
        Assert.Equal(40m, budi.AverageIntervalDays!.Value, "1 Aug → 10 Sep");
        Assert.Equal(70000m + 110000m + 5000m, budi.TotalSpend, "both treatments + product sale");
        Assert.Equal("Potong Rambut, Cuci Rambut", budi.LastServices);
        Assert.Equal("Capster Dua", budi.LastCapsters);
        Assert.Equal(VisitStatus.Active, r.Rows.Single(x => x.Name == "Ani").Status);
        var d = r.Rows.Single(x => x.Name == "Dodi");
        Assert.Equal(VisitStatus.Inactive, d.Status, "72 days; cancelled visit ignored");
        Assert.Equal(1, d.Visits);
        Assert.Equal(VisitStatus.Never, r.Rows.Single(x => x.Name == "Eka").Status);
        Assert.Equal("Budi", r.Rows[0].Name, "'Perlu diingatkan' listed first");

        Assert.Equal(VisitStatus.Inactive, A.Reports.GetCustomerVisits(Oct31, 30, 45).Rows.Single(x => x.Name == "Budi").Status, "thresholds configurable");
        Assert.Equal(VisitStatus.Never, A.Reports.GetCustomerVisits(new DateTime(2026, 7, 31), 30, 60).Rows.Single(x => x.Name == "Budi").Status, "as-of date");

        var history = A.Reports.GetVisitHistory(_f.Customer);
        Assert.Equal(2, history.Count);
        Assert.Equal(new DateTime(2026, 9, 10, 10, 0, 0), history[0].At, "newest first");
        var last = A.Reports.GetLastVisit(ani, Oct31)!;
        Assert.Equal(6, last.DaysAgo);
        Assert.Contains("6 hari lalu", last.Text);
        Assert.True(A.Reports.GetLastVisit(walkIn, Oct31) is null, "no marker for walk-in");
    }

    [Test("VM-16", "M4b/M4c di aplikasi: menu per izin, pengaturan batas hari (validasi), penanda di Registrasi, riwayat di Master Customer")]
    public void M4bcScreens()
    {
        Complete(new DateTime(2026, 10, 1, 10, 0, 0), [new(_f.Haircut, _f.Capster1)], 0);
        _f.Db.Clock.Now = new DateTime(2026, 10, 31, 9, 30, 0);
        var dialogs = new FakeDialogs();
        var cashierRole = A.Roles.GetAll().Single(r => r.Code == RoleCodes.Cashier).Id;
        A.Users.Create(new AppUser { Username = "kasirm", DisplayName = "Kasir", RoleId = cashierRole }, "kasir-uji-99");
        var cashier = new ShellViewModel(ViewModelTests.Services(A), dialogs, A.Auth.Login("kasirm", "kasir-uji-99"));
        var ck = cashier.NavItems.Select(n => n.Key).ToHashSet();
        Assert.True(ck.Contains("report:stock") && ck.Contains("report:customervisit"), "kasir: stok & kunjungan");
        Assert.False(ck.Contains("report:servicemargin") || ck.Contains("report:productprofit") || ck.Contains("report:cancellation"),
            "kasir: no margin / profit / audit");
        Assert.False(cashier.NavItems.Any(n => n.IsComingSoon && n.Section == "Laporan"), "no placeholder left");

        var admin = new ShellViewModel(ViewModelTests.Services(A), dialogs, A.Auth.Login("admin", "admin"));
        foreach (var key in new[] { "report:servicemargin", "report:productprofit", "report:stock", "report:cancellation", "report:customervisit" })
        {
            admin.Navigate(admin.NavItems.Single(n => n.Key == key));
            var page = (ReportPageViewModel)admin.SelectedPage!;
            Assert.False(page.HasError, $"{key}: {page.ErrorText}");
            Assert.True(page.Document is not null && page.Sections.Count > 0, key);
        }
        var visits = (CustomerVisitReportViewModel)admin.SelectedPage!;
        Assert.Equal(VisitStatus.Remind, visits.FilterOptions[0].Label, "default filter = who to call");
        Assert.Equal(1, visits.Sections[0].Rows.Count, "Budi, 30 days");

        // Settings: thresholds validated, marker can be switched off.
        var settings = new BranchSettingsViewModel(A.Branch, dialogs) { VisitActiveText = "60", VisitInactiveText = "30" };
        settings.SaveCommand.Execute(null);
        Assert.Contains("Batas hari", dialogs.Messages.Last());
        settings.VisitActiveText = "20"; settings.VisitInactiveText = "45"; settings.ShowLastVisit = true;
        settings.SaveCommand.Execute(null);
        Assert.Equal(new VisitSettings(20, 45, true), A.Branch.GetVisitSettings());

        var reg = new RegistrationEditorViewModel(ViewModelTests.Services(A), "admin", null);
        Assert.True(reg.LastVisitText is null, "default customer = walk-in: no marker");
        reg.CustomerId = _f.Customer;
        Assert.Contains("30 hari lalu", reg.LastVisitText);
        Assert.Contains("Potong Rambut dengan Capster Satu", reg.LastVisitText);
        settings.ShowLastVisit = false;
        settings.SaveCommand.Execute(null);
        var off = new RegistrationEditorViewModel(ViewModelTests.Services(A), "admin", null) { CustomerId = _f.Customer };
        Assert.True(off.LastVisitText is null, "K24 optional");

        var customers = (ListPageViewModel<Customer>)admin.CreatePage(admin.NavItems.Single(n => n.Key == "master:customer"));
        var action = customers.ExtraActions.Single(a => a.Label == "Riwayat Kunjungan");
        customers.SelectedItem = customers.Items.Single(c => c.Id == _f.Customer);
        string? shown = null;
        dialogs.OnEditor = ed => { shown = ((DocumentDetailViewModel)ed).Text; return false; };
        action.Command.Execute(null);
        Assert.Contains("Kunjungan: 1 kali", shown);
        Assert.Contains("Potong Rambut — Capster Satu", shown);
        customers.SelectedItem = customers.Items.Single(c => c.IsWalkIn);
        Assert.False(action.Command.CanExecute(null), "no history for walk-in");
        Assert.False(((ListPageViewModel<Customer>)cashier.CreatePage(cashier.NavItems.Single(n => n.Key == "master:customer")))
            .ExtraActions.Count == 0, "kasir has report.customer → history available");
    }
}
