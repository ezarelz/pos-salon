using System.IO;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Presentation.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private string _username = "", _password = "";
    private string? _errorText;

    public LoginViewModel(IAuthService auth, string salonName)
    {
        _auth = auth;
        SalonName = salonName;
        LoginCommand = new RelayCommand(Login);
    }

    public string SalonName { get; }
    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string? ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_errorText);
    public RelayCommand LoginCommand { get; }
    public UserSession? Session { get; private set; }
    public event EventHandler<UserSession>? LoggedIn;

    private void Login()
    {
        ErrorText = null;
        if (string.IsNullOrWhiteSpace(Username)) { ErrorText = "Username wajib diisi."; return; }
        try
        {
            Session = _auth.Login(Username, Password);
            Password = "";
            LoggedIn?.Invoke(this, Session);
        }
        catch (BusinessException ex)
        {
            ErrorText = ex.Message;
            Password = "";
        }
    }
}

public sealed record DashboardRecentRow(string TimeText, string DocNo, string Kind, string CustomerName, string TotalText, string MethodName);
public sealed record DashboardMethodRow(string Name, string CountText, string AmountText);
public sealed record DashboardCapsterRow(string Name, string WorkText, string AmountText);

/// <summary>
/// Today at a glance for the cashier/owner: revenue (services + products), clients finished, queue,
/// payment recap per method (cash drawer / QRIS matching), latest transactions, capster work and low stock.
/// </summary>
public sealed class DashboardViewModel : PageViewModel
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");
    private readonly IDashboardService _dashboard;
    private readonly IClock _clock;
    private readonly Action<NavItem>? _navigate;
    private DashboardSummary? _summary;
    private readonly bool _showCommission;

    /// <param name="showCommission">Only users with report.commission see the capsters' commission (§15).</param>
    public DashboardViewModel(IDashboardService dashboard, IClock clock, string salonName, string userName,
        IReadOnlyList<NavItem>? quickLinks = null, Action<NavItem>? navigate = null, bool showCommission = false)
    {
        _showCommission = showCommission;
        _dashboard = dashboard;
        _clock = clock;
        _navigate = navigate;
        SalonName = salonName;
        Greeting = $"Selamat datang, {userName}";
        QuickLinks = quickLinks ?? [];
        RefreshCommand = new RelayCommand(Refresh);
        OpenCommand = new RelayCommand<NavItem>(i => { if (i is not null) _navigate?.Invoke(i); });
    }

    public override string Title => "Dashboard";
    public override string Key => "dashboard";
    public string Greeting { get; }
    public string SalonName { get; }
    public IReadOnlyList<NavItem> QuickLinks { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand<NavItem> OpenCommand { get; }

    public DashboardSummary? Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public string DateText => Summary is null ? "" : Summary.Date.ToString("dddd, d MMMM yyyy", Id);
    public string UpdatedText { get; private set; } = "";

    // KPI: revenue
    public string RevenueText => Rp(Summary?.TotalRevenue ?? 0);
    public string RevenueDetailText => Summary is null ? "" : $"Jasa {Rp(Summary.ServiceRevenue)} · Produk {Rp(Summary.ProductRevenue)}";
    public string PreviousDayText
    {
        get
        {
            if (Summary is null) return "";
            var prev = Summary.PreviousDayRevenue;
            if (prev == 0) return Summary.TotalRevenue == 0 ? "Kemarin: belum ada transaksi" : "Kemarin: Rp 0";
            var pct = Math.Round((Summary.TotalRevenue - prev) / prev * 100, 0, MidpointRounding.AwayFromZero);
            var sign = pct > 0 ? "▲ " : pct < 0 ? "▼ " : "";
            return $"Kemarin {Rp(prev)} ({sign}{Math.Abs(pct)}%)";
        }
    }

    // KPI: clients finished
    public string ClientsText => (Summary?.ClientsServed ?? 0).ToString(Id);
    public string ClientsDetailText => Summary is null || Summary.ClientsServed == 0
        ? "Belum ada treatment selesai"
        : $"Rata-rata {Rp(Summary.AveragePerClient)} / client";

    // KPI: queue
    public string QueueText => ((Summary?.Waiting ?? 0) + (Summary?.InProcess ?? 0)).ToString(Id);
    public string QueueDetailText => Summary is null ? "" : $"{Summary.Waiting} menunggu · {Summary.InProcess} dikerjakan";

    // KPI: transactions
    public string TransactionsText => (Summary?.TransactionCount ?? 0).ToString(Id);
    public string TransactionsDetailText => Summary is null ? "" :
        $"{Summary.ClientsServed} treatment · {Summary.SalesCount} penjualan" + (Summary.CancelledCount > 0 ? $" · {Summary.CancelledCount} batal" : "");

    public IReadOnlyList<DashboardMethodRow> Methods => Summary?.Methods
        .Select(m => new DashboardMethodRow(m.MethodName, $"{m.Count} transaksi", Rp(m.Amount))).ToList() ?? [];
    public string MethodsTotalText => Rp(Summary?.Methods.Sum(m => m.Amount) ?? 0);
    public string CashText => Rp(Summary?.Methods.Where(m => m.IsCash).Sum(m => m.Amount) ?? 0);

    public IReadOnlyList<DashboardRecentRow> Recent => Summary?.Recent
        .Select(d => new DashboardRecentRow(d.At.ToString("HH:mm", Id), d.DocNo, d.Kind, d.CustomerName, Rp(d.Total), d.MethodName))
        .ToList() ?? [];

    public IReadOnlyList<DashboardCapsterRow> Capsters => Summary?.Capsters
        .Select(c => new DashboardCapsterRow(c.Name,
            $"{c.Clients} client · {c.Services} jasa" + (_showCommission ? $" · komisi {Rp(c.Commission)}" : ""), Rp(c.Amount))).ToList() ?? [];

    public IReadOnlyList<string> LowStockLines => Summary?.LowStock
        .Select(p => $"{p.Name} ({p.Code}): stok {Quantity.Format(p.StockOnHand)} {p.Unit}, minimum {Quantity.Format(p.StockMin)}")
        .ToList() ?? [];

    public override void Refresh()
    {
        var now = _clock.Now;
        Summary = _dashboard.GetSummary(now);
        UpdatedText = $"Diperbarui {now.ToString("HH:mm", Id)}";
        OnPropertyChanged(string.Empty);   // every derived text depends on Summary
    }

    private static string Rp(decimal amount) => $"Rp {Money.Format(amount)}";
}

public sealed class BranchSettingsViewModel : PageViewModel
{
    private readonly IBranchService _branch;
    private readonly IDialogService _dialogs;
    private string _shortName = "", _name = "";
    private string? _address, _phone, _footer, _status;
    private int _paperWidthMm = 58;
    private byte[]? _logo;
    private string _visitActiveText = "30", _visitInactiveText = "60";
    private bool _showLastVisit = true;

    public BranchSettingsViewModel(IBranchService branch, IDialogService dialogs)
    {
        _branch = branch;
        _dialogs = dialogs;
        SaveCommand = new RelayCommand(Save);
        ChooseLogoCommand = new RelayCommand(ChooseLogo);
        RemoveLogoCommand = new RelayCommand(RemoveLogo, () => HasLogo);
        Refresh();
    }

    public override string Title => "Pengaturan Salon";
    public override string Key => "settings:branch";
    public IReadOnlyList<Option<int>> PaperWidths { get; } = [new(58, "Thermal 58 mm"), new(80, "Thermal 80 mm")];
    public string ShortName { get => _shortName; set => SetProperty(ref _shortName, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? Address { get => _address; set => SetProperty(ref _address, value); }
    public string? Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string? ReceiptFooter { get => _footer; set => SetProperty(ref _footer, value); }
    public int PaperWidthMm { get => _paperWidthMm; set => SetProperty(ref _paperWidthMm, value); }
    /// <summary>K23: customer is "Aktif" below this many days since the last visit.</summary>
    public string VisitActiveText { get => _visitActiveText; set => SetProperty(ref _visitActiveText, value); }
    /// <summary>K23: customer is "Tidak aktif" above this many days.</summary>
    public string VisitInactiveText { get => _visitInactiveText; set => SetProperty(ref _visitInactiveText, value); }
    /// <summary>K24: show "terakhir datang …" on the registration screen.</summary>
    public bool ShowLastVisit { get => _showLastVisit; set => SetProperty(ref _showLastVisit, value); }
    public string? StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ChooseLogoCommand { get; }
    public RelayCommand RemoveLogoCommand { get; }
    public event EventHandler? Saved;

    /// <summary>Receipt logo (printed centred above the salon name). Saved immediately when chosen or removed.</summary>
    public byte[]? Logo
    {
        get => _logo;
        private set
        {
            if (!SetProperty(ref _logo, value)) return;
            OnPropertyChanged(nameof(HasLogo));
            RemoveLogoCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasLogo => Logo is { Length: > 0 };

    private void ChooseLogo()
    {
        var file = _dialogs.PickOpenFile("Pilih logo untuk nota", "Gambar (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp");
        if (file is null) return;
        try
        {
            var bytes = File.ReadAllBytes(file);
            _branch.SaveLogo(bytes);
            Logo = bytes;
            StatusText = "Logo nota tersimpan. Tips: logo hitam-putih dengan latar putih paling jelas di printer thermal.";
        }
        catch (BusinessException ex) { _dialogs.Error(ex.Message); }
        catch (IOException ex) { _dialogs.Error($"File logo tidak bisa dibaca: {ex.Message}"); }
    }

    private void RemoveLogo()
    {
        if (!_dialogs.Confirm("Hapus logo dari nota?")) return;
        _branch.SaveLogo(null);
        Logo = null;
        StatusText = "Logo nota dihapus.";
    }

    public override void Refresh()
    {
        var b = _branch.Get();
        ShortName = b.ShortName; Name = b.Name; Address = b.Address; Phone = b.Phone;
        ReceiptFooter = b.ReceiptFooter; PaperWidthMm = b.PaperWidthMm; StatusText = null;
        Logo = _branch.GetLogo();
        var v = _branch.GetVisitSettings();
        VisitActiveText = v.ActiveDays.ToString(); VisitInactiveText = v.InactiveDays.ToString(); ShowLastVisit = v.ShowLastVisitOnRegistration;
    }

    private void Save()
    {
        try
        {
            if (!int.TryParse(VisitActiveText?.Trim(), out var active) || !int.TryParse(VisitInactiveText?.Trim(), out var inactive))
                throw new BusinessException("Batas hari kunjungan harus berupa angka bulat.");
            _branch.SaveVisitSettings(new MSPASalon.Core.Reports.VisitSettings(active, inactive, ShowLastVisit));
            _branch.Save(new Branch
            {
                ShortName = ShortName, Name = Name, Address = Address, Phone = Phone,
                ReceiptFooter = ReceiptFooter, PaperWidthMm = PaperWidthMm,
            });
            StatusText = "Pengaturan tersimpan.";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
        }
    }
}

/// <summary>Placeholder tab for modules scheduled in later milestones, so the navigation shows the full plan.</summary>
public sealed class ComingSoonViewModel(string key, string title, string milestone, string description) : PageViewModel
{
    public override string Title => title;
    public override string Key => key;
    public string Milestone => milestone;
    public string Description => description;
}
