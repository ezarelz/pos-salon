using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Presentation.ViewModels;

public sealed record CountRow(string Label, string Value);

/// <summary>
/// Pengaturan → Reset Data Transaksi (BUSINESS_RULES §16, R1–R5). ADMIN role only. The run button stays disabled
/// until the password is entered and "RESET" is typed; a final confirmation dialog repeats what will happen.
/// </summary>
public sealed class ResetDataPageViewModel : PageViewModel
{
    private readonly PresentationServices _s;
    private readonly IDialogService _dialogs;
    private readonly UserSession _session;
    private ResetPreview? _preview;
    private bool _keepStock;
    private string _password = "", _confirmText = "", _backupDirectory;
    private string? _errorText, _resultText;

    public ResetDataPageViewModel(PresentationServices s, IDialogService dialogs, UserSession session)
    {
        _s = s; _dialogs = dialogs; _session = session;
        _backupDirectory = s.Administration.DefaultBackupDirectory;
        ResetCommand = new RelayCommand(Execute, () => CanExecute);
        ChangeFolderCommand = new RelayCommand(() =>
        {
            if (_dialogs.PickFolder("Folder backup otomatis sebelum reset") is { } f) BackupDirectory = f;
        });
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public override string Title => "Reset Data Transaksi";
    public override string Key => "admin:reset";
    public string ConfirmWord => AdministrationRules.ResetConfirmWord;

    public ResetPreview? Preview { get => _preview; private set { if (SetProperty(ref _preview, value)) RaisePreview(); } }

    public IReadOnlyList<CountRow> CountRows => Preview is not { Counts: var c } ? [] :
    [
        new("Registrasi", c.Registrations.ToString()), new("Treatment", c.Treatments.ToString()),
        new("Penjualan produk", c.Sales.ToString()), new("Pembelian", c.Purchases.ToString()),
        new("Stock adjustment", c.Adjustments.ToString()), new("Pembayaran", c.Payments.ToString()),
        new("Mutasi stok (ledger)", c.LedgerRows.ToString()),
    ];

    public string PeriodText => Preview switch
    {
        { Oldest: { } o, Newest: { } n } => $"Transaksi tertua {o:dd-MM-yyyy}, terbaru {n:dd-MM-yyyy}.",
        _ => "Belum ada transaksi.",
    };

    /// <summary>R4: strong warning, not a block.</summary>
    public bool IsOperationalWarning => Preview?.LooksOperational == true;
    public string OperationalWarning =>
        $"PERHATIAN: ada transaksi yang lebih tua dari {AdministrationRules.OperationalDataDays} hari. Data ini tampaknya sudah data operasional, " +
        "bukan uji coba. Transaksi adalah catatan keuangan; pastikan Anda memang ingin menghapusnya.";

    public string StockText => Preview is null ? "" :
        $"{Preview.ProductsWithStock} produk punya stok saat ini" + (Preview.ProductsWithNegativeStock > 0 ? $" ({Preview.ProductsWithNegativeStock} minus)." : ".");

    /// <summary>R1: false = all stock 0; true = current stock becomes the opening balance.</summary>
    public bool KeepStock { get => _keepStock; set { if (SetProperty(ref _keepStock, value)) OnPropertyChanged(nameof(ZeroStock)); } }
    public bool ZeroStock { get => !_keepStock; set => KeepStock = !value; }

    public string BackupDirectory { get => _backupDirectory; set => SetProperty(ref _backupDirectory, value); }
    public string Password { get => _password; set { if (SetProperty(ref _password, value)) ResetCommand.NotifyCanExecuteChanged(); } }
    public string ConfirmText { get => _confirmText; set { if (SetProperty(ref _confirmText, value)) ResetCommand.NotifyCanExecuteChanged(); } }
    public bool CanExecute => _password.Length > 0 && _confirmText.Trim() == AdministrationRules.ResetConfirmWord;

    public string? ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public string? ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    public RelayCommand ResetCommand { get; }
    public RelayCommand ChangeFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public override void Refresh() => Preview = _s.Administration.PreviewReset();

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(CountRows));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(IsOperationalWarning));
        OnPropertyChanged(nameof(StockText));
    }

    private void Execute()
    {
        ErrorText = null;
        var c = Preview?.Counts;
        var msg = $"Hapus SEMUA data transaksi?{Environment.NewLine}{Environment.NewLine}" +
                  $"{c?.Documents ?? 0} dokumen dan {c?.LedgerRows ?? 0} mutasi stok akan dihapus; nomor nota kembali ke 0001.{Environment.NewLine}" +
                  $"Stok: {(KeepStock ? "stok saat ini menjadi saldo awal" : "semua menjadi 0")}.{Environment.NewLine}" +
                  $"Master, user, pengaturan, dan customer TIDAK dihapus.{Environment.NewLine}" +
                  $"Backup otomatis disimpan di:{Environment.NewLine}{BackupDirectory}";
        if (IsOperationalWarning) msg = OperationalWarning + Environment.NewLine + Environment.NewLine + msg;
        if (!_dialogs.Confirm(msg, "Reset Data Transaksi")) return;
        try
        {
            var r = _s.Administration.ResetTransactions(_session, Password, ConfirmText,
                KeepStock ? StockResetMode.KeepAsOpening : StockResetMode.Zero, BackupDirectory);
            ResultText = $"Reset selesai: {r.Deleted.Documents} dokumen dan {r.Deleted.LedgerRows} mutasi stok dihapus" +
                         (r.Mode == StockResetMode.KeepAsOpening ? $"; saldo awal dibuat untuk {r.OpeningRows} produk" : "; semua stok 0") +
                         $".{Environment.NewLine}Backup sebelum reset: {r.BackupPath}" +
                         (r.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, r.Warnings) : "");
            Password = "";
            ConfirmText = "";
            Refresh();
        }
        catch (BusinessException ex)
        {
            ErrorText = ex.Message;
            Password = "";
        }
    }
}

/// <summary>
/// Pengaturan → Restore Backup (§16). ADMIN only: choose a file → validation summary → password → restore.
/// The current database is backed up first; afterwards the user logs in again (users/passwords may differ).
/// </summary>
public sealed class RestorePageViewModel : PageViewModel
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");
    private readonly PresentationServices _s;
    private readonly IDialogService _dialogs;
    private readonly UserSession _session;
    private readonly Action _restored;
    private BackupInfo? _info;
    private string _password = "";
    private string? _errorText;

    public RestorePageViewModel(PresentationServices s, IDialogService dialogs, UserSession session, Action restored)
    {
        _s = s; _dialogs = dialogs; _session = session; _restored = restored;
        ChooseFileCommand = new RelayCommand(Choose);
        RestoreCommand = new RelayCommand(Execute, () => Info is not null && _password.Length > 0);
    }

    public override string Title => "Restore Backup";
    public override string Key => "admin:restore";
    public string SafetyFolder => _s.Administration.DefaultBackupDirectory;

    public BackupInfo? Info
    {
        get => _info;
        private set
        {
            if (!SetProperty(ref _info, value)) return;
            OnPropertyChanged(nameof(HasInfo));
            OnPropertyChanged(nameof(InfoRows));
            RestoreCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasInfo => Info is not null;

    public IReadOnlyList<CountRow> InfoRows => Info is not { } i ? [] :
    [
        new("File", i.Path),
        new("Ukuran", $"{(i.SizeBytes / 1024m).ToString("#,0", Id)} KB"),
        new("Nama salon", i.SalonName),
        new("Versi skema", $"v{i.SchemaVersion}"),
        new("Transaksi terakhir", i.LastTransaction?.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-"),
        new("Treatment / Penjualan", $"{i.Counts.Treatments} / {i.Counts.Sales}"),
        new("Pembelian / Adjustment", $"{i.Counts.Purchases} / {i.Counts.Adjustments}"),
    ];

    public string Password { get => _password; set { if (SetProperty(ref _password, value)) RestoreCommand.NotifyCanExecuteChanged(); } }
    public string? ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand RestoreCommand { get; }

    private void Choose()
    {
        var file = _dialogs.PickOpenFile("Pilih file backup database", "Backup database (*.db)|*.db|Semua file (*.*)|*.*");
        if (file is null) return;
        try
        {
            Info = _s.Administration.InspectBackup(file);
            ErrorText = null;
        }
        catch (BusinessException ex)
        {
            Info = null;
            ErrorText = ex.Message;
        }
    }

    private void Execute()
    {
        if (Info is not { } info) return;
        ErrorText = null;
        if (!_dialogs.Confirm(
                $"Ganti SELURUH isi database saat ini dengan backup ini?{Environment.NewLine}{Environment.NewLine}{info.Path}{Environment.NewLine}" +
                $"Transaksi terakhir di backup: {info.LastTransaction?.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-"}.{Environment.NewLine}" +
                $"Transaksi setelah tanggal itu akan hilang dari database aktif.{Environment.NewLine}{Environment.NewLine}" +
                $"Database saat ini dibackup dulu ke:{Environment.NewLine}{SafetyFolder}", "Restore Backup"))
            return;
        try
        {
            var r = _s.Administration.Restore(_session, Password, info.Path, SafetyFolder);
            Password = "";
            _dialogs.Info($"Restore selesai.{Environment.NewLine}Database sebelumnya disimpan di:{Environment.NewLine}{r.SafetyBackupPath}" +
                          $"{Environment.NewLine}{Environment.NewLine}Silakan login ulang (user & password mengikuti data backup).", "Restore Backup");
            _restored();
        }
        catch (BusinessException ex)
        {
            ErrorText = ex.Message;
            Password = "";
        }
    }
}
