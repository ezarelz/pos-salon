using Dapper;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class M4FoundationTests : IDisposable
{
    private readonly SalonFixture _f = new();
    private Infrastructure.AppServices A => _f.Db.App;
    public void Dispose() => _f.Dispose();

    [Test("M4-01", "Migrasi 003: snapshot biaya bahan per baris jasa saat LUNAS; batal mencatat cancelled_by/at + audit_log")]
    public void SnapshotsAndAudit()
    {
        // Creambath recipe: shampoo 0,1 x 20.000 + mask 0,15 x 30.000 = 6.500; Potong: no recipe = 0.
        var reg = A.Registrations.Create(_f.Customer, [new(_f.Creambath, _f.Capster1), new(_f.Haircut, _f.Capster2)], null, "admin");
        var t = A.Treatments.Start(reg.Id, "admin");
        A.Treatments.Complete(t.Id, 0, new PaymentInput(_f.Cash, 135000, null), "admin");
        using (var c = A.Db.Open())
        {
            var costs = c.Query<long?>("SELECT material_cost FROM treatment_line WHERE treatment_id = @id ORDER BY line_no", new { id = t.Id }).ToList();
            Assert.Equal(6500L, costs[0]!.Value, "creambath material cost snapshot");
            Assert.Equal(0L, costs[1]!.Value, "service without recipe = 0, not NULL");
        }
        // Later change of the product cost price must not change the snapshot.
        var shampoo = A.Products.Get(_f.Shampoo)!;
        shampoo.CostPrice = 99000;
        A.Products.Save(shampoo);
        using (var c = A.Db.Open())
            Assert.Equal(6500L, c.ExecuteScalar<long>("SELECT material_cost FROM treatment_line WHERE treatment_id = @id AND line_no = 1", new { id = t.Id }));

        _f.Db.Clock.Now = _f.Db.Clock.Now.AddMinutes(30);
        A.Treatments.Cancel(t.Id, "salah capster", allowPosted: true, "admin");
        var sale = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "kasir1");
        A.Sales.Cancel(sale.Id, "dobel input", "kasir1");
        var waiting = A.Registrations.Create(_f.Customer, [new(_f.Wash, _f.Capster1)], null, "kasir1");
        A.Registrations.Cancel(waiting.Id, "customer pulang", "kasir1");

        using var db = A.Db.Open();
        Assert.Equal("admin", db.ExecuteScalar<string>("SELECT cancelled_by FROM treatment WHERE id = @id", new { id = t.Id }));
        Assert.Equal("2026-10-31 10:00:00", db.ExecuteScalar<string>("SELECT cancelled_at FROM treatment WHERE id = @id", new { id = t.Id }));
        Assert.Equal("kasir1", db.ExecuteScalar<string>("SELECT cancelled_by FROM sale WHERE id = @id", new { id = sale.Id }));
        var audit = db.Query<(string Action, string DocType, string DocNo, string UserName, string Details)>(
            "SELECT action, doc_type, doc_no, user_name, details FROM audit_log ORDER BY id").ToList();
        Assert.Equal(3, audit.Count);
        Assert.Equal("TREATMENT", audit[0].DocType);
        Assert.Contains("LUNAS", audit[0].Details);
        Assert.Equal(sale.DocNo, audit[1].DocNo);
        Assert.Equal("REGISTRATION", audit[2].DocType);
        Assert.True(audit.All(a => a.Action == "CANCEL"));
    }

    [Test("M4-02", "Izin laporan: ADMIN semua izin baru; KASIR baru = view+customer; upgrade KASIR lama hanya dapat izin baru")]
    public void ReportPermissions()
    {
        var admin = A.Auth.Login("admin", "admin");
        foreach (var p in new[] { Permissions.ReportView, Permissions.ReportCustomer, Permissions.ReportFinance, Permissions.ReportCommission, Permissions.ReportAudit })
            Assert.True(admin.Can(p), $"admin {p}");

        var cashierRole = A.Roles.GetAll().Single(r => r.Code == RoleCodes.Cashier).Id;
        A.Users.Create(new AppUser { Username = "kasirx", DisplayName = "Kasir", RoleId = cashierRole }, "kasir-uji-99");
        var cashier = A.Auth.Login("kasirx", "kasir-uji-99");
        Assert.True(cashier.Can(Permissions.ReportView) && cashier.Can(Permissions.ReportCustomer), "kasir: kas, stok, customer");
        Assert.False(cashier.Can(Permissions.ReportFinance) || cashier.Can(Permissions.ReportCommission) || cashier.Can(Permissions.ReportAudit),
            "kasir: no finance/commission/audit");

        // Existing installation: KASIR gets report.customer once; master.customer removed by the admin stays removed.
        var path = Path.Combine(_f.Db.Directory, "old.db");
        LegacySchema.CreateV1(path, withCashierRole: true);
        var upgraded = new Infrastructure.AppServices(path, _f.Db.Clock);
        using var c = upgraded.Db.Open();
        var perms = c.Query<string>("""
            SELECT permission_code FROM role_permission rp JOIN role r ON r.id = rp.role_id WHERE r.code = 'KASIR'
            """).ToHashSet();
        Assert.True(perms.Contains(Permissions.ReportCustomer), "new default granted on upgrade");
        Assert.False(perms.Contains(Permissions.MasterCustomer), "permission removed by admin not restored");
        Assert.False(perms.Contains(Permissions.ReportFinance));
        // Second start: no change.
        _ = new Infrastructure.AppServices(path, _f.Db.Clock);
        Assert.Equal(perms.Count, c.ExecuteScalar<int>("SELECT COUNT(*) FROM role_permission rp JOIN role r ON r.id = rp.role_id WHERE r.code = 'KASIR'"));
    }
}
