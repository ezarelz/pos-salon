using MSPASalon.Core.Entities;

namespace MSPASalon.Core.Interfaces;

/// <summary>Time source; replaced by a fixed clock in tests.</summary>
public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

/// <summary>
/// CRUD for one master entity. <see cref="Save"/> validates (field rules + uniqueness) and throws
/// <see cref="Domain.BusinessException"/> with user-facing messages; it returns the record id.
/// </summary>
public interface IMasterService<T> where T : class, IMasterEntity
{
    IReadOnlyList<T> GetAll();
    T? Get(long id);
    long Save(T entity);
    /// <summary>Deletes the record (K10: deleting masters is allowed). Returns a warning to show, or null.</summary>
    string? Delete(long id);
    /// <summary>Text to show in the delete confirmation (e.g. remaining stock warning), or null.</summary>
    string? DeleteWarning(long id);
    string SuggestNextCode();
}

public interface IUserService
{
    IReadOnlyList<AppUser> GetAll();
    AppUser? Get(long id);
    long Create(AppUser user, string password);
    void Update(AppUser user);
    void ResetPassword(long userId, string newPassword);
    void Delete(long userId, long currentUserId);
}

public interface IAuthService
{
    /// <summary>Returns the session, or throws BusinessException("Username atau password salah.").</summary>
    UserSession Login(string username, string password);
    void ChangePassword(long userId, string currentPassword, string newPassword);
    /// <summary>Re-reads role permissions (after the role master changed).</summary>
    UserSession Refresh(UserSession session);
}

public interface IBranchService
{
    Branch Get();
    void Save(Branch branch);
    /// <summary>Receipt logo bytes, or null when no logo is set.</summary>
    byte[]? GetLogo();
    /// <summary>Sets (PNG/JPEG/BMP, max 1 MB) or removes (null) the receipt logo.</summary>
    void SaveLogo(byte[]? image);
}

public interface IBackupService
{
    /// <summary>Creates a consistent online backup and returns the full path of the backup file.</summary>
    string Backup(string targetDirectory);
    string DatabasePath { get; }
}

/// <summary>One posted document of the day (treatment paid or product sale), newest first on the dashboard.</summary>
public sealed record DashboardDocument(DateTime At, string DocNo, string Kind, string CustomerName, decimal Total, string MethodName);

/// <summary>Payments of the day per method, for matching the cash drawer and QRIS/transfer statements (§9 Y5).</summary>
public sealed record DashboardMethodTotal(string MethodName, bool IsCash, int Count, decimal Amount);

/// <summary>Work of one capster on the day. Amount = Σ service prices (before the treatment's header discount).</summary>
public sealed record DashboardCapster(string Name, int Clients, int Services, decimal Amount, decimal Commission = 0);

/// <summary>
/// Daily operational summary. Only posted documents count: treatments LUNAS and sales POSTED, by posting time.
/// Cancelled documents are excluded (their payments are removed on cancel, K11).
/// </summary>
public sealed record DashboardSummary(
    DateTime Date,
    decimal ServiceRevenue, decimal ProductRevenue, decimal PreviousDayRevenue,
    int ClientsServed, int SalesCount, int Waiting, int InProcess, int CancelledCount,
    IReadOnlyList<DashboardMethodTotal> Methods,
    IReadOnlyList<DashboardDocument> Recent,
    IReadOnlyList<DashboardCapster> Capsters,
    IReadOnlyList<Product> LowStock)
{
    public decimal TotalRevenue => ServiceRevenue + ProductRevenue;
    public int TransactionCount => ClientsServed + SalesCount;
    public decimal AveragePerClient => ClientsServed == 0 ? 0 : Math.Round(ServiceRevenue / ClientsServed, 0, MidpointRounding.AwayFromZero);
}

public interface IDashboardService
{
    /// <summary>Summary of <paramref name="day"/> (time of day ignored). Queue counts are always the current state.</summary>
    DashboardSummary GetSummary(DateTime day);
}

public sealed class ImportResult
{
    public Dictionary<string, int> Counts { get; } = new();
    public List<string> Warnings { get; } = [];
    public List<string> Notes { get; } = [];
}

public interface ILegacyImportService
{
    /// <summary>
    /// Imports master data from the legacy salon.sqlite (opened read-only) in ONE transaction.
    /// Throws BusinessException if the target already contains master data.
    /// </summary>
    ImportResult Import(string legacyDatabasePath);
    bool TargetHasMasterData();
}

/// <summary>Read-only report queries (BUSINESS_RULES §14). Only posted documents; dates by posting time (L1, L2).</summary>
public interface IReportService
{
    MSPASalon.Core.Reports.CashDailyReport GetCashDaily(DateTime day);
    MSPASalon.Core.Reports.RevenueReport GetRevenue(DateTime from, DateTime to);
    /// <param name="employeeId">null = all capsters.</param>
    MSPASalon.Core.Reports.CommissionReport GetCommission(DateTime from, DateTime to, long? employeeId);
}

/// <summary>Writes a report to an Excel workbook (.xlsx).</summary>
public interface IReportExporter
{
    void ToXlsx(MSPASalon.Core.Reports.ReportDocument document, string path);
}
