using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Validation;

namespace MSPASalon.Presentation.ViewModels.Editors;

public sealed class ProductEditorViewModel : MasterEditorViewModel<Product>
{
    private long? _categoryId;
    private string _unit = "PCS";
    private string? _contentQtyText, _contentUnit;
    private string _costPriceText = "0", _sellPriceText = "0", _stockMinText = "0", _stockMaxText = "0";
    private bool _isSellable = true, _isConsumable, _isActive = true;

    public ProductEditorViewModel(IMasterService<Product> service, IMasterService<Category> categories, Product? existing)
        : base(service, existing)
    {
        Categories = new List<Option<long?>> { new(null, "(tanpa kategori)") }
            .Concat(categories.GetAll().Select(c => new Option<long?>(c.Id, $"{c.Name} ({c.Code})"))).ToList();
        if (existing is null) return;
        _categoryId = existing.CategoryId; _unit = existing.Unit;
        _contentQtyText = existing.ContentQty.HasValue ? Quantity.Format(existing.ContentQty.Value) : null;
        _contentUnit = existing.ContentUnit;
        _costPriceText = Money.Format(existing.CostPrice); _sellPriceText = Money.Format(existing.SellPrice);
        _stockMinText = Quantity.Format(existing.StockMin); _stockMaxText = Quantity.Format(existing.StockMax);
        _isSellable = existing.IsSellable; _isConsumable = existing.IsConsumable; _isActive = existing.IsActive;
        StockOnHandText = $"{Quantity.Format(existing.StockOnHand)} {existing.Unit}";
    }

    protected override string EntityLabel => "Produk";
    public override double PreferredHeight => 600;
    public IReadOnlyList<Option<long?>> Categories { get; }
    /// <summary>Read-only: stock only changes through transactions (ledger), never by editing the master.</summary>
    public string StockOnHandText { get; } = "0 (produk baru)";

    public long? CategoryId { get => _categoryId; set => SetProperty(ref _categoryId, value); }
    public string Unit { get => _unit; set => SetProperty(ref _unit, value); }
    public string? ContentQtyText { get => _contentQtyText; set => SetProperty(ref _contentQtyText, value); }
    public string? ContentUnit { get => _contentUnit; set => SetProperty(ref _contentUnit, value); }
    public string CostPriceText { get => _costPriceText; set => SetProperty(ref _costPriceText, value); }
    public string SellPriceText { get => _sellPriceText; set => SetProperty(ref _sellPriceText, value); }
    public string StockMinText { get => _stockMinText; set => SetProperty(ref _stockMinText, value); }
    public string StockMaxText { get => _stockMaxText; set => SetProperty(ref _stockMaxText, value); }
    public bool IsSellable { get => _isSellable; set => SetProperty(ref _isSellable, value); }
    public bool IsConsumable { get => _isConsumable; set => SetProperty(ref _isConsumable, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    protected override void Save()
    {
        var input = new InputErrors();
        var p = new Product
        {
            Id = Id, Code = Code, Name = Name, CategoryId = CategoryId, Unit = Unit,
            ContentQty = input.OptionalQty(ContentQtyText, "Isi per satuan"), ContentUnit = ContentUnit,
            CostPrice = input.Money(CostPriceText, "Harga pokok"), SellPrice = input.Money(SellPriceText, "Harga jual"),
            StockMin = input.Qty(StockMinText, "Stok minimum"), StockMax = input.Qty(StockMaxText, "Stok maksimum"),
            IsSellable = IsSellable, IsConsumable = IsConsumable, IsActive = IsActive,
        };
        input.ThrowIfAny();
        SavedId = Service.Save(p);
    }
}

/// <summary>One recipe line in the service editor; qty is edited as text (Indonesian decimal comma).</summary>
public sealed class MaterialLineViewModel : ViewModelBase
{
    private string _qtyText;

    public MaterialLineViewModel(Product product, decimal qty)
    {
        Product = product;
        _qtyText = Quantity.Format(qty);
    }

    public Product Product { get; }
    public string ProductLabel => $"{Product.Name} ({Product.Code})";
    public string Unit => Product.Unit;

    public string QtyText
    {
        get => _qtyText;
        set
        {
            if (!SetProperty(ref _qtyText, value)) return;
            OnPropertyChanged(nameof(EquivalentText));
            OnPropertyChanged(nameof(CostText));
        }
    }

    public decimal? Qty => Quantity.TryParse(_qtyText, out var q) ? q : null;

    /// <summary>Informational only (K9): "≈ 20 ml" when the product has content per unit.</summary>
    public string EquivalentText =>
        Qty is { } q && Product.ContentQty is { } content && !string.IsNullOrWhiteSpace(Product.ContentUnit)
            ? $"≈ {Quantity.Format(decimal.Round(q * content, Quantity.Decimals))} {Product.ContentUnit}"
            : "";

    /// <summary>Standard material cost per customer = round(qty × cost price) (A6).</summary>
    public decimal Cost => Qty is { } q ? Money.LineGross(q, Product.CostPrice) : 0m;
    public string CostText => Money.Format(Cost);
}

/// <summary>
/// Service master with its consumable recipe (BOM, decision K9). Also shows the standard unit economics:
/// price − commission − material cost = margin per customer (accounting view, BUSINESS_RULES §12).
/// </summary>
public sealed class ServiceEditorViewModel : MasterEditorViewModel<Service>
{
    private string _priceText = "0", _commissionText = "0";
    private bool _isActive = true;
    private Product? _productToAdd;
    private string _qtyToAdd = "";

    public ServiceEditorViewModel(IMasterService<Service> service, IMasterService<Product> products, Service? existing)
        : base(service, existing)
    {
        var all = products.GetAll();
        ConsumableProducts = all.Where(p => p.IsConsumable && p.IsActive).OrderBy(p => p.Name).ToList();
        AddMaterialCommand = new RelayCommand(AddMaterial);
        RemoveMaterialCommand = new RelayCommand<MaterialLineViewModel>(RemoveMaterial);
        Materials.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (INotifyPropertyChanged i in e.NewItems) i.PropertyChanged += (_, _) => RaiseEconomics();
            RaiseEconomics();
        };

        if (existing is null) return;
        var full = service.Get(existing.Id) ?? existing;
        _priceText = Money.Format(full.Price); _commissionText = Money.Format(full.Commission); _isActive = full.IsActive;
        var byId = all.ToDictionary(p => p.Id);
        foreach (var m in full.Materials)
            if (byId.TryGetValue(m.ProductId, out var p)) Materials.Add(new MaterialLineViewModel(p, m.Qty));
    }

    protected override string EntityLabel => "Jasa";
    public override double PreferredWidth => 820;
    public override double PreferredHeight => 640;

    public IReadOnlyList<Product> ConsumableProducts { get; }
    public bool HasConsumables => ConsumableProducts.Count > 0;
    public ObservableCollection<MaterialLineViewModel> Materials { get; } = [];
    public RelayCommand AddMaterialCommand { get; }
    public RelayCommand<MaterialLineViewModel> RemoveMaterialCommand { get; }

    public string PriceText { get => _priceText; set { if (SetProperty(ref _priceText, value)) RaiseEconomics(); } }
    public string CommissionText { get => _commissionText; set { if (SetProperty(ref _commissionText, value)) RaiseEconomics(); } }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public Product? ProductToAdd { get => _productToAdd; set => SetProperty(ref _productToAdd, value); }
    public string QtyToAdd { get => _qtyToAdd; set => SetProperty(ref _qtyToAdd, value); }

    public decimal MaterialCost => Materials.Sum(m => m.Cost);
    public string MaterialCostText => Money.Format(MaterialCost);

    public string MarginText
    {
        get
        {
            if (!Money.TryParse(_priceText, out var price) || !Money.TryParse(_commissionText, out var commission)) return "-";
            return Money.Format(price - commission - MaterialCost);
        }
    }

    private void RaiseEconomics()
    {
        OnPropertyChanged(nameof(MaterialCostText));
        OnPropertyChanged(nameof(MarginText));
    }

    private void AddMaterial()
    {
        ErrorText = null;
        if (ProductToAdd is null) { ErrorText = "Pilih bahan terlebih dahulu."; return; }
        if (!Quantity.TryParse(QtyToAdd, out var qty) || qty <= 0)
        {
            ErrorText = $"Qty pemakaian harus lebih dari 0, maksimal {Quantity.Decimals} desimal (mis. 0,1).";
            return;
        }
        if (Materials.Any(m => m.Product.Id == ProductToAdd.Id))
        {
            ErrorText = $"'{ProductToAdd.Name}' sudah ada di resep. Ubah qty-nya langsung di tabel.";
            return;
        }
        Materials.Add(new MaterialLineViewModel(ProductToAdd, qty));
        ProductToAdd = null;
        QtyToAdd = "";
    }

    private void RemoveMaterial(MaterialLineViewModel? line)
    {
        if (line is not null) Materials.Remove(line);
    }

    protected override void Save()
    {
        var input = new InputErrors();
        var s = new Service
        {
            Id = Id, Code = Code, Name = Name, IsActive = IsActive,
            Price = input.Money(PriceText, "Harga jasa"), Commission = input.Money(CommissionText, "Komisi"),
        };
        foreach (var m in Materials)
            s.Materials.Add(new ServiceMaterial
            {
                ProductId = m.Product.Id, ProductName = m.Product.Name,
                Qty = input.Qty(m.QtyText, $"Qty pemakaian '{m.Product.Name}'"),
            });
        input.ThrowIfAny();
        SavedId = Service.Save(s);
    }
}
