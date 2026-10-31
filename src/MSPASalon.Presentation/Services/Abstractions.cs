using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Presentation.ViewModels;

namespace MSPASalon.Presentation.Services;

/// <summary>UI interactions the ViewModels need, implemented by the WPF layer (and by fakes in tests).</summary>
public interface IDialogService
{
    void Info(string message, string title = "POS Salon");
    void Error(string message, string title = "POS Salon");
    bool Confirm(string message, string title = "Konfirmasi");
    /// <summary>Shows the editor modally. Returns true when the editor saved successfully.</summary>
    bool ShowEditor(EditorViewModel editor);
    string? PickOpenFile(string title, string filter);
    string? PickFolder(string title);
    /// <summary>Save-as dialog. Returns the chosen path, or null when cancelled.</summary>
    string? PickSaveFile(string title, string filter, string defaultFileName);
}

/// <summary>All application services the presentation layer uses (Core contracts only, no infrastructure).</summary>
public sealed record PresentationServices(
    IMasterService<Customer> Customers,
    IMasterService<Supplier> Suppliers,
    IMasterService<Category> Categories,
    IMasterService<Employee> Employees,
    IMasterService<Product> Products,
    IMasterService<Service> Services,
    IMasterService<PaymentMethod> PaymentMethods,
    IMasterService<Role> Roles,
    IUserService Users,
    IAuthService Auth,
    IBranchService Branch,
    IBackupService Backup,
    IDashboardService Dashboard,
    ILegacyImportService LegacyImport,
    IClock Clock,
    IRegistrationService Registrations,
    ITreatmentService Treatments,
    ISaleService Sales,
    IPurchaseService Purchases,
    IAdjustmentService Adjustments,
    IStockCardService StockCards,
    IPrintService Print,
    IThemeService Theme,
    IReportService Reports,
    IReportExporter ReportExporter,
    IAdministrationService Administration);

/// <summary>Light/dark appearance. The choice is per computer (not per user) and survives restarts.</summary>
public interface IThemeService
{
    bool IsDark { get; }
    void SetDark(bool dark);
}

/// <summary>Prints plain-text receipt lines on the thermal printer (WPF implementation in the Desktop project).</summary>
public interface IPrintService
{
    /// <summary>
    /// Shows the printer dialog and prints. <paramref name="logo"/> (optional image bytes) is printed centred above the text.
    /// Returns false when the user cancelled.
    /// </summary>
    bool PrintReceipt(IReadOnlyList<string> lines, int paperWidthMm, string jobName, byte[]? logo = null);

    /// <summary>
    /// Prints a report on A4. With <paramref name="toPdf"/> the "Microsoft Print to PDF" printer is preselected
    /// (decision T4); the user still confirms and chooses the file name. Returns false when cancelled.
    /// </summary>
    bool PrintReport(MSPASalon.Core.Reports.ReportDocument document, bool toPdf);
}
