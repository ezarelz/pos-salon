using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Infrastructure.Database;
using MSPASalon.Infrastructure.Services;

namespace MSPASalon.Infrastructure;

/// <summary>
/// Composition root for the non-UI layers. Creating it initializes/migrates the database.
/// The application is small and single-user, so plain construction is used instead of a DI container.
/// </summary>
public sealed class AppServices
{
    public DbConnectionFactory Db { get; }
    public IClock Clock { get; }

    public IMasterService<Customer> Customers { get; }
    public IMasterService<Supplier> Suppliers { get; }
    public IMasterService<Category> Categories { get; }
    public IMasterService<Employee> Employees { get; }
    public IMasterService<Product> Products { get; }
    public IMasterService<Service> Services { get; }
    public IMasterService<PaymentMethod> PaymentMethods { get; }
    public IMasterService<Role> Roles { get; }
    public IUserService Users { get; }
    public IAuthService Auth { get; }
    public IBranchService Branch { get; }
    public IBackupService Backup { get; }
    public IDashboardService Dashboard { get; }
    public IReportService Reports { get; }
    public IAdministrationService Administration { get; }
    public IReportExporter ReportExporter { get; } = new Export.XlsxWriter();
    public ILegacyImportService LegacyImport { get; }
    public IRegistrationService Registrations { get; }
    public ITreatmentService Treatments { get; }
    public ISaleService Sales { get; }
    public IPurchaseService Purchases { get; }
    public IAdjustmentService Adjustments { get; }
    public IStockCardService StockCards { get; }

    public AppServices(string databasePath, IClock? clock = null)
    {
        Clock = clock ?? new SystemClock();
        Db = new DbConnectionFactory(databasePath);
        new DatabaseInitializer(Db, Clock).Initialize();

        Customers = new CustomerService(Db, Clock);
        Suppliers = new SupplierService(Db, Clock);
        Categories = new CategoryService(Db, Clock);
        Employees = new EmployeeService(Db, Clock);
        Products = new ProductService(Db, Clock);
        Services = new ServiceService(Db, Clock);
        PaymentMethods = new PaymentMethodService(Db, Clock);
        Roles = new RoleService(Db, Clock);
        Users = new UserService(Db, Clock);
        Auth = new AuthService(Db, Clock);
        Branch = new BranchService(Db);
        Backup = new BackupService(Db, Clock);
        Dashboard = new DashboardService(Db);
        Reports = new ReportService(Db);
        Administration = new AdministrationService(Db, Clock, Backup);
        LegacyImport = new LegacyImportService(Db, Clock);
        Registrations = new RegistrationService(Db, Clock);
        Treatments = new TreatmentService(Db, Clock);
        Sales = new SaleService(Db, Clock);
        Purchases = new PurchaseService(Db, Clock);
        Adjustments = new AdjustmentService(Db, Clock);
        StockCards = new StockCardService(Db);
    }

    /// <summary>
    /// Default database location: %LOCALAPPDATA%\MSPASalon\mspasalon.db (per Windows user, writable,
    /// survives re-installs). The legacy app stored its database next to the .exe, which breaks under
    /// Program Files. Override with the MSPASALON_DB environment variable or the --db command-line option.
    /// </summary>
    public static string DefaultDatabasePath()
    {
        var env = Environment.GetEnvironmentVariable("MSPASALON_DB");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "MSPASalon", "mspasalon.db");
    }
}
