using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;

namespace MSPASalon.Presentation.ViewModels.Transactions;

/// <summary>
/// Payment entry shared by Treatment and Sale (rules Y1–Y5, decision K8). Cash: amount received + change.
/// Non-cash (QRIS/Transfer): recorded at exactly the total, optional reference number.
/// </summary>
public sealed class PaymentViewModel : ViewModelBase
{
    private long _methodId;
    private decimal _amountDue;
    private string _receivedText = "";
    private string? _reference;

    public PaymentViewModel(IEnumerable<PaymentMethod> methods)
    {
        Methods = methods.Where(m => m.IsActive).OrderBy(m => m.SortOrder).ThenBy(m => m.Code).ToList();
        _methodId = (Methods.FirstOrDefault(m => m.IsCash) ?? Methods.FirstOrDefault())?.Id ?? 0;
        ExactCommand = new RelayCommand(() => ReceivedText = Money.Format(AmountDue));
    }

    public IReadOnlyList<PaymentMethod> Methods { get; }
    public RelayCommand ExactCommand { get; }
    public PaymentMethod? SelectedMethod => Methods.FirstOrDefault(m => m.Id == _methodId);
    public bool IsCash => SelectedMethod?.IsCash ?? true;
    public bool IsNonCash => !IsCash;

    public long MethodId
    {
        get => _methodId;
        set { if (SetProperty(ref _methodId, value)) RaiseAll(); }
    }

    public decimal AmountDue
    {
        get => _amountDue;
        set { if (SetProperty(ref _amountDue, value)) RaiseAll(); }
    }

    public string ReceivedText
    {
        get => _receivedText;
        set { if (SetProperty(ref _receivedText, value)) RaiseAll(); }
    }

    public string? Reference { get => _reference; set => SetProperty(ref _reference, value); }

    public string AmountDueText => Money.Format(AmountDue);

    /// <summary>"Kembali Rp 20.000", "Kurang Rp 5.000", or the non-cash note.</summary>
    public string ChangeText
    {
        get
        {
            if (!IsCash) return $"Dicatat {Money.Format(AmountDue)} (tanpa kembalian)";
            if (!Money.TryParse(_receivedText, out var received)) return "Isi uang diterima";
            var diff = received - AmountDue;
            return diff >= 0 ? $"Kembali {Money.Format(diff)}" : $"Kurang {Money.Format(-diff)}";
        }
    }

    public bool IsShort => IsCash && Money.TryParse(_receivedText, out var r) && r < AmountDue;

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(SelectedMethod)); OnPropertyChanged(nameof(IsCash)); OnPropertyChanged(nameof(IsNonCash));
        OnPropertyChanged(nameof(AmountDueText)); OnPropertyChanged(nameof(ChangeText)); OnPropertyChanged(nameof(IsShort));
    }

    public PaymentInput Build(InputErrors errors)
    {
        if (SelectedMethod is null) { errors.Items.Add("Metode bayar wajib dipilih."); return new PaymentInput(0, 0, null); }
        var received = IsCash ? errors.Money(ReceivedText, "Uang diterima") : 0m;
        return new PaymentInput(SelectedMethod.Id, received, Reference);
    }
}

/// <summary>One service + capster row on a registration or treatment.</summary>
public sealed class ServiceLineEditViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly long? _snapshotServiceId;
    private readonly decimal? _snapshotPrice;
    private long? _serviceId;
    private long? _employeeId;

    public ServiceLineEditViewModel(IReadOnlyList<Service> services, IReadOnlyList<Employee> capsters, Action changed,
        long? serviceId = null, long? employeeId = null, decimal? snapshotPrice = null)
    {
        Services = services; Capsters = capsters; _changed = changed;
        _serviceId = serviceId; _employeeId = employeeId; _snapshotServiceId = serviceId; _snapshotPrice = snapshotPrice;
    }

    public IReadOnlyList<Service> Services { get; }
    public IReadOnlyList<Employee> Capsters { get; }

    public long? ServiceId
    {
        get => _serviceId;
        set
        {
            if (!SetProperty(ref _serviceId, value)) return;
            OnPropertyChanged(nameof(Price)); OnPropertyChanged(nameof(PriceText));
            _changed();
        }
    }

    public long? EmployeeId
    {
        get => _employeeId;
        set { if (SetProperty(ref _employeeId, value)) _changed(); }
    }

    /// <summary>Snapshot price for the original service of the line, otherwise the current master price.</summary>
    public decimal Price => _serviceId is null ? 0
        : _serviceId == _snapshotServiceId && _snapshotPrice.HasValue ? _snapshotPrice.Value
        : Services.FirstOrDefault(s => s.Id == _serviceId)?.Price ?? 0;

    public string PriceText => Money.Format(Price);
}

/// <summary>Product row with qty / price / discount text inputs (sale, purchase, adjustment).</summary>
public sealed class ProductLineEditViewModel : ViewModelBase
{
    private readonly Action _changed;
    private string _qtyText, _priceText, _discountText = "0";

    public ProductLineEditViewModel(Product product, decimal qty, decimal price, Action changed)
    {
        Product = product; _changed = changed;
        _qtyText = Quantity.Format(qty);
        _priceText = Money.Format(price);
    }

    public Product Product { get; }
    public string ProductLabel => $"{Product.Name} ({Product.Code})";
    public string Unit => Product.Unit;
    public string StockText => $"{Quantity.Format(Product.StockOnHand)} {Product.Unit}";

    public string QtyText { get => _qtyText; set { if (SetProperty(ref _qtyText, value)) Changed(); } }
    public string PriceText { get => _priceText; set { if (SetProperty(ref _priceText, value)) Changed(); } }
    public string DiscountText { get => _discountText; set { if (SetProperty(ref _discountText, value)) Changed(); } }

    public decimal? Qty => Quantity.TryParse(_qtyText, out var q) ? q : null;
    public decimal? Price => Money.TryParse(_priceText, out var p) ? p : null;
    public decimal? Discount => string.IsNullOrWhiteSpace(_discountText) ? 0 : Money.TryParse(_discountText, out var d) ? d : null;

    public decimal Gross => Qty is { } q && Price is { } p ? Money.LineGross(q, p) : 0;
    public decimal Total => Math.Max(0, Gross - (Discount ?? 0));
    public string TotalText => Money.Format(Total);
    /// <summary>Adjustment: stock after the signed qty.</summary>
    public string AfterText => Qty is { } q ? $"{Quantity.Format(Product.StockOnHand + q)} {Product.Unit}" : "-";
    /// <summary>Adjustment: value at cost price.</summary>
    public string CostValueText => Qty is { } q ? Money.Format(Money.LineGross(q, Product.CostPrice)) : "-";

    private void Changed()
    {
        OnPropertyChanged(nameof(TotalText)); OnPropertyChanged(nameof(AfterText)); OnPropertyChanged(nameof(CostValueText));
        _changed();
    }
}

/// <summary>Helper for "pick product + qty + Tambah" bars that merge repeated products into one row.</summary>
public sealed class ProductPicker(IReadOnlyList<Product> products) : ViewModelBase
{
    private Product? _product;
    private string _qtyText = "1";

    public IReadOnlyList<Product> Products { get; } = products;
    public Product? Product { get => _product; set => SetProperty(ref _product, value); }
    public string QtyText { get => _qtyText; set => SetProperty(ref _qtyText, value); }

    /// <summary>Adds or merges into <paramref name="lines"/>; returns an error message or null.</summary>
    public string? AddTo(ObservableCollection<ProductLineEditViewModel> lines, Func<Product, decimal> defaultPrice, Action changed, bool allowNegative = false)
    {
        if (Product is null) return "Pilih produk terlebih dahulu.";
        if (!Quantity.TryParse(QtyText, out var qty) || qty == 0 || (!allowNegative && qty < 0))
            return allowNegative ? "Qty tidak valid (mis. 2 atau -0,5)." : "Qty harus lebih dari 0 (mis. 1 atau 0,5).";
        var existing = lines.FirstOrDefault(l => l.Product.Id == Product.Id);
        if (existing is not null)
            existing.QtyText = Quantity.Format((existing.Qty ?? 0) + qty);
        else
            lines.Add(new ProductLineEditViewModel(Product, qty, defaultPrice(Product), changed));
        Product = null;
        QtyText = "1";
        changed();
        return null;
    }
}
