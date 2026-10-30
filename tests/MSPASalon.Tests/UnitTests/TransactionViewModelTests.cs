using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Masters;
using MSPASalon.Presentation.ViewModels.Transactions;
using MSPASalon.Tests.Framework;
using MSPASalon.Tests.IntegrationTests;

namespace MSPASalon.Tests.UnitTests;

public sealed class TransactionViewModelTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakePrint _print = new();
    private Infrastructure.AppServices A => _f.Db.App;
    public void Dispose() => _f.Dispose();

    private ShellViewModel Shell(string user = "admin", string pwd = "admin") =>
        new(ViewModelTests.Services(A, _print), _dialogs, A.Auth.Login(user, pwd));

    [Test("VM-08", "Pembayaran: kembalian/kurang dihitung langsung; non-tunai dicatat sebesar total")]
    public void PaymentViewModel()
    {
        var vm = new PaymentViewModel(A.PaymentMethods.GetAll()) { AmountDue = 105000 };
        Assert.True(vm.IsCash, "default = Tunai");
        vm.ReceivedText = "150.000";
        Assert.Equal("Kembali 45.000", vm.ChangeText);
        vm.ReceivedText = "100.000";
        Assert.Equal("Kurang 5.000", vm.ChangeText);
        Assert.True(vm.IsShort);
        vm.ExactCommand.Execute(null);
        Assert.Equal("105.000", vm.ReceivedText);
        vm.MethodId = _f.Qris;
        Assert.False(vm.IsCash);
        Assert.Contains("tanpa kembalian", vm.ChangeText);
        var e = new InputErrors();
        Assert.Equal(0m, vm.Build(e).AmountReceived, "non-cash ignores received");
    }

    [Test("VM-09", "Layar registrasi → 'Proses sekarang' → layar treatment: mulai, tambah jasa, diskon, bayar, cetak nota")]
    public void RegistrationToPaymentFlow()
    {
        var shell = Shell();
        var regPage = (ListPageViewModel<Registration>)shell.CreatePage(shell.NavItems.Single(n => n.Key == "trans:registration"));
        _dialogs.OnEditor = ed =>
        {
            var reg = (RegistrationEditorViewModel)ed;
            reg.Lines[0].ServiceId = _f.Wash;
            reg.Lines[0].EmployeeId = _f.Capster1;
            Assert.Equal("40.000", reg.TotalText);
            return reg.TrySave();
        };
        _dialogs.ConfirmAnswer = true;                       // "Proses treatment sekarang?" → yes
        regPage.AddCommand.Execute(null);
        Assert.Equal(1, regPage.Items.Count);

        var page = (TreatmentPageViewModel)shell.SelectedPage!;
        Assert.Equal("trans:treatment", page.Key);
        Assert.True(page.IsWaiting, "registration selected in queue");
        Assert.Equal(1, page.Backflush.Count);               // preview: shampoo for "Cuci Rambut"

        page.StartCommand.Execute(null);
        Assert.True(page.IsInProcess, "started");
        page.AddLineCommand.Execute(null);
        page.Lines[1].ServiceId = _f.Creambath;
        page.Lines[1].EmployeeId = _f.Capster2;
        Assert.True(page.LinesDirty);
        Assert.Equal("105.000", page.SubtotalText);
        Assert.Equal(0.2m, page.Backflush.Single(b => b.ProductId == _f.Shampoo).Qty);
        page.DiscountText = "5.000";
        Assert.Equal("100.000", page.TotalText);
        page.Payment.ReceivedText = "100.000";
        page.CompleteCommand.Execute(null);                  // confirm + "cetak nota?" answered yes

        Assert.Equal(0, page.Queue.Count, "queue empty after payment");
        var t = A.Treatments.GetList(_f.Db.Clock.Now, _f.Db.Clock.Now).Single();
        Assert.Equal(TreatmentStatus.Paid, t.Status);
        Assert.Equal(100000m, t.GrandTotal);
        Assert.Equal(1, _print.Jobs.Count, "receipt printed");
        Assert.Equal(58, _print.Jobs[0].Paper);
        Assert.True(_print.Jobs[0].Lines.All(l => l.Length <= 32));
    }

    [Test("VM-10", "Layar treatment: kurang bayar menampilkan error dan input kasir tidak hilang")]
    public void TreatmentUnderpaymentKeepsInput()
    {
        var reg = A.Registrations.Create(_f.Customer, [new(_f.Haircut, _f.Capster1)], null, "admin");
        A.Treatments.Start(reg.Id, "admin");
        var page = new TreatmentPageViewModel(ViewModelTests.Services(A, _print), _dialogs, A.Auth.Login("admin", "admin"), _ => { });
        page.Select(reg.Id);
        page.DiscountText = "10.000";
        page.Payment.ReceivedText = "50.000";
        page.CompleteCommand.Execute(null);
        Assert.Contains("ERROR: Uang diterima", _dialogs.Messages.Last());
        Assert.Equal("10.000", page.DiscountText, "discount kept");
        Assert.Equal("50.000", page.Payment.ReceivedText, "received kept");
        Assert.True(page.IsInProcess);
    }

    [Test("VM-11", "Editor penjualan: produk digabung, total & kembalian, posting, tanya cetak")]
    public void SaleEditorFlow()
    {
        var shell = Shell();
        var salePage = (ListPageViewModel<Sale>)shell.CreatePage(shell.NavItems.Single(n => n.Key == "trans:sale"));
        _dialogs.OnEditor = ed =>
        {
            var vm = (SaleEditorViewModel)ed;
            vm.Picker.Product = vm.Picker.Products.Single(p => p.Id == _f.Drink);
            vm.Picker.QtyText = "2";
            vm.AddLineCommand.Execute(null);
            vm.Picker.Product = vm.Picker.Products.Single(p => p.Id == _f.Drink);
            vm.Picker.QtyText = "1";
            vm.AddLineCommand.Execute(null);
            Assert.Equal(1, vm.Lines.Count, "same product merged");
            Assert.Equal("3", vm.Lines[0].QtyText);
            vm.Lines[0].DiscountText = "1.000";
            Assert.Equal("14.000", vm.TotalText);
            Assert.Equal(14000m, vm.Payment.AmountDue);
            vm.Payment.ReceivedText = "20.000";
            Assert.Equal("Kembali 6.000", vm.Payment.ChangeText);
            Assert.False(vm.Picker.Products.Any(p => p.Id == _f.Mask), "non-sellable product not offered");
            return vm.TrySave();
        };
        salePage.AddCommand.Execute(null);
        Assert.Equal(1, salePage.Items.Count);
        Assert.Equal(7m, _f.Stock(_f.Drink));
        Assert.Equal(1, _print.Jobs.Count, "print confirmed");
        Assert.Contains("Kembalian: Rp 6.000", _dialogs.Messages.Last(m => m.StartsWith("CONFIRM")));
    }

    [Test("VM-12", "Hak akses: KASIR tidak bisa membatalkan penjualan yang sudah diposting")]
    public void CashierCannotCancelPosted()
    {
        A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
        var cashierRole = A.Roles.GetAll().Single(r => r.Code == RoleCodes.Cashier).Id;
        A.Users.Create(new AppUser { Username = "kasir9", DisplayName = "Kasir", RoleId = cashierRole }, "kasir999");
        var shell = Shell("kasir9", "kasir999");
        var page = (ListPageViewModel<Sale>)shell.CreatePage(shell.NavItems.Single(n => n.Key == "trans:sale"));
        page.SelectedItem = page.Items.Single();
        Assert.True(page.EditCommand.CanExecute(null), "can view/print");
        Assert.False(page.DeleteCommand.CanExecute(null), "cannot cancel posted");

        var admin = Shell();
        var adminPage = (ListPageViewModel<Sale>)admin.CreatePage(admin.NavItems.Single(n => n.Key == "trans:sale"));
        adminPage.SelectedItem = adminPage.Items.Single();
        Assert.True(adminPage.DeleteCommand.CanExecute(null), "admin can cancel");
        _dialogs.OnEditor = ed => { ((CancelReasonEditorViewModel)ed).Reason = "salah input"; return ed.TrySave(); };
        adminPage.DeleteCommand.Execute(null);
        Assert.Equal(DocStatus.Cancelled, adminPage.Items.Single().Status);
        Assert.Equal(10m, _f.Stock(_f.Drink));
    }

    [Test("VM-13", "Kartu stok: pilih produk & periode, saldo awal/akhir tampil")]
    public void StockCardPage()
    {
        var vm = new StockCardPageViewModel(ViewModelTests.Services(A, _print));
        vm.ProductId = _f.Shampoo;
        Assert.Equal(1, vm.Rows.Count, "fixture purchase");
        Assert.Equal("0 BTL", vm.OpeningText);
        Assert.Equal("2 BTL", vm.ClosingText);
    }
}
