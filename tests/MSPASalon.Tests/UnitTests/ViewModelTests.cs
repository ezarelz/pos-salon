using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Infrastructure;
using MSPASalon.Presentation.Services;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Editors;
using MSPASalon.Presentation.ViewModels.Masters;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.UnitTests;

/// <summary>Scripted dialog service: records messages and answers confirmations.</summary>
public sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;
    public List<string> Messages { get; } = [];
    public Func<EditorViewModel, bool>? OnEditor { get; set; }
    public void Info(string message, string title = "MSPA Salon") => Messages.Add("INFO: " + message);
    public void Error(string message, string title = "MSPA Salon") => Messages.Add("ERROR: " + message);
    public bool Confirm(string message, string title = "Konfirmasi") { Messages.Add("CONFIRM: " + message); return ConfirmAnswer; }
    public bool ShowEditor(EditorViewModel editor) => OnEditor?.Invoke(editor) ?? false;
    public string? OpenFile { get; set; }
    public string? PickOpenFile(string title, string filter) => OpenFile;
    public string? PickFolder(string title) => null;
}

public sealed class FakePrint : IPrintService
{
    public List<(IReadOnlyList<string> Lines, int Paper, string Job, byte[]? Logo)> Jobs { get; } = [];
    public bool PrintReceipt(IReadOnlyList<string> lines, int paperWidthMm, string jobName, byte[]? logo = null)
    { Jobs.Add((lines, paperWidthMm, jobName, logo)); return true; }
}

public sealed class FakeTheme : IThemeService
{
    public bool IsDark { get; private set; }
    public void SetDark(bool dark) => IsDark = dark;
}

public sealed class ViewModelTests : IDisposable
{
    private readonly TestDatabase _db = new();
    public void Dispose() => _db.Dispose();

    internal static PresentationServices Services(AppServices a, IPrintService? print = null, IThemeService? theme = null) => new(
        a.Customers, a.Suppliers, a.Categories, a.Employees, a.Products, a.Services, a.PaymentMethods, a.Roles,
        a.Users, a.Auth, a.Branch, a.Backup, a.Dashboard, a.LegacyImport, a.Clock,
        a.Registrations, a.Treatments, a.Sales, a.Purchases, a.Adjustments, a.StockCards, print ?? new FakePrint(), theme ?? new FakeTheme());

    [Test("VM-01", "Editor produk: input Indonesia ('28.000', '0,5') tersimpan; input salah tampil di ErrorText")]
    public void ProductEditor()
    {
        var vm = new ProductEditorViewModel(_db.App.Products, _db.App.Categories, null)
        {
            Name = "Shampo", Unit = "btl", CostPriceText = "18.000", SellPriceText = "Rp 28.000",
            StockMinText = "0,5", StockMaxText = "10", ContentQtyText = "200", ContentUnit = "ml", IsConsumable = true,
        };
        Assert.Equal("PRD001", vm.Code, "suggested code");
        Assert.True(vm.TrySave(), vm.ErrorText ?? "");
        var p = _db.App.Products.Get(vm.SavedId)!;
        Assert.Equal(18000m, p.CostPrice); Assert.Equal(0.5m, p.StockMin); Assert.Equal(200m, p.ContentQty);

        var bad = new ProductEditorViewModel(_db.App.Products, _db.App.Categories, null) { Name = "X", CostPriceText = "18.000,50", StockMinText = "abc" };
        Assert.False(bad.TrySave());
        Assert.Contains("Harga pokok tidak valid", bad.ErrorText);
        Assert.Contains("Stok minimum tidak valid", bad.ErrorText);
        Assert.Equal(1, _db.App.Products.GetAll().Count, "nothing saved");
    }

    [Test("VM-02", "Editor jasa: tambah bahan, ekuivalen ml, biaya bahan & margin, tolak duplikat, simpan resep")]
    public void ServiceEditorRecipe()
    {
        var shampoo = _db.App.Products.Save(new Product { Code = "PRD001", Name = "Shampo", Unit = "BTL", ContentQty = 200, ContentUnit = "ml", CostPrice = 20000, IsConsumable = true });
        var mask = _db.App.Products.Save(new Product { Code = "PRD002", Name = "Masker Rambut", Unit = "BTL", CostPrice = 30000, IsConsumable = true });
        _db.App.Products.Save(new Product { Code = "PRD003", Name = "Air Mineral", Unit = "BTL", IsSellable = true });

        var vm = new ServiceEditorViewModel(_db.App.Services, _db.App.Products, null)
        {
            Name = "Creambath", PriceText = "65.000", CommissionText = "20.000",
        };
        Assert.Equal(2, vm.ConsumableProducts.Count, "only consumables offered");

        vm.ProductToAdd = vm.ConsumableProducts.Single(p => p.Id == shampoo); vm.QtyToAdd = "0,1";
        vm.AddMaterialCommand.Execute(null);
        vm.ProductToAdd = vm.ConsumableProducts.Single(p => p.Id == mask); vm.QtyToAdd = "0.15";
        vm.AddMaterialCommand.Execute(null);
        Assert.Equal(2, vm.Materials.Count);
        Assert.Equal("≈ 20 ml", vm.Materials[0].EquivalentText);
        Assert.Equal("6.500", vm.MaterialCostText, "0,1x20.000 + 0,15x30.000");
        Assert.Equal("38.500", vm.MarginText, "65.000 - 20.000 - 6.500");

        vm.ProductToAdd = vm.ConsumableProducts.Single(p => p.Id == shampoo); vm.QtyToAdd = "0,2";
        vm.AddMaterialCommand.Execute(null);
        Assert.Contains("sudah ada di resep", vm.ErrorText);
        Assert.Equal(2, vm.Materials.Count);

        vm.Materials[1].QtyText = "0,2";
        Assert.Equal("8.000", vm.MaterialCostText);
        Assert.True(vm.TrySave(), vm.ErrorText ?? "");
        var saved = _db.App.Services.Get(vm.SavedId)!;
        Assert.Equal(2, saved.Materials.Count);
        Assert.Equal(0.2m, saved.Materials.Single(m => m.ProductId == mask).Qty);

        var edit = new ServiceEditorViewModel(_db.App.Services, _db.App.Products, saved);
        Assert.Equal(2, edit.Materials.Count, "recipe reloaded in editor");
        edit.RemoveMaterialCommand.Execute(edit.Materials[0]);
        Assert.True(edit.TrySave());
        Assert.Equal(1, _db.App.Services.Get(saved.Id)!.Materials.Count);
    }

    [Test("VM-03", "Daftar master: pencarian multi-kata, status, hapus dengan konfirmasi")]
    public void ListPageSearchAndDelete()
    {
        _db.App.Customers.Save(new Customer { Code = "CST001", Name = "Budi Santoso", Phone = "0811" });
        _db.App.Customers.Save(new Customer { Code = "CST002", Name = "Rina Melati" });
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(Services(_db.App), dialogs, _db.App.Auth.Login("admin", "admin"));
        var page = (ListPageViewModel<Customer>)shell.CreatePage(shell.NavItems.Single(n => n.Key == "master:customer"));

        Assert.Equal(3, page.Items.Count, "walk-in + 2");
        page.SearchText = "budi 0811";
        Assert.Equal(1, page.Items.Count);
        Assert.Equal("1 dari 3 data", page.StatusText);
        page.SearchText = "";
        page.SelectedItem = page.Items.Single(c => c.Code == "CST002");
        Assert.True(page.DeleteCommand.CanExecute(null));

        dialogs.ConfirmAnswer = false;
        page.DeleteCommand.Execute(null);
        Assert.Equal(3, page.Items.Count, "cancelled delete keeps data");
        dialogs.ConfirmAnswer = true;
        page.SelectedItem = page.Items.Single(c => c.Code == "CST002");
        page.DeleteCommand.Execute(null);
        Assert.Equal(2, page.Items.Count);

        page.SelectedItem = page.Items.Single(c => c.IsWalkIn);
        page.DeleteCommand.Execute(null);
        Assert.Contains("ERROR: Customer Walk-in terakhir", dialogs.Messages.Last());
    }

    [Test("VM-04", "Shell: menu sesuai hak akses KASIR, tab tidak dobel, laporan (M4) tampil 'segera', nav ringkas di layar sempit")]
    public void ShellNavigation()
    {
        var cashierRole = _db.App.Roles.GetAll().Single(r => r.Code == RoleCodes.Cashier).Id;
        _db.App.Users.Create(new AppUser { Username = "kasir1", DisplayName = "Kasir", RoleId = cashierRole }, "kasir123");
        var shell = new ShellViewModel(Services(_db.App), new FakeDialogs(), _db.App.Auth.Login("kasir1", "kasir123"));

        var keys = shell.NavItems.Select(n => n.Key).ToHashSet();
        Assert.True(keys.Contains("master:customer"));
        Assert.True(keys.Contains("trans:treatment"));
        Assert.False(keys.Contains("master:product"), "kasir cannot see product master");
        Assert.False(keys.Contains("master:user"));
        Assert.False(shell.CanBackup);

        var customer = shell.NavItems.Single(n => n.Key == "master:customer");
        shell.Navigate(customer);
        shell.Navigate(customer);
        Assert.Equal(2, shell.OpenPages.Count, "dashboard + customer, no duplicate tab");
        shell.Navigate(shell.NavItems.Single(n => n.Key == "trans:treatment"));
        Assert.True(shell.SelectedPage is MSPASalon.Presentation.ViewModels.Transactions.TreatmentPageViewModel, "M2 treatment page");
        shell.ClosePageCommand.Execute(shell.SelectedPage);
        shell.Navigate(shell.NavItems.Single(n => n.Key == "report"));
        Assert.True(shell.SelectedPage is ComingSoonViewModel, "M4 placeholder");
        shell.ClosePageCommand.Execute(shell.SelectedPage);
        Assert.Equal(2, shell.OpenPages.Count);
        Assert.False(keys.Contains("trans:purchase"), "kasir cannot purchase");
        shell.ClosePageCommand.Execute(shell.OpenPages[0]);
        Assert.Equal("dashboard", shell.OpenPages[0].Key, "dashboard cannot be closed");

        shell.OnAvailableWidthChanged(853);
        Assert.True(shell.IsNavCollapsed, "collapsed at 853 DIP");
        shell.OnAvailableWidthChanged(1366);
        Assert.False(shell.IsNavCollapsed, "expanded again on wide window");
    }

    [Test("VM-05", "Login: password salah tampil pesan & dikosongkan; berhasil memicu event")]
    public void LoginViewModel()
    {
        var vm = new LoginViewModel(_db.App.Auth, "Salon");
        UserSession? session = null;
        vm.LoggedIn += (_, s) => session = s;
        vm.Username = "admin"; vm.Password = "salah";
        vm.LoginCommand.Execute(null);
        Assert.Contains("salah", vm.ErrorText);
        Assert.Equal("", vm.Password);
        Assert.Null(session);
        vm.Password = "admin";
        vm.LoginCommand.Execute(null);
        Assert.NotNull(session);
        Assert.False(vm.HasError);
    }

    [Test("VM-06", "Editor role: ADMIN selalu semua hak akses; role baru tersimpan dengan centangan")]
    public void RoleEditor()
    {
        var admin = _db.App.Roles.GetAll().Single(r => r.Code == RoleCodes.Admin);
        var adminVm = new RoleEditorViewModel(_db.App.Roles, admin);
        Assert.True(adminVm.PermissionItems.All(p => p.IsChecked && !p.IsEditable));
        Assert.False(adminVm.IsCodeEditable);

        var vm = new RoleEditorViewModel(_db.App.Roles, null) { Code = "SUPERVISOR", Name = "Supervisor" };
        foreach (var p in vm.PermissionItems.Where(p => p.Module == "Laporan" || p.Code == Permissions.MasterProduct)) p.IsChecked = true;
        Assert.True(vm.TrySave(), vm.ErrorText ?? "");
        var saved = _db.App.Roles.GetAll().Single(r => r.Code == "SUPERVISOR");
        Assert.True(saved.Permissions.SetEquals([Permissions.ReportView, Permissions.MasterProduct]));
    }

    [Test("VM-07", "Editor user: konfirmasi password harus sama; user baru wajib ganti password default")]
    public void UserEditor()
    {
        var vm = new UserEditorViewModel(_db.App.Users, _db.App.Roles, _db.App.Employees, null)
        {
            Username = "kasir1", DisplayName = "Kasir Satu", Password = "kasir123", ConfirmPassword = "kasir124",
        };
        Assert.False(vm.TrySave());
        Assert.Contains("Konfirmasi password", vm.ErrorText);
        vm.ConfirmPassword = "kasir123";
        Assert.True(vm.TrySave(), vm.ErrorText ?? "");
        var u = _db.App.Users.GetAll().Single(x => x.Username == "kasir1");
        Assert.Equal(RoleCodes.Cashier, u.RoleCode, "default role KASIR");
        Assert.True(u.MustChangePassword);
    }
}
