namespace MSPASalon.Core.Entities;

public sealed class Role : IMasterEntity
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Built-in roles (ADMIN, KASIR) cannot be deleted or renamed.</summary>
    public bool IsSystem { get; set; }
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int UserCount { get; set; }
}

public sealed class AppUser
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public long RoleId { get; set; }
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
}

/// <summary>The logged-in user and the permissions resolved from the user's role.</summary>
public sealed class UserSession
{
    public required AppUser User { get; init; }
    public required IReadOnlySet<string> Permissions { get; init; }
    public DateTime LoggedInAt { get; init; }

    public bool IsAdmin => string.Equals(User.RoleCode, Domain.RoleCodes.Admin, StringComparison.OrdinalIgnoreCase);

    public bool Can(string permission) => IsAdmin || Permissions.Contains(permission);
}
