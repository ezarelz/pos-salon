namespace MSPASalon.Core.Domain;

/// <summary>
/// Permission catalogue. Roles are data (table <c>role</c> + <c>role_permission</c>), so new roles can be
/// added later from the Role master without code changes. Only the permission codes themselves are code.
/// </summary>
public static class Permissions
{
    public const string MasterCustomer = "master.customer";
    public const string MasterProduct = "master.product";
    public const string MasterService = "master.service";
    public const string MasterEmployee = "master.employee";
    public const string MasterSupplier = "master.supplier";
    public const string MasterCategory = "master.category";
    public const string MasterPaymentMethod = "master.paymentmethod";
    public const string MasterUser = "master.user";
    public const string MasterRole = "master.role";
    public const string SettingsBranch = "settings.branch";

    public const string TransRegistration = "trans.registration";
    public const string TransTreatment = "trans.treatment";
    public const string TransSale = "trans.sale";
    public const string TransPurchase = "trans.purchase";
    public const string TransAdjustment = "trans.adjustment";
    public const string TransCancelPosted = "trans.cancelposted";

    public const string ReportView = "report.view";
    public const string AdminBackup = "admin.backup";
    public const string AdminImport = "admin.import";

    public sealed record Definition(string Code, string Name, string Module);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(MasterCustomer, "Master Customer", "Master"),
        new(MasterProduct, "Master Produk", "Master"),
        new(MasterService, "Master Jasa & Resep Bahan", "Master"),
        new(MasterEmployee, "Master Karyawan", "Master"),
        new(MasterSupplier, "Master Supplier", "Master"),
        new(MasterCategory, "Master Kategori", "Master"),
        new(MasterPaymentMethod, "Master Metode Bayar", "Master"),
        new(MasterUser, "Master User", "Master"),
        new(MasterRole, "Master Role & Hak Akses", "Master"),
        new(SettingsBranch, "Pengaturan Salon", "Pengaturan"),
        new(TransRegistration, "Registrasi Customer", "Transaksi"),
        new(TransTreatment, "Proses Treatment & Pembayaran", "Transaksi"),
        new(TransSale, "Penjualan Produk", "Transaksi"),
        new(TransPurchase, "Pembelian", "Transaksi"),
        new(TransAdjustment, "Stock Adjustment", "Transaksi"),
        new(TransCancelPosted, "Batalkan Dokumen yang Sudah Diposting", "Transaksi"),
        new(ReportView, "Lihat Laporan", "Laporan"),
        new(AdminBackup, "Backup Database", "Administrasi"),
        new(AdminImport, "Import Data Aplikasi Lama", "Administrasi"),
    ];

    /// <summary>Default permissions of the built-in KASIR role.</summary>
    public static readonly IReadOnlyList<string> CashierDefaults =
        [MasterCustomer, TransRegistration, TransTreatment, TransSale, ReportView];
}

/// <summary>Built-in role codes. ADMIN always has every permission (also permissions added in later versions).</summary>
public static class RoleCodes
{
    public const string Admin = "ADMIN";
    public const string Cashier = "KASIR";
}
