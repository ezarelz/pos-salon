using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Security;
using MSPASalon.Core.Validation;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>Roles are data: new roles can be added at any time with any permission set (requirement A4).</summary>
public sealed class RoleService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Role>(db, clock)
{
    protected override string Table => "role";
    protected override string CodePrefix => "ROLE";
    protected override string EntityLabel => "Role";

    private const string Select = """
        SELECT r.id, r.code, r.name, r.is_system,
               (SELECT COUNT(*) FROM app_user u WHERE u.role_id = r.id) AS user_count
        FROM role r
        """;

    public override IReadOnlyList<Role> GetAll()
    {
        using var c = Db.Open();
        var roles = c.Query<Role>(Select + " ORDER BY r.is_system DESC, r.code").ToList();
        foreach (var r in roles) LoadPermissions(c, r);
        return roles;
    }

    public override Role? Get(long id)
    {
        using var c = Db.Open();
        var r = c.QuerySingleOrDefault<Role>(Select + " WHERE r.id = @id", new { id });
        if (r is not null) LoadPermissions(c, r);
        return r;
    }

    private static void LoadPermissions(SqliteConnection c, Role r) =>
        r.Permissions = c.Query<string>("SELECT permission_code FROM role_permission WHERE role_id = @id", new { id = r.Id })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    protected override List<string> ValidateFields(Role e) => MasterValidators.Validate(e);

    protected override void ValidateWithDatabase(SqliteConnection c, SqliteTransaction tx, Role e, List<string> errors)
    {
        if (e.Id <= 0) { e.IsSystem = false; return; }
        var existing = c.QuerySingle<(string Code, long IsSystem)>("SELECT code, is_system FROM role WHERE id = @id", new { id = e.Id }, tx);
        e.IsSystem = existing.IsSystem == 1;
        if (e.IsSystem && !string.Equals(existing.Code, e.Code, StringComparison.OrdinalIgnoreCase))
            errors.Add("Kode role bawaan (ADMIN/KASIR) tidak bisa diubah.");
    }

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Role e, string now) =>
        c.ExecuteScalar<long>("INSERT INTO role(code, name, is_system) VALUES (@Code, @Name, 0) RETURNING id", new { e.Code, e.Name }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Role e, string now) =>
        c.Execute("UPDATE role SET code = @Code, name = @Name WHERE id = @Id", new { e.Id, e.Code, e.Name }, tx);

    protected override void AfterSave(SqliteConnection c, SqliteTransaction tx, Role e)
    {
        c.Execute("DELETE FROM role_permission WHERE role_id = @id", new { id = e.Id }, tx);
        var perms = string.Equals(e.Code, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase)
            ? Permissions.All.Select(p => p.Code)          // ADMIN always has everything
            : e.Permissions;
        foreach (var p in perms)
            c.Execute("INSERT INTO role_permission(role_id, permission_code) VALUES (@r, @p)", new { r = e.Id, p }, tx);
    }

    protected override void BeforeDelete(SqliteConnection c, SqliteTransaction tx, long id)
    {
        if (c.ExecuteScalar<long>("SELECT is_system FROM role WHERE id = @id", new { id }, tx) == 1)
            throw new BusinessException("Role bawaan (ADMIN/KASIR) tidak bisa dihapus.");
        var users = c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE role_id = @id", new { id }, tx);
        if (users > 0) throw new BusinessException($"Role masih dipakai oleh {users} user. Pindahkan user ke role lain terlebih dahulu.");
    }
}

public sealed class UserService(DbConnectionFactory db, IClock clock) : IUserService
{
    private const string Select = """
        SELECT u.id, u.username, u.display_name, u.employee_id, e.name AS employee_name, u.role_id,
               r.code AS role_code, r.name AS role_name, u.is_active, u.must_change_password
        FROM app_user u JOIN role r ON r.id = u.role_id LEFT JOIN employee e ON e.id = u.employee_id
        """;

    public IReadOnlyList<AppUser> GetAll()
    {
        using var c = db.Open();
        return c.Query<AppUser>(Select + " ORDER BY u.username").ToList();
    }

    public AppUser? Get(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<AppUser>(Select + " WHERE u.id = @id", new { id });
    }

    private static void Normalize(AppUser u)
    {
        u.Username = (u.Username ?? "").Trim();
        u.DisplayName = (u.DisplayName ?? "").Trim();
        if (u.EmployeeId is <= 0) u.EmployeeId = null;
    }

    private static void ValidateCommon(SqliteConnection c, SqliteTransaction tx, AppUser u, List<string> errors)
    {
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE username = @n COLLATE NOCASE AND id <> @id",
                new { n = u.Username, id = u.Id }, tx) > 0)
            errors.Add($"Username '{u.Username}' sudah dipakai.");
        if (u.RoleId > 0 && c.ExecuteScalar<long>("SELECT COUNT(*) FROM role WHERE id = @id", new { id = u.RoleId }, tx) == 0)
            errors.Add("Role tidak ditemukan.");
        if (u.EmployeeId.HasValue && c.ExecuteScalar<long>("SELECT COUNT(*) FROM employee WHERE id = @id", new { id = u.EmployeeId }, tx) == 0)
            errors.Add("Karyawan tidak ditemukan.");
    }

    public long Create(AppUser user, string password)
    {
        Normalize(user);
        var errors = MasterValidators.Validate(user);
        errors.AddRange(MasterValidators.ValidatePassword(password));
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        ValidateCommon(c, tx, user, errors);
        if (errors.Count > 0) throw new BusinessException(errors);

        var h = PasswordHasher.Hash(password);
        var now = DbTime.ToDb(clock.Now);
        user.Id = c.ExecuteScalar<long>("""
            INSERT INTO app_user(username, display_name, employee_id, role_id, password_hash, password_salt,
                                 password_iterations, is_active, must_change_password, created_at, updated_at)
            VALUES (@Username, @DisplayName, @EmployeeId, @RoleId, @h, @s, @i, @IsActive, @MustChangePassword, @now, @now)
            RETURNING id
            """, new { user.Username, user.DisplayName, user.EmployeeId, user.RoleId, h = h.Hash, s = h.Salt, i = h.Iterations,
                       user.IsActive, user.MustChangePassword, now }, tx);
        tx.Commit();
        return user.Id;
    }

    public void Update(AppUser user)
    {
        Normalize(user);
        var errors = MasterValidators.Validate(user);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE id = @id", new { id = user.Id }, tx) == 0)
            throw new BusinessException("User tidak ditemukan.");
        ValidateCommon(c, tx, user, errors);
        if (errors.Count == 0 && !WouldKeepActiveAdmin(c, tx, user.Id, user.RoleId, user.IsActive))
            errors.Add("Harus ada minimal satu user Administrator yang aktif.");
        if (errors.Count > 0) throw new BusinessException(errors);

        c.Execute("""
            UPDATE app_user SET username = @Username, display_name = @DisplayName, employee_id = @EmployeeId,
                   role_id = @RoleId, is_active = @IsActive, must_change_password = @MustChangePassword, updated_at = @now
            WHERE id = @Id
            """, new { user.Id, user.Username, user.DisplayName, user.EmployeeId, user.RoleId, user.IsActive,
                       user.MustChangePassword, now = DbTime.ToDb(clock.Now) }, tx);
        tx.Commit();
    }

    public void ResetPassword(long userId, string newPassword)
    {
        var errors = MasterValidators.ValidatePassword(newPassword);
        if (errors.Count > 0) throw new BusinessException(errors);
        var h = PasswordHasher.Hash(newPassword);
        using var c = db.Open();
        var n = c.Execute("""
            UPDATE app_user SET password_hash = @h, password_salt = @s, password_iterations = @i,
                   must_change_password = 1, updated_at = @now WHERE id = @id
            """, new { h = h.Hash, s = h.Salt, i = h.Iterations, now = DbTime.ToDb(clock.Now), id = userId });
        if (n == 0) throw new BusinessException("User tidak ditemukan.");
    }

    public void Delete(long userId, long currentUserId)
    {
        if (userId == currentUserId) throw new BusinessException("Anda tidak bisa menghapus user yang sedang login.");
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        if (!WouldKeepActiveAdmin(c, tx, userId, newRoleId: null, newActive: false))
            throw new BusinessException("Harus ada minimal satu user Administrator yang aktif.");
        if (c.Execute("DELETE FROM app_user WHERE id = @id", new { id = userId }, tx) == 0)
            throw new BusinessException("User tidak ditemukan.");
        tx.Commit();
    }

    /// <summary>True if, after changing/removing <paramref name="userId"/>, at least one active ADMIN user remains.</summary>
    private static bool WouldKeepActiveAdmin(SqliteConnection c, SqliteTransaction tx, long userId, long? newRoleId, bool newActive)
    {
        var adminRoleId = c.ExecuteScalar<long>("SELECT id FROM role WHERE code = @c", new { c = RoleCodes.Admin }, tx);
        var others = c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE role_id = @r AND is_active = 1 AND id <> @id",
            new { r = adminRoleId, id = userId }, tx);
        return others > 0 || (newActive && newRoleId == adminRoleId);
    }
}

public sealed class AuthService(DbConnectionFactory db, IClock clock) : IAuthService
{
    private sealed class CredentialRow
    {
        public long Id { get; set; }
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public int PasswordIterations { get; set; }
        public bool IsActive { get; set; }
    }

    private const string InvalidCredentials = "Username atau password salah.";

    public UserSession Login(string username, string password)
    {
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<CredentialRow>("""
            SELECT id, password_hash, password_salt, password_iterations, is_active
            FROM app_user WHERE username = @u COLLATE NOCASE
            """, new { u = (username ?? "").Trim() });
        if (row is null || !PasswordHasher.Verify(password ?? "", row.PasswordHash, row.PasswordSalt, row.PasswordIterations))
            throw new BusinessException(InvalidCredentials);
        if (!row.IsActive) throw new BusinessException("User tidak aktif. Hubungi Administrator.");
        return BuildSession(c, row.Id, clock.Now);
    }

    public UserSession Refresh(UserSession session)
    {
        using var c = db.Open();
        return BuildSession(c, session.User.Id, session.LoggedInAt);
    }

    private static UserSession BuildSession(SqliteConnection c, long userId, DateTime loggedInAt)
    {
        var user = c.QuerySingle<AppUser>("""
            SELECT u.id, u.username, u.display_name, u.employee_id, e.name AS employee_name, u.role_id,
                   r.code AS role_code, r.name AS role_name, u.is_active, u.must_change_password
            FROM app_user u JOIN role r ON r.id = u.role_id LEFT JOIN employee e ON e.id = u.employee_id
            WHERE u.id = @id
            """, new { id = userId });
        var perms = c.Query<string>("SELECT permission_code FROM role_permission WHERE role_id = @r", new { r = user.RoleId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new UserSession { User = user, Permissions = perms, LoggedInAt = loggedInAt };
    }

    public void ChangePassword(long userId, string currentPassword, string newPassword)
    {
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<CredentialRow>(
            "SELECT id, password_hash, password_salt, password_iterations, is_active FROM app_user WHERE id = @id", new { id = userId })
            ?? throw new BusinessException("User tidak ditemukan.");
        if (!PasswordHasher.Verify(currentPassword ?? "", row.PasswordHash, row.PasswordSalt, row.PasswordIterations))
            throw new BusinessException("Password lama salah.");
        var errors = MasterValidators.ValidatePassword(newPassword);
        if (errors.Count == 0 && newPassword == currentPassword) errors.Add("Password baru harus berbeda dari password lama.");
        if (errors.Count > 0) throw new BusinessException(errors);

        var h = PasswordHasher.Hash(newPassword);
        c.Execute("""
            UPDATE app_user SET password_hash = @h, password_salt = @s, password_iterations = @i,
                   must_change_password = 0, updated_at = @now WHERE id = @id
            """, new { h = h.Hash, s = h.Salt, i = h.Iterations, now = DbTime.ToDb(clock.Now), id = userId });
    }
}
