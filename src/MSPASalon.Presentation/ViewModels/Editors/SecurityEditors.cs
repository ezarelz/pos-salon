using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Validation;

namespace MSPASalon.Presentation.ViewModels.Editors;

public sealed record PermissionGroup(string Module, IReadOnlyList<PermissionItemViewModel> Items);

public sealed class PermissionItemViewModel(Permissions.Definition definition, bool isChecked, bool isEditable) : ViewModelBase
{
    private bool _isChecked = isChecked;
    public string Code => definition.Code;
    public string Name => definition.Name;
    public string Module => definition.Module;
    public bool IsEditable { get; } = isEditable;
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}

/// <summary>Role editor: new roles can be added later with any combination of permissions (decision A4).</summary>
public sealed class RoleEditorViewModel : MasterEditorViewModel<Role>
{
    public RoleEditorViewModel(IMasterService<Role> service, Role? existing) : base(service, existing)
    {
        var full = existing is null ? null : service.Get(existing.Id);
        IsSystem = full?.IsSystem ?? false;
        IsAdminRole = string.Equals(full?.Code, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase);
        var granted = full?.Permissions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PermissionItems = Permissions.All
            .Select(p => new PermissionItemViewModel(p, IsAdminRole || granted.Contains(p.Code), isEditable: !IsAdminRole))
            .ToList();
    }

    protected override string EntityLabel => "Role";
    public override double PreferredHeight => 640;
    public bool IsSystem { get; }
    public bool IsCodeEditable => !IsSystem;
    public bool IsAdminRole { get; }
    public string Hint => IsAdminRole
        ? "Role ADMIN selalu memiliki semua hak akses."
        : IsSystem ? "Role bawaan: kode tidak bisa diubah, hak akses boleh disesuaikan." : "Centang hak akses untuk role ini.";
    public IReadOnlyList<PermissionItemViewModel> PermissionItems { get; }
    public IReadOnlyList<PermissionGroup> PermissionGroups => PermissionItems
        .GroupBy(p => p.Module).Select(g => new PermissionGroup(g.Key, g.ToList())).ToList();

    protected override void Save()
    {
        var role = new Role
        {
            Id = Id, Code = Code, Name = Name,
            Permissions = PermissionItems.Where(p => p.IsChecked).Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
        SavedId = Service.Save(role);
    }
}

public sealed class UserEditorViewModel : EditorViewModel
{
    private readonly IUserService _users;
    private readonly long _id;
    private string _username = "", _displayName = "", _password = "", _confirmPassword = "";
    private long _roleId;
    private long? _employeeId;
    private bool _isActive = true, _mustChangePassword = true;

    public UserEditorViewModel(IUserService users, IMasterService<Role> roles, IMasterService<Employee> employees, AppUser? existing)
    {
        _users = users;
        Roles = roles.GetAll().Select(r => new Option<long>(r.Id, $"{r.Name} ({r.Code})")).ToList();
        Employees = new List<Option<long?>> { new(null, "(tidak terhubung)") }
            .Concat(employees.GetAll().Where(e => e.IsActive).Select(e => new Option<long?>(e.Id, $"{e.Name} ({e.Code})"))).ToList();
        _roleId = Roles.FirstOrDefault(r => r.Label.Contains($"({RoleCodes.Cashier})"))?.Value ?? Roles.FirstOrDefault()?.Value ?? 0;
        if (existing is null) return;
        _id = existing.Id; _username = existing.Username; _displayName = existing.DisplayName; _roleId = existing.RoleId;
        _employeeId = existing.EmployeeId; _isActive = existing.IsActive; _mustChangePassword = existing.MustChangePassword;
    }

    public bool IsNew => _id == 0;
    public override string Title => IsNew ? "Tambah User" : "Ubah User";
    public override double PreferredHeight => 560;
    public IReadOnlyList<Option<long>> Roles { get; }
    public IReadOnlyList<Option<long?>> Employees { get; }

    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public long RoleId { get => _roleId; set => SetProperty(ref _roleId, value); }
    public long? EmployeeId { get => _employeeId; set => SetProperty(ref _employeeId, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public bool MustChangePassword { get => _mustChangePassword; set => SetProperty(ref _mustChangePassword, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }

    protected override void Save()
    {
        var user = new AppUser
        {
            Id = _id, Username = Username, DisplayName = DisplayName, RoleId = RoleId, EmployeeId = EmployeeId,
            IsActive = IsActive, MustChangePassword = MustChangePassword,
        };
        if (IsNew)
        {
            if (Password != ConfirmPassword) throw new BusinessException("Konfirmasi password tidak sama.");
            _users.Create(user, Password);
        }
        else
        {
            _users.Update(user);
        }
    }
}

public sealed class ResetPasswordEditorViewModel(IUserService users, AppUser user) : EditorViewModel
{
    private string _password = "", _confirm = "";
    public override string Title => $"Reset Password: {user.Username}";
    public override string SaveLabel => "Reset Password";
    public override double PreferredWidth => 520;
    public override double PreferredHeight => 340;
    public string Info => "User wajib mengganti password ini saat login berikutnya.";
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string ConfirmPassword { get => _confirm; set => SetProperty(ref _confirm, value); }

    protected override void Save()
    {
        if (Password != ConfirmPassword) throw new BusinessException("Konfirmasi password tidak sama.");
        users.ResetPassword(user.Id, Password);
    }
}

/// <summary>Change own password; also shown automatically after login when the password must be changed.</summary>
public sealed class ChangePasswordViewModel(IAuthService auth, long userId, bool forced) : EditorViewModel
{
    private string _current = "", _new = "", _confirm = "";
    public override string Title => "Ganti Password";
    public override double PreferredWidth => 520;
    public override double PreferredHeight => 400;
    public bool Forced { get; } = forced;
    public string Info => Forced
        ? "Demi keamanan, Anda wajib mengganti password sebelum melanjutkan."
        : $"Password minimal {MasterValidators.MinPasswordLength} karakter.";
    public string CurrentPassword { get => _current; set => SetProperty(ref _current, value); }
    public string NewPassword { get => _new; set => SetProperty(ref _new, value); }
    public string ConfirmPassword { get => _confirm; set => SetProperty(ref _confirm, value); }

    protected override void Save()
    {
        if (NewPassword != ConfirmPassword) throw new BusinessException("Konfirmasi password baru tidak sama.");
        auth.ChangePassword(userId, CurrentPassword, NewPassword);
    }
}
