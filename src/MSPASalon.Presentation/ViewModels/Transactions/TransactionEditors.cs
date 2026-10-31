using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Presentation.ViewModels.Transactions;

internal static class Lookups
{
    public static List<Option<long>> Customers(PresentationServices s) =>
        s.Customers.GetAll().Where(c => c.IsActive)
            .OrderByDescending(c => c.IsWalkIn).ThenBy(c => c.Name)
            .Select(c => new Option<long>(c.Id, $"{c.Name} ({c.Code})")).ToList();

    public static long DefaultCustomer(PresentationServices s) =>
        s.Customers.GetAll().FirstOrDefault(c => c.IsWalkIn && c.IsActive)?.Id ?? 0;

    public static List<Service> ActiveServices(PresentationServices s) => s.Services.GetAll().Where(x => x.IsActive).OrderBy(x => x.Name).ToList();

    public static List<Employee> ActiveCapsters(PresentationServices s) =>
        s.Employees.GetAll().Where(e => e.IsActive && e.IsCapster).OrderBy(e => e.Name).ToList();
}

/// <summary>Reason dialog for cancelling a document (K11). The action runs inside Save so errors stay in the dialog.</summary>
public sealed class CancelReasonEditorViewModel(string title, string info, Action<string> confirm) : EditorViewModel
{
    private string _reason = "";
    public override string Title => title;
    public override string SaveLabel => "Batalkan Dokumen";
    public override string CancelLabel => "Kembali";
    public override double PreferredWidth => 560;
    public override double PreferredHeight => 340;
    public string Info => info;
    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }

    protected override void Save() => confirm(Reason);
}

/// <summary>Read-only document view (receipt / purchase / adjustment) with optional printing.</summary>
public sealed class DocumentDetailViewModel(string title, IReadOnlyList<string> lines, Action? print) : EditorViewModel
{
    public override string Title => title;
    public override string SaveLabel => print is null ? "Tutup" : "Cetak";
    public override string CancelLabel => "Tutup";
    public override double PreferredWidth => 520;
    public override double PreferredHeight => 680;
    public bool CanPrint => print is not null;
    public string Text { get; } = string.Join(Environment.NewLine, lines);

    protected override void Save() => print?.Invoke();
}

/// <summary>Create / edit a registration while it is MENUNGGU (BUSINESS_RULES §3).</summary>
public sealed class RegistrationEditorViewModel : EditorViewModel
{
    private readonly PresentationServices _s;
    private readonly string _user;
    private readonly long _id;
    private long _customerId;
    private string? _notes;
    private string? _lastVisitText;
    private readonly bool _showLastVisit;

    public RegistrationEditorViewModel(PresentationServices s, string user, Registration? existing)
    {
        _s = s; _user = user;
        _showLastVisit = s.Branch.GetVisitSettings().ShowLastVisitOnRegistration;
        Customers = Lookups.Customers(s);
        Services = Lookups.ActiveServices(s);
        Capsters = Lookups.ActiveCapsters(s);
        AddLineCommand = new RelayCommand(() => { Lines.Add(NewLine()); Changed(); });
        RemoveLineCommand = new RelayCommand<ServiceLineEditViewModel>(l => { if (l is not null) { Lines.Remove(l); Changed(); } });

        if (existing is null)
        {
            _customerId = Lookups.DefaultCustomer(s);
            Lines.Add(NewLine());
            UpdateLastVisit();
            return;
        }
        var full = s.Registrations.Get(existing.Id) ?? throw new BusinessException("Registrasi tidak ditemukan.");
        if (full.Status != RegistrationStatus.Waiting)
            throw new BusinessException($"Registrasi {full.RegNo} berstatus {full.Status}; hanya registrasi MENUNGGU yang bisa diubah.");
        _id = full.Id; RegNo = full.RegNo; _customerId = full.CustomerId ?? 0; _notes = full.Notes;
        UpdateLastVisit();
        foreach (var l in full.Lines) Lines.Add(new ServiceLineEditViewModel(Services, Capsters, Changed, l.ServiceId, l.EmployeeId, l.Price));
    }

    private ServiceLineEditViewModel NewLine() => new(Services, Capsters, Changed);

    public bool IsNew => _id == 0;
    public string? RegNo { get; }
    public override string Title => IsNew ? "Registrasi Customer Baru" : $"Ubah Registrasi {RegNo}";
    public override double PreferredWidth => 820;
    public override double PreferredHeight => 600;
    public IReadOnlyList<Option<long>> Customers { get; }
    public IReadOnlyList<Service> Services { get; }
    public IReadOnlyList<Employee> Capsters { get; }
    public ObservableCollection<ServiceLineEditViewModel> Lines { get; } = [];
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ServiceLineEditViewModel> RemoveLineCommand { get; }
    public long CustomerId
    {
        get => _customerId;
        set { if (SetProperty(ref _customerId, value)) UpdateLastVisit(); }
    }

    /// <summary>K24 (optional, Pengaturan Salon): "Terakhir datang 49 hari lalu · Barber dengan Capster A".</summary>
    public string? LastVisitText { get => _lastVisitText; private set => SetProperty(ref _lastVisitText, value); }

    private void UpdateLastVisit()
    {
        if (!_showLastVisit) return;
        LastVisitText = _customerId == 0 || _s.Customers.Get(_customerId) is not { IsWalkIn: false } ? null
            : _s.Reports.GetLastVisit(_customerId, _s.Clock.Now)?.Text ?? "Belum pernah treatment di aplikasi ini.";
    }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string TotalText => Money.Format(Lines.Sum(l => l.Price));
    public bool HasNoCapster => Capsters.Count == 0;
    public PostResult? Result { get; private set; }
    public long SavedId { get; private set; }

    private void Changed() => OnPropertyChanged(nameof(TotalText));

    internal static List<ServiceLineInput> BuildLines(IEnumerable<ServiceLineEditViewModel> lines, List<string> errors)
    {
        var result = new List<ServiceLineInput>();
        var n = 0;
        foreach (var l in lines)
        {
            n++;
            if (l.ServiceId is null) { errors.Add($"Baris {n}: jasa belum dipilih."); continue; }
            if (l.EmployeeId is null) { errors.Add($"Baris {n}: capster belum dipilih."); continue; }
            result.Add(new ServiceLineInput(l.ServiceId.Value, l.EmployeeId.Value));
        }
        if (n == 0) errors.Add("Minimal satu jasa harus dipilih.");
        return result;
    }

    protected override void Save()
    {
        var errors = new List<string>();
        if (CustomerId <= 0) errors.Add("Customer wajib dipilih.");
        var lines = BuildLines(Lines, errors);
        if (errors.Count > 0) throw new BusinessException(errors);
        if (IsNew)
        {
            Result = _s.Registrations.Create(CustomerId, lines, Notes, _user);
            SavedId = Result.Id;
        }
        else
        {
            _s.Registrations.Update(_id, CustomerId, lines, Notes, _user);
            SavedId = _id;
        }
    }
}

/// <summary>Direct product sale; saved and posted when paid (BUSINESS_RULES §6).</summary>
public sealed class SaleEditorViewModel : EditorViewModel
{
    private readonly PresentationServices _s;
    private readonly string _user;
    private long _customerId;
    private string? _notes;

    public SaleEditorViewModel(PresentationServices s, string user)
    {
        _s = s; _user = user;
        Customers = Lookups.Customers(s);
        _customerId = Lookups.DefaultCustomer(s);
        Picker = new ProductPicker(s.Products.GetAll().Where(p => p.IsActive && p.IsSellable).OrderBy(p => p.Name).ToList());
        Payment = new PaymentViewModel(s.PaymentMethods.GetAll());
        AddLineCommand = new RelayCommand(() => ErrorText = Picker.AddTo(Lines, p => p.SellPrice, Changed));
        RemoveLineCommand = new RelayCommand<ProductLineEditViewModel>(l => { if (l is not null) { Lines.Remove(l); Changed(); } });
    }

    public override string Title => "Penjualan Produk";
    public override string SaveLabel => "Bayar & Simpan";
    public override double PreferredWidth => 960;
    public override double PreferredHeight => 700;
    public IReadOnlyList<Option<long>> Customers { get; }
    public ProductPicker Picker { get; }
    public PaymentViewModel Payment { get; }
    public ObservableCollection<ProductLineEditViewModel> Lines { get; } = [];
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ProductLineEditViewModel> RemoveLineCommand { get; }
    public long CustomerId { get => _customerId; set => SetProperty(ref _customerId, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public decimal Subtotal => Lines.Sum(l => l.Gross);
    public decimal DiscountTotal => Lines.Sum(l => Math.Min(l.Discount ?? 0, l.Gross));
    public decimal Total => Lines.Sum(l => l.Total);
    public string SubtotalText => Money.Format(Subtotal);
    public string DiscountTotalText => Money.Format(DiscountTotal);
    public string TotalText => Money.Format(Total);
    public PostResult? Result { get; private set; }

    private void Changed()
    {
        OnPropertyChanged(nameof(SubtotalText)); OnPropertyChanged(nameof(DiscountTotalText)); OnPropertyChanged(nameof(TotalText));
        Payment.AmountDue = Total;
    }

    protected override void Save()
    {
        var input = new InputErrors();
        if (CustomerId <= 0) input.Items.Add("Customer wajib dipilih.");
        if (Lines.Count == 0) input.Items.Add("Minimal satu produk harus ditambahkan.");
        var lines = new List<SaleLineInput>();
        foreach (var l in Lines)
        {
            var qty = input.Qty(l.QtyText, $"Qty {l.Product.Name}");
            var price = input.Money(l.PriceText, $"Harga {l.Product.Name}");
            var disc = input.Money(string.IsNullOrWhiteSpace(l.DiscountText) ? "0" : l.DiscountText, $"Diskon {l.Product.Name}");
            lines.Add(new SaleLineInput(l.Product.Id, qty, price, disc));
        }
        var pay = Payment.Build(input);
        input.ThrowIfAny();
        Result = _s.Sales.Post(CustomerId, lines, Notes, pay, _user);
    }
}

/// <summary>Purchase / goods receipt (BUSINESS_RULES §7).</summary>
public sealed class PurchaseEditorViewModel : EditorViewModel
{
    private readonly PresentationServices _s;
    private readonly string _user;
    private long _supplierId;
    private string? _supplierRef, _notes;

    public PurchaseEditorViewModel(PresentationServices s, string user)
    {
        _s = s; _user = user;
        Suppliers = s.Suppliers.GetAll().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new Option<long>(x.Id, $"{x.Name} ({x.Code})")).ToList();
        _supplierId = Suppliers.Count == 1 ? Suppliers[0].Value : 0;
        Picker = new ProductPicker(s.Products.GetAll().Where(p => p.IsActive).OrderBy(p => p.Name).ToList());
        AddLineCommand = new RelayCommand(() => ErrorText = Picker.AddTo(Lines, p => p.CostPrice, Changed));
        RemoveLineCommand = new RelayCommand<ProductLineEditViewModel>(l => { if (l is not null) { Lines.Remove(l); Changed(); } });
    }

    public override string Title => "Pembelian / Penerimaan Barang";
    public override string SaveLabel => "Simpan & Terima Barang";
    public override double PreferredWidth => 900;
    public override double PreferredHeight => 660;
    public IReadOnlyList<Option<long>> Suppliers { get; }
    public bool HasNoSupplier => Suppliers.Count == 0;
    public ProductPicker Picker { get; }
    public ObservableCollection<ProductLineEditViewModel> Lines { get; } = [];
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ProductLineEditViewModel> RemoveLineCommand { get; }
    public long SupplierId { get => _supplierId; set => SetProperty(ref _supplierId, value); }
    public string? SupplierRef { get => _supplierRef; set => SetProperty(ref _supplierRef, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string TotalText => Money.Format(Lines.Sum(l => l.Total));
    public PostResult? Result { get; private set; }

    private void Changed() => OnPropertyChanged(nameof(TotalText));

    protected override void Save()
    {
        var input = new InputErrors();
        if (SupplierId <= 0) input.Items.Add("Supplier wajib dipilih (tambahkan di Master Supplier bila belum ada).");
        if (Lines.Count == 0) input.Items.Add("Minimal satu produk harus ditambahkan.");
        var lines = Lines.Select(l => new PurchaseLineInput(l.Product.Id, input.Qty(l.QtyText, $"Qty {l.Product.Name}"),
            input.Money(l.PriceText, $"Harga beli {l.Product.Name}"))).ToList();
        input.ThrowIfAny();
        Result = _s.Purchases.Post(SupplierId, SupplierRef, lines, Notes, _user);
    }
}

/// <summary>Stock adjustment / opname with signed quantities (BUSINESS_RULES §8).</summary>
public sealed class AdjustmentEditorViewModel : EditorViewModel
{
    private readonly PresentationServices _s;
    private readonly string _user;
    private string _reason = "";

    public AdjustmentEditorViewModel(PresentationServices s, string user)
    {
        _s = s; _user = user;
        Picker = new ProductPicker(s.Products.GetAll().Where(p => p.IsActive).OrderBy(p => p.Name).ToList());
        AddLineCommand = new RelayCommand(() => ErrorText = Picker.AddTo(Lines, p => p.CostPrice, Changed, allowNegative: true));
        RemoveLineCommand = new RelayCommand<ProductLineEditViewModel>(l => { if (l is not null) { Lines.Remove(l); Changed(); } });
    }

    public override string Title => "Stock Adjustment / Opname";
    public override string SaveLabel => "Simpan Adjustment";
    public override double PreferredWidth => 880;
    public override double PreferredHeight => 620;
    public ProductPicker Picker { get; }
    public ObservableCollection<ProductLineEditViewModel> Lines { get; } = [];
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ProductLineEditViewModel> RemoveLineCommand { get; }
    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }
    public string TotalValueText => Money.Format(Lines.Sum(l => l.Qty is { } q ? Money.LineGross(q, l.Product.CostPrice) : 0));
    public PostResult? Result { get; private set; }

    private void Changed() => OnPropertyChanged(nameof(TotalValueText));

    protected override void Save()
    {
        var input = new InputErrors();
        if (Lines.Count == 0) input.Items.Add("Minimal satu produk harus ditambahkan.");
        var lines = Lines.Select(l => new AdjustmentLineInput(l.Product.Id, input.Qty(l.QtyText, $"Qty {l.Product.Name}"))).ToList();
        input.ThrowIfAny();
        Result = _s.Adjustments.Post(Reason, lines, _user);
    }
}

/// <summary>Plain-text renderings for documents without a customer receipt.</summary>
public static class DocumentText
{
    private const int Width = 48;

    public static IReadOnlyList<string> Purchase(Purchase p)
    {
        var o = Header("PEMBELIAN", p.DocNo, p.DocAt, p.Status, p.CancelReason);
        o.Add($"Supplier : {p.SupplierName}");
        if (!string.IsNullOrWhiteSpace(p.SupplierRef)) o.Add($"No. Nota : {p.SupplierRef}");
        o.Add($"Dibuat   : {p.CreatedBy}");
        o.Add(new string('-', Width));
        foreach (var l in p.Lines)
        {
            o.Add(l.ProductName);
            o.Add(ReceiptBuilder.LeftRight($"  {Quantity.Format(l.Qty)} {l.Unit} x {Money.Format(l.Price)}", Money.Format(l.LineTotal), Width));
        }
        o.Add(new string('-', Width));
        o.Add(ReceiptBuilder.LeftRight("TOTAL", Money.Format(p.GrandTotal), Width));
        if (!string.IsNullOrWhiteSpace(p.Notes)) o.Add($"Catatan: {p.Notes}");
        return o;
    }

    public static IReadOnlyList<string> Adjustment(StockAdjustment a)
    {
        var o = Header("STOCK ADJUSTMENT", a.DocNo, a.DocAt, a.Status, a.CancelReason);
        o.Add($"Alasan   : {a.Reason}");
        o.Add($"Dibuat   : {a.CreatedBy}");
        o.Add(new string('-', Width));
        foreach (var l in a.Lines)
        {
            o.Add(l.ProductName);
            var sign = l.Qty > 0 ? "+" : "";
            o.Add(ReceiptBuilder.LeftRight($"  {sign}{Quantity.Format(l.Qty)} {l.Unit} x {Money.Format(l.CostPrice)}", Money.Format(l.LineTotal), Width));
        }
        o.Add(new string('-', Width));
        o.Add(ReceiptBuilder.LeftRight("NILAI (harga pokok)", Money.Format(a.TotalValue), Width));
        return o;
    }

    private static List<string> Header(string title, string docNo, DateTime at, string status, string? cancelReason)
    {
        var o = new List<string> { title, $"No       : {docNo}", $"Tanggal  : {at:dd-MM-yyyy HH:mm}", $"Status   : {status}" };
        if (!string.IsNullOrWhiteSpace(cancelReason)) o.Add($"Alasan batal: {cancelReason}");
        return o;
    }
}
