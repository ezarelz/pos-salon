using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Infrastructure.Database;
using MSPASalon.Tests.Framework;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class SecurityTests : IDisposable
{
    private readonly TestDatabase _db = new();
    public void Dispose() => _db.Dispose();

    private long RoleId(string code) => _db.App.Roles.GetAll().Single(r => r.Code == code).Id;

    [Test("INT-16", "Login: berhasil, password salah, user nonaktif; admin awal wajib ganti password")]
    public void Login()
    {
        var s = _db.App.Auth.Login("ADMIN", DatabaseInitializer.DefaultAdminPassword);   // username case-insensitive
        Assert.True(s.IsAdmin);
        Assert.True(s.User.MustChangePassword);
        Assert.Contains("salah", Assert.Throws<BusinessException>(() => _db.App.Auth.Login("admin", "x")).Message);
        Assert.Contains("salah", Assert.Throws<BusinessException>(() => _db.App.Auth.Login("tidakada", "x")).Message);

        var id = _db.App.Users.Create(new AppUser { Username = "kasir1", DisplayName = "Kasir 1", RoleId = RoleId(RoleCodes.Cashier) }, "kasir123");
        var u = _db.App.Users.Get(id)!;
        u.IsActive = false;
        _db.App.Users.Update(u);
        Assert.Contains("tidak aktif", Assert.Throws<BusinessException>(() => _db.App.Auth.Login("kasir1", "kasir123")).Message);
    }

    [Test("INT-17", "Ganti password: password lama diverifikasi, flag wajib-ganti dihapus")]
    public void ChangePassword()
    {
        var s = _db.App.Auth.Login("admin", "admin");
        Assert.Contains("lama salah", Assert.Throws<BusinessException>(() => _db.App.Auth.ChangePassword(s.User.Id, "salah", "barubaru")).Message);
        Assert.Throws<BusinessException>(() => _db.App.Auth.ChangePassword(s.User.Id, "admin", "123"));        // too short
        _db.App.Auth.ChangePassword(s.User.Id, "admin", "rahasia-baru");
        var s2 = _db.App.Auth.Login("admin", "rahasia-baru");
        Assert.False(s2.User.MustChangePassword);
        Assert.Throws<BusinessException>(() => _db.App.Auth.Login("admin", "admin"));
    }

    [Test("INT-18", "Role: KASIR terbatas; role baru bisa ditambah dengan hak akses pilihan (A4)")]
    public void RolesAndPermissions()
    {
        var cashier = _db.App.Users.Create(new AppUser { Username = "kasir1", DisplayName = "Kasir", RoleId = RoleId(RoleCodes.Cashier) }, "kasir123");
        var s = _db.App.Auth.Login("kasir1", "kasir123");
        Assert.True(s.Can(Permissions.TransTreatment));
        Assert.True(s.Can(Permissions.MasterCustomer));
        Assert.False(s.Can(Permissions.MasterProduct));
        Assert.False(s.Can(Permissions.MasterUser));

        var role = new Role { Code = "gudang", Name = "Staf Gudang" };
        role.Permissions.Add(Permissions.MasterProduct);
        role.Permissions.Add(Permissions.TransPurchase);
        var roleId = _db.App.Roles.Save(role);
        Assert.Equal("GUDANG", _db.App.Roles.Get(roleId)!.Code);

        var u = _db.App.Users.Get(cashier)!;
        u.RoleId = roleId;
        _db.App.Users.Update(u);
        var s2 = _db.App.Auth.Refresh(s);
        Assert.True(s2.Can(Permissions.MasterProduct));
        Assert.True(s2.Can(Permissions.TransPurchase));
        Assert.False(s2.Can(Permissions.TransTreatment));

        Assert.Contains("masih dipakai", Assert.Throws<BusinessException>(() => _db.App.Roles.Delete(roleId)).Message);
        Assert.Contains("bawaan", Assert.Throws<BusinessException>(() => _db.App.Roles.Delete(RoleId(RoleCodes.Cashier))).Message);
        Assert.Throws<BusinessException>(() => _db.App.Roles.Save(new Role { Code = "KOSONG", Name = "Tanpa Hak" }));   // no permission
    }

    [Test("INT-19", "User: admin aktif terakhir tidak bisa dinonaktifkan/dihapus; tidak bisa hapus diri sendiri")]
    public void LastAdminProtected()
    {
        var admin = _db.App.Users.GetAll().Single();
        admin.IsActive = false;
        Assert.Contains("minimal satu user Administrator", Assert.Throws<BusinessException>(() => _db.App.Users.Update(admin)).Message);
        admin.IsActive = true;
        admin.RoleId = RoleId(RoleCodes.Cashier);
        Assert.Throws<BusinessException>(() => _db.App.Users.Update(admin));

        var other = _db.App.Users.Create(new AppUser { Username = "kasir1", DisplayName = "Kasir", RoleId = RoleId(RoleCodes.Cashier) }, "kasir123");
        Assert.Contains("sedang login", Assert.Throws<BusinessException>(() => _db.App.Users.Delete(admin.Id, admin.Id)).Message);
        Assert.Throws<BusinessException>(() => _db.App.Users.Delete(admin.Id, other));
        _db.App.Users.Delete(other, admin.Id);
        Assert.Equal(1, _db.App.Users.GetAll().Count);
        Assert.Throws<BusinessException>(() =>
            _db.App.Users.Create(new AppUser { Username = "Admin", DisplayName = "Dup", RoleId = RoleId(RoleCodes.Admin) }, "abcdef"));
    }
}
