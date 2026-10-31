using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Security;

namespace MSPASalon.Infrastructure.Database;

/// <summary>
/// Creates/upgrades the database. Migrations are embedded *.sql files named NNN_description.sql and are
/// applied in order, each in its own transaction, and recorded in schema_version.
/// After migrating, reference data is seeded idempotently (permissions, built-in roles, branch row,
/// payment methods, walk-in customer, first admin user). NO sample/demo data is ever inserted.
/// </summary>
public sealed class DatabaseInitializer
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin";

    private readonly DbConnectionFactory _factory;
    private readonly IClock _clock;

    public DatabaseInitializer(DbConnectionFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public sealed record Migration(int Version, string Name, string Sql);

    public static IReadOnlyList<Migration> LoadMigrations()
    {
        var asm = typeof(DatabaseInitializer).Assembly;
        const string marker = ".Database.Migrations.";
        var list = new List<Migration>();
        foreach (var resource in asm.GetManifestResourceNames().Where(n => n.Contains(marker) && n.EndsWith(".sql")))
        {
            var file = resource[(resource.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
            // Embedded resource names replace a leading digit-prefixed segment with '_' (e.g. "_001_initial_schema.sql").
            var digits = new string(file.TrimStart('_').TakeWhile(char.IsAsciiDigit).ToArray());
            if (!int.TryParse(digits, out var version))
                throw new InvalidOperationException($"Migration resource '{resource}' has no numeric prefix.");
            using var stream = asm.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            list.Add(new Migration(version, file, reader.ReadToEnd()));
        }
        if (list.GroupBy(m => m.Version).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate migration version.");
        return list.OrderBy(m => m.Version).ToList();
    }

    public int CurrentVersion()
    {
        using var c = _factory.Open();
        EnsureVersionTable(c);
        return c.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 0;
    }

    /// <summary>Applies pending migrations and seeds reference data. Safe to call at every start-up.</summary>
    public void Initialize()
    {
        using (var c = _factory.Open())
        {
            c.Execute("PRAGMA journal_mode = WAL;");
            EnsureVersionTable(c);
            var current = c.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version") ?? 0;
            foreach (var m in LoadMigrations().Where(m => m.Version > current))
            {
                using var tx = DbConnectionFactory.BeginWrite(c);
                using (var cmd = c.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = m.Sql;
                    cmd.ExecuteNonQuery();
                }
                c.Execute("INSERT INTO schema_version(version, name, applied_at) VALUES (@v, @n, @t)",
                    new { v = m.Version, n = m.Name, t = DbTime.ToDb(_clock.Now) }, tx);
                tx.Commit();
            }
        }
        Seed();
    }

    private static void EnsureVersionTable(SqliteConnection c) =>
        c.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL)");

    private void Seed()
    {
        var now = DbTime.ToDb(_clock.Now);
        using var c = _factory.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);

        // Permission catalogue is code-defined; keep the table in sync (new permissions in future versions).
        var existingPermissions = c.Query<string>("SELECT code FROM permission", transaction: tx).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPermissions = Permissions.All.Select(p => p.Code).Where(code => !existingPermissions.Contains(code)).ToList();
        foreach (var p in Permissions.All)
            c.Execute("""
                INSERT INTO permission(code, name, module) VALUES (@Code, @Name, @Module)
                ON CONFLICT(code) DO UPDATE SET name = excluded.name, module = excluded.module
                """, p, tx);

        c.Execute("INSERT OR IGNORE INTO role(code, name, is_system) VALUES (@c, 'Administrator', 1)", new { c = RoleCodes.Admin }, tx);
        var cashierCreated = c.Execute("INSERT OR IGNORE INTO role(code, name, is_system) VALUES (@c, 'Kasir', 1)", new { c = RoleCodes.Cashier }, tx) > 0;
        var adminRoleId = c.ExecuteScalar<long>("SELECT id FROM role WHERE code = @c", new { c = RoleCodes.Admin }, tx);
        var cashierRoleId = c.ExecuteScalar<long>("SELECT id FROM role WHERE code = @c", new { c = RoleCodes.Cashier }, tx);

        // ADMIN always holds every permission, including ones added by later versions.
        c.Execute("INSERT OR IGNORE INTO role_permission(role_id, permission_code) SELECT @r, code FROM permission", new { r = adminRoleId }, tx);
        // New KASIR role: all defaults. Existing KASIR role (upgrade): only defaults introduced by this version,
        // so permissions the admin removed earlier are not given back.
        foreach (var p in Permissions.CashierDefaults.Where(p => cashierCreated || newPermissions.Contains(p)))
            c.Execute("INSERT OR IGNORE INTO role_permission(role_id, permission_code) VALUES (@r, @p)", new { r = cashierRoleId, p }, tx);

        c.Execute("INSERT OR IGNORE INTO branch(id, short_name, name, paper_width_mm) VALUES (1, 'MSPA', 'MSPA Salon', 58)", transaction: tx);

        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM payment_method", transaction: tx) == 0)
        {
            c.Execute("INSERT INTO payment_method(code, name, is_cash, sort_order) VALUES ('TUNAI', 'Tunai', 1, 1)", transaction: tx);
            c.Execute("INSERT INTO payment_method(code, name, is_cash, sort_order) VALUES ('QRIS', 'QRIS', 0, 2)", transaction: tx);
            c.Execute("INSERT INTO payment_method(code, name, is_cash, sort_order) VALUES ('TRANSFER', 'Transfer Bank', 0, 3)", transaction: tx);
        }

        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM customer WHERE is_walk_in = 1", transaction: tx) == 0
            && c.ExecuteScalar<long>("SELECT COUNT(*) FROM customer WHERE code = 'UMUM'", transaction: tx) == 0)
        {
            c.Execute("""
                INSERT INTO customer(code, name, is_walk_in, is_active, created_at, updated_at)
                VALUES ('UMUM', 'UMUM / Walk-in', 1, 1, @now, @now)
                """, new { now }, tx);
        }

        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user", transaction: tx) == 0)
        {
            var h = PasswordHasher.Hash(DefaultAdminPassword);
            c.Execute("""
                INSERT INTO app_user(username, display_name, role_id, password_hash, password_salt, password_iterations,
                                     is_active, must_change_password, created_at, updated_at)
                VALUES (@u, 'Administrator', @r, @h, @s, @i, 1, 1, @now, @now)
                """, new { u = DefaultAdminUsername, r = adminRoleId, h = h.Hash, s = h.Salt, i = h.Iterations, now }, tx);
        }

        tx.Commit();
    }
}
