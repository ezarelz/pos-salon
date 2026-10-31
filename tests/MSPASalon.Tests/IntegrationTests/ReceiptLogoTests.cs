using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Tests.Framework;
using MSPASalon.Tests.UnitTests;

namespace MSPASalon.Tests.IntegrationTests;

public sealed class ReceiptLogoTests : IDisposable
{
    // Smallest valid PNG (1×1 px) — only the header matters for validation.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private readonly SalonFixture _f = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mspasalon-logo-" + Guid.NewGuid().ToString("N"));
    private Infrastructure.AppServices A => _f.Db.App;

    public void Dispose()
    {
        _f.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Test("INT-LOGO-01", "Logo nota: migrasi 002, simpan/hapus; format & ukuran divalidasi; data salon lain tidak berubah")]
    public void LogoStorage()
    {
        Assert.True(new Infrastructure.Database.DatabaseInitializer(A.Db, A.Clock).CurrentVersion() >= 2, "schema ≥ v2");
        Assert.True(A.Branch.GetLogo() is null, "default tanpa logo");
        var before = A.Branch.Get().Name;
        A.Branch.SaveLogo(Png);
        Assert.Equal(Png.Length, A.Branch.GetLogo()!.Length);
        Assert.Equal(before, A.Branch.Get().Name);
        Assert.Contains("PNG, JPG", Assert.Throws<BusinessException>(() => A.Branch.SaveLogo("bukan gambar"u8.ToArray())).Message);
        var big = new byte[ReceiptLogo.MaxBytes + 1]; Png.CopyTo(big, 0);
        Assert.Contains("maksimal", Assert.Throws<BusinessException>(() => A.Branch.SaveLogo(big)).Message);
        A.Branch.SaveLogo(null);
        Assert.True(A.Branch.GetLogo() is null);
    }

    [Test("VM-14", "Pengaturan Salon: pilih logo → tersimpan & ikut dikirim ke printer bersama struk")]
    public void LogoFromSettingsToPrinter()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "logo.png");
        File.WriteAllBytes(file, Png);
        var dialogs = new FakeDialogs { OpenFile = file, ConfirmAnswer = true };
        var vm = new BranchSettingsViewModel(A.Branch, dialogs);
        Assert.False(vm.RemoveLogoCommand.CanExecute(null));
        vm.ChooseLogoCommand.Execute(null);
        Assert.True(vm.HasLogo);
        Assert.True(vm.RemoveLogoCommand.CanExecute(null));

        var print = new FakePrint();
        var shell = new ShellViewModel(ViewModelTests.Services(A, print), dialogs, A.Auth.Login("admin", "admin"));
        var sale = A.Sales.Post(_f.Customer, [new(_f.Drink, 1, 5000, 0)], null, new PaymentInput(_f.Cash, 5000, null), "admin");
        shell.PrintSale(A.Sales.Get(sale.Id)!);
        Assert.Equal(Png.Length, print.Jobs.Single().Logo!.Length, "logo dikirim ke printer");

        vm.RemoveLogoCommand.Execute(null);
        Assert.False(vm.HasLogo);
        Assert.True(A.Branch.GetLogo() is null);
    }
}
