using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;

namespace MSPASalon.Presentation.ViewModels.Editors;

public static class GenderOptions
{
    public static readonly IReadOnlyList<Option<string?>> All =
    [
        new(null, "-"),
        new(Gender.Male, "Laki-laki"),
        new(Gender.Female, "Perempuan"),
    ];
}

/// <summary>Code + Name shared by every master editor.</summary>
public abstract class MasterEditorViewModel<T> : EditorViewModel where T : class, IMasterEntity
{
    protected readonly IMasterService<T> Service;
    protected readonly long Id;
    private string _code = "";
    private string _name = "";

    protected MasterEditorViewModel(IMasterService<T> service, T? existing)
    {
        Service = service;
        Id = existing?.Id ?? 0;
        _code = existing?.Code ?? service.SuggestNextCode();
        _name = existing?.Name ?? "";
    }

    protected abstract string EntityLabel { get; }
    public bool IsNew => Id == 0;
    public override string Title => IsNew ? $"Tambah {EntityLabel}" : $"Ubah {EntityLabel}";

    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public long SavedId { get; protected set; }
}

public sealed class CustomerEditorViewModel : MasterEditorViewModel<Customer>
{
    private string? _phone, _address, _notes, _gender;
    private bool _isMember, _isWalkIn, _isActive = true;

    public CustomerEditorViewModel(IMasterService<Customer> service, Customer? existing) : base(service, existing)
    {
        if (existing is null) return;
        _phone = existing.Phone; _address = existing.Address; _notes = existing.Notes; _gender = existing.Gender;
        _isMember = existing.IsMember; _isWalkIn = existing.IsWalkIn; _isActive = existing.IsActive;
    }

    protected override string EntityLabel => "Customer";
    public IReadOnlyList<Option<string?>> Genders => GenderOptions.All;
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string? Gender { get => _gender; set => SetProperty(ref _gender, value); }
    public bool IsMember { get => _isMember; set => SetProperty(ref _isMember, value); }
    public bool IsWalkIn { get => _isWalkIn; set => SetProperty(ref _isWalkIn, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    protected override void Save() => SavedId = Service.Save(new Customer
    {
        Id = Id, Code = Code, Name = Name, Phone = Phone, Address = Address, Notes = Notes, Gender = Gender,
        IsMember = IsMember, IsWalkIn = IsWalkIn, IsActive = IsActive,
    });
}

public sealed class SupplierEditorViewModel : MasterEditorViewModel<Supplier>
{
    private string? _phone, _address, _contact;
    private bool _isActive = true;

    public SupplierEditorViewModel(IMasterService<Supplier> service, Supplier? existing) : base(service, existing)
    {
        if (existing is null) return;
        _phone = existing.Phone; _address = existing.Address; _contact = existing.ContactPerson; _isActive = existing.IsActive;
    }

    protected override string EntityLabel => "Supplier";
    public override double PreferredHeight => 460;
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string? ContactPerson { get => _contact; set => SetProperty(ref _contact, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    protected override void Save() => SavedId = Service.Save(new Supplier
    {
        Id = Id, Code = Code, Name = Name, Phone = Phone, Address = Address, ContactPerson = ContactPerson, IsActive = IsActive,
    });
}

public sealed class CategoryEditorViewModel(IMasterService<Category> service, Category? existing)
    : MasterEditorViewModel<Category>(service, existing)
{
    protected override string EntityLabel => "Kategori";
    public override double PreferredWidth => 560;
    public override double PreferredHeight => 320;

    protected override void Save() => SavedId = Service.Save(new Category { Id = Id, Code = Code, Name = Name });
}

public sealed class EmployeeEditorViewModel : MasterEditorViewModel<Employee>
{
    private string? _phone, _address, _position, _gender;
    private bool _isCapster, _isActive = true;

    public EmployeeEditorViewModel(IMasterService<Employee> service, Employee? existing) : base(service, existing)
    {
        if (existing is null) return;
        _phone = existing.Phone; _address = existing.Address; _position = existing.Position; _gender = existing.Gender;
        _isCapster = existing.IsCapster; _isActive = existing.IsActive;
    }

    protected override string EntityLabel => "Karyawan";
    public IReadOnlyList<Option<string?>> Genders => GenderOptions.All;
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string? Position { get => _position; set => SetProperty(ref _position, value); }
    public string? Gender { get => _gender; set => SetProperty(ref _gender, value); }
    public bool IsCapster { get => _isCapster; set => SetProperty(ref _isCapster, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    protected override void Save() => SavedId = Service.Save(new Employee
    {
        Id = Id, Code = Code, Name = Name, Phone = Phone, Address = Address, Position = Position, Gender = Gender,
        IsCapster = IsCapster, IsActive = IsActive,
    });
}

public sealed class PaymentMethodEditorViewModel : MasterEditorViewModel<PaymentMethod>
{
    private bool _isCash, _isActive = true;
    private int _sortOrder;

    public PaymentMethodEditorViewModel(IMasterService<PaymentMethod> service, PaymentMethod? existing) : base(service, existing)
    {
        if (existing is null) return;
        _isCash = existing.IsCash; _isActive = existing.IsActive; _sortOrder = existing.SortOrder;
    }

    protected override string EntityLabel => "Metode Bayar";
    public override double PreferredWidth => 600;
    public override double PreferredHeight => 400;
    public bool IsCash { get => _isCash; set => SetProperty(ref _isCash, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public int SortOrder { get => _sortOrder; set => SetProperty(ref _sortOrder, value); }

    protected override void Save() => SavedId = Service.Save(new PaymentMethod
    {
        Id = Id, Code = Code, Name = Name, IsCash = IsCash, IsActive = IsActive, SortOrder = SortOrder,
    });
}
