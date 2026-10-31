using System.IO;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Reports;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Presentation.ViewModels.Reports;

/// <summary>One displayed table row; <see cref="IsTotal"/> rows are shown bold. Indexer allows binding "[0]", "[1]", ...</summary>
public sealed class ReportRow(object?[] cells, bool isTotal)
{
    public object?[] Cells { get; } = cells;
    public bool IsTotal { get; } = isTotal;
    public object? this[int index] => index < Cells.Length ? Cells[index] : null;
}

/// <summary>A report section prepared for the screen (one tab with a grid).</summary>
public sealed class ReportGridSection
{
    public ReportGridSection(ReportSection s)
    {
        Title = s.Title;
        Columns = s.Columns;
        var rows = s.Rows.Select(r => new ReportRow(r, false)).ToList();
        if (s.Totals is { } t && rows.Count > 0) rows.Add(new ReportRow(t, true));
        Rows = rows;
        EmptyText = s.EmptyText;
    }

    public string Title { get; }
    public IReadOnlyList<ReportColumn> Columns { get; }
    public IReadOnlyList<ReportRow> Rows { get; }
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyText { get; }
}

/// <summary>
/// Report page frame (decision T2): period filter with quick choices, optional extra filter, summary cards,
/// one tab per section, and Print / PDF / Excel actions. Subclasses only build the <see cref="ReportDocument"/>.
/// </summary>
public abstract class ReportPageViewModel : PageViewModel
{
    protected readonly PresentationServices S;
    protected readonly IDialogService Dialogs;
    protected readonly UserSession Session;
    private PeriodPreset _preset;
    private DateTime? _from, _to;
    private long _filterValue;
    private bool _suspend = true;
    private ReportDocument? _document;
    private string? _errorText, _statusText;
    private ReportGridSection? _selectedSection;

    protected ReportPageViewModel(PresentationServices s, IDialogService dialogs, UserSession session,
        IReadOnlyList<PeriodPreset> presets, PeriodPreset defaultPreset)
    {
        S = s;
        Dialogs = dialogs;
        Session = session;
        Presets = presets.Select(p => new Option<PeriodPreset>(p, ReportPeriods.Label(p))).ToList();
        RefreshCommand = new RelayCommand(Refresh);
        PrintCommand = new RelayCommand(() => Print(toPdf: false), () => Document is not null);
        PdfCommand = new RelayCommand(() => Print(toPdf: true), () => Document is not null);
        ExcelCommand = new RelayCommand(ExportExcel, () => Document is not null);
        SelectedPreset = defaultPreset;
    }

    public IReadOnlyList<Option<PeriodPreset>> Presets { get; }
    public virtual bool IsSingleDay => false;
    public bool IsRange => !IsSingleDay;

    public PeriodPreset SelectedPreset
    {
        get => _preset;
        set
        {
            if (!SetProperty(ref _preset, value) || value == PeriodPreset.Custom) return;
            var (from, to) = ReportPeriods.Range(value, S.Clock.Now);
            var wasSuspended = _suspend;
            _suspend = true;
            DateFrom = from;
            DateTo = IsSingleDay ? from : to;
            _suspend = wasSuspended;
            if (!_suspend) Refresh();
        }
    }

    public DateTime? DateFrom
    {
        get => _from;
        set
        {
            if (!SetProperty(ref _from, value?.Date)) return;
            if (IsSingleDay) { _to = _from; OnPropertyChanged(nameof(DateTo)); }
            OnDateEdited();
        }
    }

    public DateTime? DateTo
    {
        get => _to;
        set { if (SetProperty(ref _to, value?.Date)) OnDateEdited(); }
    }

    private void OnDateEdited()
    {
        if (_suspend) return;
        _preset = PeriodPreset.Custom;           // a hand-picked date turns the quick choice into "Pilih tanggal"
        OnPropertyChanged(nameof(SelectedPreset));
        Refresh();
    }

    /// <summary>Optional extra filter (e.g. capster). Value 0 = all.</summary>
    public virtual string? FilterLabel => null;
    public bool HasFilter => FilterLabel is not null;
    public IReadOnlyList<Option<long>> FilterOptions { get; protected set; } = [];
    public long FilterValue
    {
        get => _filterValue;
        set { if (SetProperty(ref _filterValue, value) && !_suspend) Refresh(); }
    }

    public ReportDocument? Document
    {
        get => _document;
        private set
        {
            if (!SetProperty(ref _document, value)) return;
            Sections = value?.Sections.Select(x => new ReportGridSection(x)).ToList() ?? [];
            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(PeriodText));
            SelectedSection = Sections.FirstOrDefault();
            PrintCommand.NotifyCanExecuteChanged();
            PdfCommand.NotifyCanExecuteChanged();
            ExcelCommand.NotifyCanExecuteChanged();
            OnDocumentChanged();
        }
    }

    public IReadOnlyList<ReportGridSection> Sections { get; private set; } = [];
    public ReportGridSection? SelectedSection { get => _selectedSection; set => SetProperty(ref _selectedSection, value); }
    public IReadOnlyList<ReportSummaryItem> Summary => Document?.Summary ?? [];
    public IReadOnlyList<string> Notes => Document?.Notes ?? [];
    public string PeriodText => Document?.PeriodText ?? "";

    public string? ErrorText
    {
        get => _errorText;
        private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public string? StatusText { get => _statusText; protected set => SetProperty(ref _statusText, value); }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand PrintCommand { get; }
    public RelayCommand PdfCommand { get; }
    public RelayCommand ExcelCommand { get; }

    /// <summary>Report-specific extra print action (e.g. thermal cash recap). Null = not shown.</summary>
    public virtual string? ExtraPrintLabel => null;
    public bool HasExtraPrint => ExtraPrintLabel is not null;
    public RelayCommand? ExtraPrintCommand { get; protected init; }

    protected string SalonName => S.Branch.Get().Name;
    protected string UserName => Session.User.DisplayName;

    protected abstract ReportDocument BuildDocument(DateTime from, DateTime to);
    protected virtual void OnDocumentChanged() { }

    /// <summary>Call at the end of the subclass constructor: enables automatic refresh and loads the first result.</summary>
    protected void Activate()
    {
        _suspend = false;
        Refresh();
    }

    public override void Refresh()
    {
        if (_suspend) return;
        StatusText = null;
        if (DateFrom is not { } from || DateTo is not { } to)
        {
            ErrorText = "Pilih tanggal laporan.";
            return;
        }
        if (ReportPeriods.Validate(from, to) is { } error)
        {
            ErrorText = error;
            return;
        }
        try
        {
            Document = BuildDocument(from, to);
            ErrorText = null;
        }
        catch (BusinessException ex)
        {
            ErrorText = ex.Message;
        }
    }

    private void Print(bool toPdf)
    {
        if (Document is null) return;
        try
        {
            if (S.Print.PrintReport(Document, toPdf))
                StatusText = toPdf ? "Dokumen dikirim ke printer PDF." : "Laporan dikirim ke printer.";
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            Dialogs.Error($"Gagal mencetak: {ex.Message}");
        }
    }

    private void ExportExcel()
    {
        if (Document is null) return;
        var path = Dialogs.PickSaveFile("Simpan sebagai Excel", "Excel Workbook (*.xlsx)|*.xlsx", Document.FileName + ".xlsx");
        if (path is null) return;
        try
        {
            S.ReportExporter.ToXlsx(Document, path);
            StatusText = $"Excel tersimpan: {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error($"File tidak bisa disimpan. Bila file ini sedang terbuka di Excel, tutup dulu lalu ulangi.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }
}

/// <summary>Laporan 1 — Rekap Kas Harian (§14): one day; extra action prints the recap on the thermal printer.</summary>
public sealed class CashDailyReportViewModel : ReportPageViewModel
{
    private CashDailyReport? _report;

    public CashDailyReportViewModel(PresentationServices s, IDialogService dialogs, UserSession session)
        : base(s, dialogs, session, [PeriodPreset.Today, PeriodPreset.Yesterday, PeriodPreset.Custom], PeriodPreset.Today)
    {
        ExtraPrintCommand = new RelayCommand(PrintReceipt, () => _report is not null);
        Activate();
    }

    public override string Title => "Rekap Kas Harian";
    public override string Key => "report:cash";
    public override bool IsSingleDay => true;
    public override string? ExtraPrintLabel => "Cetak Struk Rekap";

    protected override ReportDocument BuildDocument(DateTime from, DateTime to)
    {
        _report = S.Reports.GetCashDaily(from);
        return ReportBuilders.CashDaily(_report, SalonName, UserName, S.Clock.Now);
    }

    protected override void OnDocumentChanged() => ExtraPrintCommand?.NotifyCanExecuteChanged();

    private void PrintReceipt()
    {
        if (_report is null) return;
        var branch = S.Branch.Get();
        var lines = ReportBuilders.CashDailyReceipt(_report, branch, UserName, S.Clock.Now, ReceiptBuilder.CharsPerLine(branch.PaperWidthMm));
        try
        {
            if (S.Print.PrintReceipt(lines, branch.PaperWidthMm, $"Rekap Kas {_report.Day:dd-MM-yyyy}"))
                StatusText = "Struk rekap kas dikirim ke printer.";
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            Dialogs.Error($"Gagal mencetak: {ex.Message}");
        }
    }
}

/// <summary>Laporan 2 — Pendapatan (§14).</summary>
public sealed class RevenueReportViewModel : ReportPageViewModel
{
    public RevenueReportViewModel(PresentationServices s, IDialogService dialogs, UserSession session)
        : base(s, dialogs, session, ReportPeriods.General, PeriodPreset.ThisMonth) => Activate();

    public override string Title => "Laporan Pendapatan";
    public override string Key => "report:revenue";

    protected override ReportDocument BuildDocument(DateTime from, DateTime to) =>
        ReportBuilders.Revenue(S.Reports.GetRevenue(from, to), SalonName, UserName, S.Clock.Now);
}

/// <summary>Laporan 3 — Komisi Capster (§14, K20, K25). Choosing one capster turns the print into a signed slip.</summary>
public sealed class CommissionReportViewModel : ReportPageViewModel
{
    public CommissionReportViewModel(PresentationServices s, IDialogService dialogs, UserSession session)
        : base(s, dialogs, session, ReportPeriods.Commission,
            s.Clock.Now.Day <= 15 ? PeriodPreset.FirstHalfThisMonth : PeriodPreset.SecondHalfThisMonth)
    {
        FilterOptions =
        [
            new Option<long>(0, "Semua capster"),
            .. s.Employees.GetAll().Where(e => e.IsCapster).OrderBy(e => e.Name)
                .Select(e => new Option<long>(e.Id, e.IsActive ? e.Name : $"{e.Name} (nonaktif)")),
        ];
        Activate();
    }

    public override string Title => "Komisi Capster";
    public override string Key => "report:commission";
    public override string? FilterLabel => "Capster";

    protected override ReportDocument BuildDocument(DateTime from, DateTime to) =>
        ReportBuilders.Commission(S.Reports.GetCommission(from, to, FilterValue == 0 ? null : FilterValue), SalonName, UserName, S.Clock.Now);
}
