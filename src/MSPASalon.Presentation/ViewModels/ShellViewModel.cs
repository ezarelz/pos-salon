using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Reports;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.Layout;
using MSPASalon.Presentation.Services;
using MSPASalon.Presentation.ViewModels.Editors;
using MSPASalon.Presentation.ViewModels.Masters;
using MSPASalon.Presentation.ViewModels.Transactions;

namespace MSPASalon.Presentation.ViewModels;

/// <summary>Navigation entry. <see cref="Glyph"/> is a Segoe MDL2 Assets code point (built into Windows 10/11).</summary>
public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);

public sealed record NavItem(string Section, string Title, string Key, string Glyph, string? Permission, string? Milestone = null)
{
    public bool IsComingSoon => Milestone is not null;
    public string ToolTip => IsComingSoon ? $"{Title} (tersedia di {Milestone})" : Title;
}

/// <summary>
/// Main window state: permission-filtered navigation, open tabs, and global actions
/// (backup, legacy import, change password, logout).
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly PresentationServices _s;
    private readonly IDialogService _dialogs;
    private UserSession _session;
    private PageViewModel? _selectedPage;
    private bool _isNavCollapsed;
    private bool _userCollapsedNav;
    private string _salonName;

    public ShellViewModel(PresentationServices services, IDialogService dialogs, UserSession session)
    {
        _s = services;
        _dialogs = dialogs;
        _session = session;
        _salonName = services.Branch.Get().Name;

        NavigateCommand = new RelayCommand<NavItem>(Navigate);
        ClosePageCommand = new RelayCommand<PageViewModel>(ClosePage);
        ToggleNavCommand = new RelayCommand(() => { _userCollapsedNav = !IsNavCollapsed; IsNavCollapsed = !IsNavCollapsed; });
        BackupCommand = new RelayCommand(Backup, () => Session.Can(Permissions.AdminBackup));
        ImportCommand = new RelayCommand(Import, () => Session.Can(Permissions.AdminImport));
        ChangePasswordCommand = new RelayCommand(() => _dialogs.ShowEditor(new ChangePasswordViewModel(_s.Auth, Session.User.Id, forced: false)));
        LogoutCommand = new RelayCommand(() => LogoutRequested?.Invoke(this, EventArgs.Empty));
        ToggleThemeCommand = new RelayCommand(() =>
        {
            _s.Theme.SetDark(!_s.Theme.IsDark);
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(ThemeLabel));
            OnPropertyChanged(nameof(ThemeGlyph));
        });

        BuildNavigation();
        Navigate(NavItems.First(n => n.Key == "dashboard"));
    }

    public UserSession Session { get => _session; private set => SetProperty(ref _session, value); }
    public string SalonName { get => _salonName; private set => SetProperty(ref _salonName, value); }
    public string UserLabel => $"{Session.User.DisplayName} · {Session.User.RoleName}";
    public string WindowTitle => $"{SalonName} — POS Salon";

    public ObservableCollection<NavItem> NavItems { get; } = [];
    /// <summary>Navigation grouped by section, in display order (bound by the view).</summary>
    public IReadOnlyList<NavSection> NavSections { get; private set; } = [];
    public ObservableCollection<PageViewModel> OpenPages { get; } = [];

    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set { if (SetProperty(ref _selectedPage, value)) value?.Refresh(); }
    }

    public bool IsNavCollapsed { get => _isNavCollapsed; set => SetProperty(ref _isNavCollapsed, value); }

    public RelayCommand<NavItem> NavigateCommand { get; }
    public RelayCommand<PageViewModel> ClosePageCommand { get; }
    public RelayCommand ToggleNavCommand { get; }
    public RelayCommand BackupCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ChangePasswordCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public bool IsDarkMode => _s.Theme.IsDark;
    /// <summary>Label of the action (what clicking does), not of the current state.</summary>
    public string ThemeLabel => IsDarkMode ? "Mode Terang" : "Mode Gelap";
    public string ThemeGlyph => IsDarkMode ? "\uE706" : "\uE708";   // Segoe MDL2: Brightness (sun) / QuietHours (moon)
    public bool CanBackup => Session.Can(Permissions.AdminBackup);
    public bool CanImport => Session.Can(Permissions.AdminImport);

    public event EventHandler? LogoutRequested;

    /// <summary>Called by the view on resize: collapse the navigation on narrow windows unless the user chose otherwise.</summary>
    public void OnAvailableWidthChanged(double width)
    {
        if (WindowSizing.IsCompact(width)) IsNavCollapsed = true;
        else if (!_userCollapsedNav) IsNavCollapsed = false;
    }

    private void BuildNavigation()
    {
        var all = new List<NavItem>
        {
            new("Beranda", "Dashboard", "dashboard", "", null),

            new("Transaksi", "Registrasi Customer", "trans:registration", "\uE8FA", Permissions.TransRegistration),
            new("Transaksi", "Proses Treatment", "trans:treatment", "\uE916", Permissions.TransTreatment),
            new("Transaksi", "Riwayat Treatment", "trans:treatmenthistory", "\uE81C", Permissions.TransTreatment),
            new("Transaksi", "Penjualan Produk", "trans:sale", "\uE7BF", Permissions.TransSale),

            new("Inventori", "Pembelian", "trans:purchase", "\uE719", Permissions.TransPurchase),
            new("Inventori", "Stock Adjustment", "trans:adjustment", "\uE9D5", Permissions.TransAdjustment),
            new("Inventori", "Kartu Stok", "trans:stockcard", "\uE8A5", Permissions.ReportView),

            new("Master", "Customer", "master:customer", "", Permissions.MasterCustomer),
            new("Master", "Produk", "master:product", "", Permissions.MasterProduct),
            new("Master", "Jasa & Resep Bahan", "master:service", "", Permissions.MasterService),
            new("Master", "Karyawan / Capster", "master:employee", "", Permissions.MasterEmployee),
            new("Master", "Supplier", "master:supplier", "", Permissions.MasterSupplier),
            new("Master", "Kategori Produk", "master:category", "", Permissions.MasterCategory),
            new("Master", "Metode Bayar", "master:paymentmethod", "", Permissions.MasterPaymentMethod),

            new("Laporan", "Rekap Kas Harian", "report:cash", "\uE8C7", Permissions.ReportView),
            new("Laporan", "Pendapatan", "report:revenue", "\uE9D2", Permissions.ReportFinance),
            new("Laporan", "Komisi Capster", "report:commission", "\uE716", Permissions.ReportCommission),
            new("Laporan", "Jasa & Margin", "report:servicemargin", "\uE8EF", Permissions.ReportFinance),
            new("Laporan", "Produk & Laba Kotor", "report:productprofit", "\uE7B8", Permissions.ReportFinance),
            new("Laporan", "Laporan Stok", "report:stock", "\uE7C3", Permissions.ReportView),
            new("Laporan", "Kunjungan Customer", "report:customervisit", "\uE77B", Permissions.ReportCustomer),
            new("Laporan", "Pembatalan", "report:cancellation", "\uE711", Permissions.ReportAudit),

            new("Pengaturan", "Pengaturan Salon", "settings:branch", "", Permissions.SettingsBranch),
            new("Pengaturan", "User", "master:user", "", Permissions.MasterUser),
            new("Pengaturan", "Role & Hak Akses", "master:role", "", Permissions.MasterRole),
        };
        NavItems.Clear();
        foreach (var item in all.Where(i => i.Permission is null || Session.Can(i.Permission))) NavItems.Add(item);
        NavSections = NavItems.GroupBy(i => i.Section).Select(g => new NavSection(g.Key, g.ToList())).ToList();
        OnPropertyChanged(nameof(NavSections));
    }

    public void Navigate(NavItem? item)
    {
        if (item is null) return;
        var existing = OpenPages.FirstOrDefault(p => p.Key == item.Key);
        if (existing is not null) { SelectedPage = existing; return; }
        var page = CreatePage(item);
        OpenPages.Add(page);
        SelectedPage = page;
    }

    private void ClosePage(PageViewModel? page)
    {
        if (page is null || page.Key == "dashboard") return;   // dashboard always stays open
        var index = OpenPages.IndexOf(page);
        OpenPages.Remove(page);
        if (SelectedPage == page || SelectedPage is null)
            SelectedPage = OpenPages.Count == 0 ? null : OpenPages[Math.Clamp(index - 1, 0, OpenPages.Count - 1)];
    }

    public PageViewModel CreatePage(NavItem item)
    {
        if (item.IsComingSoon)
            return new ComingSoonViewModel(item.Key, item.Title, item.Milestone!,
                "Modul ini dijadwalkan pada milestone berikutnya. Aturan bisnisnya sudah ditetapkan di BUSINESS_RULES.md dan struktur databasenya sudah tersedia.");

        bool Can(string p) => Session.Can(p);
        var d = _dialogs;
        return item.Key switch
        {
            "dashboard" => new DashboardViewModel(_s.Dashboard, _s.Clock, SalonName, Session.User.DisplayName,
                NavItems.Where(n => n.Key is "trans:registration" or "trans:treatment" or "trans:sale").ToList(), Navigate,
                showCommission: Can(Permissions.ReportCommission)),
            "report:cash" => new Reports.CashDailyReportViewModel(_s, d, Session),
            "report:revenue" => new Reports.RevenueReportViewModel(_s, d, Session),
            "report:commission" => new Reports.CommissionReportViewModel(_s, d, Session),
            "report:servicemargin" => new Reports.ServiceMarginReportViewModel(_s, d, Session),
            "report:productprofit" => new Reports.ProductProfitReportViewModel(_s, d, Session),
            "report:stock" => new Reports.StockReportViewModel(_s, d, Session),
            "report:cancellation" => new Reports.CancellationReportViewModel(_s, d, Session),
            "report:customervisit" => new Reports.CustomerVisitReportViewModel(_s, d, Session),

            "master:customer" => WithVisitHistory(new ListPageViewModel<Customer>(item.Key, item.Title,
                [
                    new("Kode", nameof(Customer.Code), MinWidth: 90), new("Nama", nameof(Customer.Name), Star: true, MinWidth: 160),
                    new("No. HP", nameof(Customer.Phone), MinWidth: 110), new("Member", nameof(Customer.IsMember), ColumnKind.Bool, 70),
                    new("Walk-in", nameof(Customer.IsWalkIn), ColumnKind.Bool, 70), new("Aktif", nameof(Customer.IsActive), ColumnKind.Bool, 60),
                ], d, _s.Customers.GetAll, x => new CustomerEditorViewModel(_s.Customers, x),
                x => _s.Customers.Delete(x.Id), x => $"{x.Code} {x.Name} {x.Phone}", x => x.Name,
                Can(Permissions.MasterCustomer))),

            "master:product" => new ListPageViewModel<Product>(item.Key, item.Title,
                [
                    new("Kode", nameof(Product.Code), MinWidth: 90), new("Nama", nameof(Product.Name), Star: true, MinWidth: 180),
                    new("Kategori", nameof(Product.CategoryName), MinWidth: 110), new("Satuan", nameof(Product.Unit), MinWidth: 60),
                    new("Stok", nameof(Product.StockOnHand), ColumnKind.Qty, 70), new("Harga Pokok", nameof(Product.CostPrice), ColumnKind.Money, 100),
                    new("Harga Jual", nameof(Product.SellPrice), ColumnKind.Money, 100), new("Dijual", nameof(Product.IsSellable), ColumnKind.Bool, 60),
                    new("Bahan", nameof(Product.IsConsumable), ColumnKind.Bool, 60), new("Aktif", nameof(Product.IsActive), ColumnKind.Bool, 55),
                ], d, _s.Products.GetAll, x => new ProductEditorViewModel(_s.Products, _s.Categories, x),
                x => _s.Products.Delete(x.Id), x => $"{x.Code} {x.Name} {x.CategoryName}", x => x.Name,
                Can(Permissions.MasterProduct), x => _s.Products.DeleteWarning(x.Id)),

            "master:service" => new ListPageViewModel<Service>(item.Key, item.Title,
                [
                    new("Kode", nameof(Service.Code), MinWidth: 90), new("Nama Jasa", nameof(Service.Name), Star: true, MinWidth: 180),
                    new("Harga", nameof(Service.Price), ColumnKind.Money, 100), new("Komisi", nameof(Service.Commission), ColumnKind.Money, 100),
                    new("Jml Bahan", nameof(Service.MaterialCount), MinWidth: 80), new("Aktif", nameof(Service.IsActive), ColumnKind.Bool, 55),
                ], d, _s.Services.GetAll, x => new ServiceEditorViewModel(_s.Services, _s.Products, x),
                x => _s.Services.Delete(x.Id), x => $"{x.Code} {x.Name}", x => x.Name, Can(Permissions.MasterService)),

            "master:employee" => new ListPageViewModel<Employee>(item.Key, item.Title,
                [
                    new("Kode", nameof(Employee.Code), MinWidth: 90), new("Nama", nameof(Employee.Name), Star: true, MinWidth: 160),
                    new("Jabatan", nameof(Employee.Position), MinWidth: 130), new("No. Telp", nameof(Employee.Phone), MinWidth: 110),
                    new("Capster", nameof(Employee.IsCapster), ColumnKind.Bool, 70), new("Aktif", nameof(Employee.IsActive), ColumnKind.Bool, 55),
                ], d, _s.Employees.GetAll, x => new EmployeeEditorViewModel(_s.Employees, x),
                x => _s.Employees.Delete(x.Id), x => $"{x.Code} {x.Name} {x.Position}", x => x.Name,
                Can(Permissions.MasterEmployee), x => _s.Employees.DeleteWarning(x.Id)),

            "master:supplier" => new ListPageViewModel<Supplier>(item.Key, item.Title,
                [
                    new("Kode", nameof(Supplier.Code), MinWidth: 90), new("Nama", nameof(Supplier.Name), Star: true, MinWidth: 160),
                    new("Kontak", nameof(Supplier.ContactPerson), MinWidth: 120), new("No. Telp", nameof(Supplier.Phone), MinWidth: 110),
                    new("Aktif", nameof(Supplier.IsActive), ColumnKind.Bool, 55),
                ], d, _s.Suppliers.GetAll, x => new SupplierEditorViewModel(_s.Suppliers, x),
                x => _s.Suppliers.Delete(x.Id), x => $"{x.Code} {x.Name} {x.ContactPerson}", x => x.Name, Can(Permissions.MasterSupplier)),

            "master:category" => new ListPageViewModel<Category>(item.Key, item.Title,
                [new("Kode", nameof(Category.Code), MinWidth: 90), new("Nama Kategori", nameof(Category.Name), Star: true, MinWidth: 160)],
                d, _s.Categories.GetAll, x => new CategoryEditorViewModel(_s.Categories, x),
                x => _s.Categories.Delete(x.Id), x => $"{x.Code} {x.Name}", x => x.Name,
                Can(Permissions.MasterCategory), x => _s.Categories.DeleteWarning(x.Id)),

            "master:paymentmethod" => new ListPageViewModel<PaymentMethod>(item.Key, item.Title,
                [
                    new("Kode", nameof(PaymentMethod.Code), MinWidth: 90), new("Nama", nameof(PaymentMethod.Name), Star: true, MinWidth: 160),
                    new("Tunai", nameof(PaymentMethod.IsCash), ColumnKind.Bool, 60), new("Urutan", nameof(PaymentMethod.SortOrder), MinWidth: 60),
                    new("Aktif", nameof(PaymentMethod.IsActive), ColumnKind.Bool, 55),
                ], d, _s.PaymentMethods.GetAll, x => new PaymentMethodEditorViewModel(_s.PaymentMethods, x),
                x => _s.PaymentMethods.Delete(x.Id), x => $"{x.Code} {x.Name}", x => x.Name, Can(Permissions.MasterPaymentMethod)),

            "master:role" => new ListPageViewModel<Role>(item.Key, item.Title,
                [
                    new("Kode", nameof(Role.Code), MinWidth: 90), new("Nama Role", nameof(Role.Name), Star: true, MinWidth: 160),
                    new("Bawaan", nameof(Role.IsSystem), ColumnKind.Bool, 70), new("Jml User", nameof(Role.UserCount), MinWidth: 70),
                ], d, _s.Roles.GetAll, x => new RoleEditorViewModel(_s.Roles, x),
                x => _s.Roles.Delete(x.Id), x => $"{x.Code} {x.Name}", x => x.Name, Can(Permissions.MasterRole)),

            "master:user" => CreateUsersPage(item),

            "settings:branch" => CreateBranchPage(),

            "trans:registration" => CreateRegistrationPage(item),
            "trans:treatment" => new TreatmentPageViewModel(_s, _dialogs, Session, PrintTreatment),
            "trans:treatmenthistory" => CreateTreatmentHistoryPage(item),
            "trans:sale" => CreateSalePage(item),
            "trans:purchase" => CreatePurchasePage(item),
            "trans:adjustment" => CreateAdjustmentPage(item),
            "trans:stockcard" => new StockCardPageViewModel(_s),

            _ => throw new InvalidOperationException($"Unknown page '{item.Key}'."),
        };
    }

    // ================================================================== transactions (M2 / M3)

    private string UserName => Session.User.Username;
    private DateTime Today => _s.Clock.Now.Date;

    private void PrintLines(IReadOnlyList<string> lines, string jobName)
    {
        try
        {
            _s.Print.PrintReceipt(lines, _s.Branch.Get().PaperWidthMm, jobName, _s.Branch.GetLogo());
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            _dialogs.Error($"Gagal mencetak: {ex.Message}");
        }
    }

    private IReadOnlyList<string> ReceiptLines(ReceiptData data) =>
        ReceiptBuilder.Build(data, ReceiptBuilder.CharsPerLine(data.Branch.PaperWidthMm));

    public void PrintTreatment(Treatment t) => PrintLines(ReceiptLines(ReceiptBuilder.ForTreatment(t, _s.Branch.Get())), $"Nota {t.DocNo}");

    public void PrintSale(Sale s) => PrintLines(ReceiptLines(ReceiptBuilder.ForSale(s, _s.Branch.Get())), $"Nota {s.DocNo}");

    /// <summary>Opens the treatment screen with the given registration selected ("Proses Treatment").</summary>
    public void OpenTreatment(long registrationId)
    {
        var nav = NavItems.FirstOrDefault(n => n.Key == "trans:treatment");
        if (nav is null) { _dialogs.Error("Anda tidak memiliki hak akses Proses Treatment."); return; }
        Navigate(nav);
        (SelectedPage as TreatmentPageViewModel)?.Select(registrationId);
    }

    private bool CancelDocument(string title, string info, Action<string> action) =>
        _dialogs.ShowEditor(new CancelReasonEditorViewModel(title, info, action));

    private PageViewModel CreateRegistrationPage(NavItem item)
    {
        ListPageViewModel<Registration>? page = null;
        page = new ListPageViewModel<Registration>(item.Key, item.Title,
            [
                new("No. Reg", nameof(Registration.RegNo), MinWidth: 110), new("Waktu", nameof(Registration.RegAt), ColumnKind.DateTime, 120),
                new("Customer", nameof(Registration.CustomerName), MinWidth: 140),
                new("Jasa (Capster)", nameof(Registration.ServiceSummary), Star: true, MinWidth: 200),
                new("Total", nameof(Registration.Total), ColumnKind.Money, 90), new("Status", nameof(Registration.Status), MinWidth: 90),
            ], _dialogs, () => [], x => new RegistrationEditorViewModel(_s, UserName, x),
            _ => null, x => $"{x.RegNo} {x.CustomerName} {x.ServiceSummary} {x.Status}", x => x.RegNo,
            Session.Can(Permissions.TransRegistration), options: new ListPageOptions<Registration>
            {
                AddLabel = "Registrasi Baru", DeleteLabel = "Batalkan",
                LoadRange = _s.Registrations.GetList,
                CanDeleteItem = x => x.Status == RegistrationStatus.Waiting,
                CustomDelete = x => CancelDocument($"Batalkan Registrasi {x.RegNo}", "Registrasi keluar dari antrian proses.",
                    reason => _s.Registrations.Cancel(x.Id, reason, UserName)),
                AfterAdd = editor =>
                {
                    if (editor is RegistrationEditorViewModel { Result: { } r } &&
                        _dialogs.Confirm($"Registrasi {r.DocNo} tersimpan dan masuk antrian.{Environment.NewLine}Proses treatment sekarang?", "Registrasi"))
                        OpenTreatment(r.Id);
                },
            }, today: Today);
        var process = new RelayCommand(() => { if (page.Selected is { } r) OpenTreatment(r.Id); },
            () => page.Selected is { Status: RegistrationStatus.Waiting or RegistrationStatus.InProcess } && Session.Can(Permissions.TransTreatment));
        page.Extra.Add(new ActionItem("Proses Treatment", process));
        return page;
    }

    /// <summary>Master Customer → "Riwayat Kunjungan" (report.customer, §14 no. 8).</summary>
    private ListPageViewModel<Customer> WithVisitHistory(ListPageViewModel<Customer> page)
    {
        if (!Session.Can(Permissions.ReportCustomer)) return page;
        var history = new RelayCommand(() =>
        {
            if (page.Selected is not { } c) return;
            var lines = ReportBuilders.VisitHistoryText(c, _s.Reports.GetVisitHistory(c.Id), _s.Branch.GetVisitSettings(), _s.Clock.Now);
            _dialogs.ShowEditor(new DocumentDetailViewModel($"Riwayat Kunjungan — {c.Name}", lines, null));
        }, () => page.Selected is { IsWalkIn: false });
        page.Extra.Add(new ActionItem("Riwayat Kunjungan", history));
        return page;
    }

    private PageViewModel CreateTreatmentHistoryPage(NavItem item) =>
        new ListPageViewModel<Treatment>(item.Key, item.Title,
            [
                new("No.", nameof(Treatment.DocNo), MinWidth: 110), new("Mulai", nameof(Treatment.TimeIn), ColumnKind.DateTime, 120),
                new("Customer", nameof(Treatment.CustomerName), MinWidth: 140),
                new("Jasa (Capster)", nameof(Treatment.ServiceSummary), Star: true, MinWidth: 200),
                new("Total", nameof(Treatment.GrandTotal), ColumnKind.Money, 90), new("Bayar", nameof(Treatment.PaymentMethodName), MinWidth: 70),
                new("Status", nameof(Treatment.Status), MinWidth: 90),
            ], _dialogs, () => [],
            x =>
            {
                var t = _s.Treatments.Get(x!.Id) ?? throw new BusinessException("Treatment tidak ditemukan.");
                var lines = ReceiptLines(ReceiptBuilder.ForTreatment(t, _s.Branch.Get()));
                return new DocumentDetailViewModel($"Treatment {t.DocNo}", lines,
                    t.Status == TreatmentStatus.Paid ? () => PrintTreatment(t) : null);
            },
            _ => null, x => $"{x.DocNo} {x.CustomerName} {x.ServiceSummary} {x.Status}", x => x.DocNo,
            Session.Can(Permissions.TransTreatment), options: new ListPageOptions<Treatment>
            {
                AllowAdd = false, EditLabel = "Lihat / Cetak", EditIsView = true, DeleteLabel = "Batalkan",
                LoadRange = _s.Treatments.GetList,
                CanDeleteItem = x => x.Status == TreatmentStatus.InProgress ||
                                     (x.Status == TreatmentStatus.Paid && Session.Can(Permissions.TransCancelPosted)),
                CustomDelete = x => CancelDocument($"Batalkan Treatment {x.DocNo}",
                    x.Status == TreatmentStatus.Paid
                        ? "Treatment LUNAS dibatalkan: pemakaian bahan dikembalikan ke stok, pembayaran dihapus, komisi tidak dihitung."
                        : "Treatment yang sedang dikerjakan dibatalkan.",
                    reason => _s.Treatments.Cancel(x.Id, reason, Session.Can(Permissions.TransCancelPosted), UserName)),
            }, today: Today);

    private PageViewModel CreateSalePage(NavItem item) =>
        new ListPageViewModel<Sale>(item.Key, item.Title,
            [
                new("No. Faktur", nameof(Sale.DocNo), MinWidth: 110), new("Waktu", nameof(Sale.DocAt), ColumnKind.DateTime, 120),
                new("Customer", nameof(Sale.CustomerName), MinWidth: 140), new("Produk", nameof(Sale.LineSummary), Star: true, MinWidth: 180),
                new("Total", nameof(Sale.GrandTotal), ColumnKind.Money, 90), new("Bayar", nameof(Sale.PaymentMethodName), MinWidth: 70),
                new("Status", nameof(Sale.Status), MinWidth: 80),
            ], _dialogs, () => [],
            x =>
            {
                if (x is null) return new SaleEditorViewModel(_s, UserName);
                var s = _s.Sales.Get(x.Id) ?? throw new BusinessException("Penjualan tidak ditemukan.");
                return new DocumentDetailViewModel($"Penjualan {s.DocNo}", ReceiptLines(ReceiptBuilder.ForSale(s, _s.Branch.Get())),
                    s.Status == DocStatus.Posted ? () => PrintSale(s) : null);
            },
            _ => null, x => $"{x.DocNo} {x.CustomerName} {x.LineSummary} {x.Status}", x => x.DocNo,
            Session.Can(Permissions.TransSale), options: new ListPageOptions<Sale>
            {
                AddLabel = "Penjualan Baru", EditLabel = "Lihat / Cetak", EditIsView = true, DeleteLabel = "Batalkan",
                LoadRange = _s.Sales.GetList,
                CanDeleteItem = x => x.Status == DocStatus.Posted && Session.Can(Permissions.TransCancelPosted),
                CustomDelete = x => CancelDocument($"Batalkan Penjualan {x.DocNo}", "Stok produk dikembalikan dan pembayaran dihapus.",
                    reason => _s.Sales.Cancel(x.Id, reason, UserName)),
                AfterAdd = editor =>
                {
                    if (editor is not SaleEditorViewModel { Result: { } r }) return;
                    var sale = _s.Sales.Get(r.Id)!;
                    var msg = $"Penjualan {r.DocNo} tersimpan.";
                    if (sale.Payment is { IsCash: true } p) msg += $"{Environment.NewLine}Kembalian: Rp {Money.Format(p.ChangeAmount)}";
                    if (_dialogs.Confirm($"{msg}{Environment.NewLine}{Environment.NewLine}Cetak nota sekarang?", "Penjualan")) PrintSale(sale);
                },
            }, today: Today);

    private PageViewModel CreatePurchasePage(NavItem item) =>
        new ListPageViewModel<Purchase>(item.Key, item.Title,
            [
                new("No. Beli", nameof(Purchase.DocNo), MinWidth: 110), new("Waktu", nameof(Purchase.DocAt), ColumnKind.DateTime, 120),
                new("Supplier", nameof(Purchase.SupplierName), MinWidth: 140), new("No. Nota Supplier", nameof(Purchase.SupplierRef), MinWidth: 110),
                new("Produk", nameof(Purchase.LineSummary), Star: true, MinWidth: 180),
                new("Total", nameof(Purchase.GrandTotal), ColumnKind.Money, 90), new("Status", nameof(Purchase.Status), MinWidth: 80),
            ], _dialogs, () => [],
            x =>
            {
                if (x is null) return new PurchaseEditorViewModel(_s, UserName);
                var p = _s.Purchases.Get(x.Id) ?? throw new BusinessException("Pembelian tidak ditemukan.");
                return new DocumentDetailViewModel($"Pembelian {p.DocNo}", DocumentText.Purchase(p), null);
            },
            _ => null, x => $"{x.DocNo} {x.SupplierName} {x.SupplierRef} {x.LineSummary} {x.Status}", x => x.DocNo,
            Session.Can(Permissions.TransPurchase), options: new ListPageOptions<Purchase>
            {
                AddLabel = "Pembelian Baru", EditLabel = "Lihat", EditIsView = true, DeleteLabel = "Batalkan",
                LoadRange = _s.Purchases.GetList, DefaultDays = 30,
                CanDeleteItem = x => x.Status == DocStatus.Posted && Session.Can(Permissions.TransCancelPosted),
                CustomDelete = x => CancelDocument($"Batalkan Pembelian {x.DocNo}",
                    "Stok yang diterima dikeluarkan kembali. Ditolak bila barang sudah terpakai/terjual sehingga stok menjadi minus.",
                    reason => _s.Purchases.Cancel(x.Id, reason, UserName)),
                AfterAdd = editor => { if (editor is PurchaseEditorViewModel { Result: { } r }) _dialogs.Info($"Pembelian {r.DocNo} tersimpan. Stok sudah bertambah."); },
            }, today: Today);

    private PageViewModel CreateAdjustmentPage(NavItem item) =>
        new ListPageViewModel<StockAdjustment>(item.Key, item.Title,
            [
                new("No. Adjust", nameof(StockAdjustment.DocNo), MinWidth: 110), new("Waktu", nameof(StockAdjustment.DocAt), ColumnKind.DateTime, 120),
                new("Alasan", nameof(StockAdjustment.Reason), MinWidth: 160), new("Produk", nameof(StockAdjustment.LineSummary), Star: true, MinWidth: 180),
                new("Nilai", nameof(StockAdjustment.TotalValue), ColumnKind.Money, 90), new("Status", nameof(StockAdjustment.Status), MinWidth: 80),
            ], _dialogs, () => [],
            x =>
            {
                if (x is null) return new AdjustmentEditorViewModel(_s, UserName);
                var a = _s.Adjustments.Get(x.Id) ?? throw new BusinessException("Adjustment tidak ditemukan.");
                return new DocumentDetailViewModel($"Adjustment {a.DocNo}", DocumentText.Adjustment(a), null);
            },
            _ => null, x => $"{x.DocNo} {x.Reason} {x.LineSummary} {x.Status}", x => x.DocNo,
            Session.Can(Permissions.TransAdjustment), options: new ListPageOptions<StockAdjustment>
            {
                AddLabel = "Adjustment Baru", EditLabel = "Lihat", EditIsView = true, DeleteLabel = "Batalkan",
                LoadRange = _s.Adjustments.GetList, DefaultDays = 30,
                CanDeleteItem = x => x.Status == DocStatus.Posted && Session.Can(Permissions.TransCancelPosted),
                CustomDelete = x => CancelDocument($"Batalkan Adjustment {x.DocNo}", "Koreksi stok dari dokumen ini dibatalkan.",
                    reason => _s.Adjustments.Cancel(x.Id, reason, UserName)),
            }, today: Today);

    private PageViewModel CreateUsersPage(NavItem item)
    {
        var page = new ListPageViewModel<AppUser>(item.Key, item.Title,
            [
                new("Username", nameof(AppUser.Username), MinWidth: 110), new("Nama", nameof(AppUser.DisplayName), Star: true, MinWidth: 150),
                new("Role", nameof(AppUser.RoleName), MinWidth: 110), new("Karyawan", nameof(AppUser.EmployeeName), MinWidth: 120),
                new("Aktif", nameof(AppUser.IsActive), ColumnKind.Bool, 55), new("Wajib Ganti Pwd", nameof(AppUser.MustChangePassword), ColumnKind.Bool, 110),
            ], _dialogs, _s.Users.GetAll, x => new UserEditorViewModel(_s.Users, _s.Roles, _s.Employees, x),
            x => { _s.Users.Delete(x.Id, Session.User.Id); return null; },
            x => $"{x.Username} {x.DisplayName} {x.RoleName}", x => x.Username, Session.Can(Permissions.MasterUser));

        var reset = new RelayCommand(() =>
        {
            if (page.Selected is { } u && _dialogs.ShowEditor(new ResetPasswordEditorViewModel(_s.Users, u))) page.Refresh();
        }, () => page.CanModify && page.Selected is not null);
        page.Extra.Add(new ActionItem("Reset Password", reset));
        return page;
    }

    private PageViewModel CreateBranchPage()
    {
        var page = new BranchSettingsViewModel(_s.Branch, _dialogs);
        page.Saved += (_, _) =>
        {
            SalonName = _s.Branch.Get().Name;
            OnPropertyChanged(nameof(WindowTitle));
        };
        return page;
    }

    private void Backup()
    {
        var folder = _dialogs.PickFolder("Pilih folder tujuan backup");
        if (folder is null) return;
        try
        {
            var file = _s.Backup.Backup(folder);
            _dialogs.Info($"Backup berhasil dibuat:{Environment.NewLine}{file}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BusinessException)
        {
            _dialogs.Error($"Backup gagal: {ex.Message}");
        }
    }

    private void Import()
    {
        if (_s.LegacyImport.TargetHasMasterData())
        {
            _dialogs.Error("Database sudah berisi master data. Import hanya bisa dilakukan sekali, ke database yang masih kosong.");
            return;
        }
        var file = _dialogs.PickOpenFile("Pilih salon.sqlite dari aplikasi lama", "Database MSPA lama (*.sqlite;*.db)|*.sqlite;*.db|Semua file (*.*)|*.*");
        if (file is null) return;
        if (!_dialogs.Confirm($"Import master data dari:{Environment.NewLine}{file}{Environment.NewLine}{Environment.NewLine}File lama hanya dibaca dan tidak akan diubah. Lanjutkan?"))
            return;
        try
        {
            var result = _s.LegacyImport.Import(file);
            _dialogs.Info(FormatImportResult(result), "Import Selesai");
            SalonName = _s.Branch.Get().Name;
            OnPropertyChanged(nameof(WindowTitle));
            Session = _s.Auth.Refresh(Session);
            foreach (var p in OpenPages) p.Refresh();
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message, "Import Gagal");
        }
    }

    public static string FormatImportResult(Core.Interfaces.ImportResult r)
    {
        var lines = new List<string> { "Data yang diimpor:" };
        lines.AddRange(r.Counts.Select(kv => $"  • {kv.Key}: {kv.Value}"));
        if (r.Warnings.Count > 0) { lines.Add(""); lines.Add("Peringatan:"); lines.AddRange(r.Warnings.Select(w => $"  • {w}")); }
        if (r.Notes.Count > 0) { lines.Add(""); lines.Add("Catatan:"); lines.AddRange(r.Notes.Select(n => $"  • {n}")); }
        return string.Join(Environment.NewLine, lines);
    }
}
