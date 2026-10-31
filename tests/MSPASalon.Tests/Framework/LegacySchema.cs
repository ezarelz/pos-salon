using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Tests.Framework;

/// <summary>Builds a database exactly as an older app version left it (only migration 001 applied).</summary>
public static class LegacySchema
{
    /// <summary>Old (M1–M3.1) permission codes and KASIR defaults, before migration 003 / M4a.</summary>
    public static readonly string[] OldPermissionCodes =
    [
        "master.customer", "master.product", "master.service", "master.employee", "master.supplier", "master.category",
        "master.paymentmethod", "master.user", "master.role", "settings.branch", "trans.registration", "trans.treatment",
        "trans.sale", "trans.purchase", "trans.adjustment", "trans.cancelposted", "report.view", "admin.backup", "admin.import",
    ];

    public static void CreateV1(string path, bool withCashierRole)
    {
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        var v1 = DatabaseInitializer.LoadMigrations().Single(m => m.Version == 1);
        c.Execute(v1.Sql);
        c.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL)");
        c.Execute("INSERT INTO schema_version VALUES (1, @n, '2026-10-01 08:00:00')", new { n = v1.Name });
        c.Execute("INSERT INTO branch(id, short_name, name, paper_width_mm) VALUES (1, 'S', 'Salon', 58)");
        foreach (var code in OldPermissionCodes)
            c.Execute("INSERT INTO permission(code, name, module) VALUES (@code, @code, 'x')", new { code });
        if (withCashierRole)
        {
            c.Execute("INSERT INTO role(code, name, is_system) VALUES ('KASIR', 'Kasir', 1)");
            // Admin of the old installation had removed master.customer from KASIR: must NOT come back on upgrade.
            foreach (var code in new[] { "trans.registration", "trans.treatment", "trans.sale", "report.view" })
                c.Execute("INSERT INTO role_permission(role_id, permission_code) SELECT id, @code FROM role WHERE code = 'KASIR'", new { code });
        }
    }
}
