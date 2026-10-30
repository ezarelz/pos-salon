using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Presentation.ViewModels.Transactions;

/// <summary>
/// Treatment processing screen (BUSINESS_RULES §4): queue of registrations on the left, the selected
/// registration/treatment on the right. MENUNGGU → Mulai; DIKERJAKAN → change services/capsters, preview the
/// materials that will be backflushed, discount, payment, Selesai &amp; Bayar; or Batalkan.
/// </summary>
public sealed class TreatmentPageViewModel : PageViewModel
{
    private readonly PresentationServices _s;
    private readonly IDialogService _dialogs;
    private readonly UserSession _session;
    private readonly Action<Treatment> _printReceipt;
    private Registration? _selected;
    private Treatment? _treatment;
    private string _discountText = "0";
    private bool _linesDirty;
    private bool _loading;
    private List<Service> _services = [];
    private List<Employee> _capsters = [];

    public TreatmentPageViewModel(PresentationServices s, IDialogService dialogs, UserSession session, Action<Treatment> printReceipt)
    {
        _s = s; _dialogs = dialogs; _session = session; _printReceipt = printReceipt;
        Payment = new PaymentViewModel(s.PaymentMethods.GetAll());
        StartCommand = new RelayCommand(Start, () => IsWaiting && CanWork);
        CompleteCommand = new RelayCommand(Complete, () => IsInProcess && CanWork);
        CancelCommand = new RelayCommand(Cancel, () => (IsWaiting || IsInProcess) && CanWork);
        SaveLinesCommand = new RelayCommand(SaveLines, () => IsInProcess && _linesDirty && CanWork);
        AddLineCommand = new RelayCommand(() => { Lines.Add(new ServiceLineEditViewModel(_services, _capsters, LinesChanged)); LinesChanged(); },
            () => IsInProcess && CanWork);
        RemoveLineCommand = new RelayCommand<ServiceLineEditViewModel>(l => { if (l is not null) { Lines.Remove(l); LinesChanged(); } });
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public override string Title => "Proses Treatment";
    public override string Key => "trans:treatment";
    public bool CanWork => _session.Can(Permissions.TransTreatment);

    public ObservableCollection<Registration> Queue { get; } = [];
    public ObservableCollection<ServiceLineEditViewModel> Lines { get; } = [];
    public ObservableCollection<BackflushItem> Backflush { get; } = [];
    public PaymentViewModel Payment { get; }

    public RelayCommand StartCommand { get; }
    public RelayCommand CompleteCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SaveLinesCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ServiceLineEditViewModel> RemoveLineCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public Registration? SelectedRegistration
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) LoadSelection(); }
    }

    public Treatment? Treatment { get => _treatment; private set => SetProperty(ref _treatment, value); }
    public bool HasSelection => _selected is not null;
    public bool IsWaiting => _selected?.Status == RegistrationStatus.Waiting;
    public bool IsInProcess => _selected?.Status == RegistrationStatus.InProcess && _treatment is not null;
    public bool HasBackflush => Backflush.Count > 0;
    public bool HasShortage => Backflush.Any(b => b.IsShort);
    public string QueueText => Queue.Count == 0 ? "Antrian kosong" : $"{Queue.Count(q => q.Status == RegistrationStatus.Waiting)} menunggu · {Queue.Count(q => q.Status == RegistrationStatus.InProcess)} dikerjakan";

    public string HeaderText => _selected is null ? "Pilih registrasi dari antrian" : $"{_selected.RegNo} · {_selected.CustomerName}";

    public string StatusText => _selected is null ? ""
        : IsWaiting ? $"MENUNGGU sejak {_selected.RegAt:HH:mm}"
        : _treatment is not null ? $"DIKERJAKAN · mulai {_treatment.TimeIn:HH:mm} ({(int)(_s.Clock.Now - _treatment.TimeIn).TotalMinutes} menit)"
        : _selected.Status;

    public string? NotesText => _selected?.Notes;

    public string DiscountText
    {
        get => _discountText;
        set { if (SetProperty(ref _discountText, value)) RaiseTotals(); }
    }

    public decimal Subtotal => IsInProcess ? Lines.Sum(l => l.Price) : _selected?.Total ?? 0;
    public decimal Discount => Money.TryParse(_discountText, out var d) ? d : 0;
    public decimal Total => Math.Max(0, Subtotal - Discount);
    public string SubtotalText => Money.Format(Subtotal);
    public string TotalText => Money.Format(Total);
    public bool LinesDirty => _linesDirty;

    public override void Refresh()
    {
        _services = Lookups.ActiveServices(_s);
        _capsters = Lookups.ActiveCapsters(_s);
        var keep = _selected?.Id;
        Queue.Clear();
        foreach (var r in _s.Treatments.GetQueue()) Queue.Add(r);
        OnPropertyChanged(nameof(QueueText));
        _selected = null;
        SelectedRegistration = keep is null ? null : Queue.FirstOrDefault(q => q.Id == keep);
        if (_selected is null) LoadSelection();
    }

    /// <summary>Used by the registration page ("Proses Treatment") to open a specific registration.</summary>
    public void Select(long registrationId)
    {
        Refresh();
        SelectedRegistration = Queue.FirstOrDefault(q => q.Id == registrationId);
    }

    private void LoadSelection()
    {
        _loading = true;
        try
        {
            Lines.Clear();
            Backflush.Clear();
            _linesDirty = false;
            _discountText = "0";
            Payment.ReceivedText = "";
            Payment.Reference = null;
            Treatment = _selected?.Status == RegistrationStatus.InProcess ? _s.Treatments.GetByRegistration(_selected.Id) : null;
            if (Treatment is not null)
                foreach (var l in Treatment.Lines)
                    Lines.Add(new ServiceLineEditViewModel(_services, _capsters, LinesChanged, l.ServiceId, l.EmployeeId, l.Price));
        }
        finally
        {
            _loading = false;
        }
        RefreshBackflush();
        RaiseAll();
    }

    private void LinesChanged()
    {
        if (_loading) return;
        _linesDirty = true;
        RefreshBackflush();
        RaiseTotals();
        SaveLinesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(LinesDirty));
    }

    private void RefreshBackflush()
    {
        Backflush.Clear();
        var ids = (IsInProcess ? Lines.Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value)
                               : _selected?.Lines.Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value) ?? []).ToList();
        if (_selected is not null && !IsInProcess && ids.Count == 0)
            ids = (_s.Registrations.Get(_selected.Id)?.Lines ?? []).Where(l => l.ServiceId.HasValue).Select(l => l.ServiceId!.Value).ToList();
        if (ids.Count > 0)
            foreach (var b in _s.Treatments.PreviewBackflush(ids)) Backflush.Add(b);
        OnPropertyChanged(nameof(HasBackflush));
        OnPropertyChanged(nameof(HasShortage));
    }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(Subtotal)); OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(Discount)); OnPropertyChanged(nameof(Total)); OnPropertyChanged(nameof(TotalText));
        Payment.AmountDue = Total;
    }

    private void RaiseAll()
    {
        foreach (var p in new[] { nameof(HasSelection), nameof(IsWaiting), nameof(IsInProcess), nameof(HeaderText), nameof(StatusText),
                     nameof(NotesText), nameof(DiscountText), nameof(LinesDirty) })
            OnPropertyChanged(p);
        RaiseTotals();
        StartCommand.NotifyCanExecuteChanged(); CompleteCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        SaveLinesCommand.NotifyCanExecuteChanged(); AddLineCommand.NotifyCanExecuteChanged();
    }

    private string User => _session.User.Username;

    private void Start()
    {
        if (_selected is null) return;
        Run(() => _s.Treatments.Start(_selected.Id, User));
    }

    private void SaveLines()
    {
        if (_treatment is null) return;
        Run(() => PersistLines());
    }

    private void PersistLines()
    {
        var errors = new List<string>();
        var lines = RegistrationEditorViewModel.BuildLines(Lines, errors);
        if (errors.Count > 0) throw new BusinessException(errors);
        _s.Treatments.UpdateLines(_treatment!.Id, lines, User);
        _linesDirty = false;
    }

    private void Complete()
    {
        if (_treatment is null) return;
        var input = new InputErrors();
        var discount = input.Money(string.IsNullOrWhiteSpace(DiscountText) ? "0" : DiscountText, "Diskon");
        var pay = Payment.Build(input);
        try
        {
            input.ThrowIfAny();
            var question = $"Selesaikan treatment {_treatment.DocNo} dan terima pembayaran {Money.Format(Total)} ({Payment.SelectedMethod?.Name})?";
            if (HasShortage) question += $"{Environment.NewLine}{Environment.NewLine}Perhatian: stok sebagian bahan tidak cukup dan akan menjadi minus.";
            if (!_dialogs.Confirm(question, "Selesai & Bayar")) return;
            if (_linesDirty) PersistLines();
            var result = _s.Treatments.Complete(_treatment.Id, discount, pay, User);
            var done = _s.Treatments.Get(result.Id)!;
            var msg = $"Treatment {result.DocNo} LUNAS.";
            if (done.Payment is { IsCash: true } p) msg += $"{Environment.NewLine}Kembalian: Rp {Money.Format(p.ChangeAmount)}";
            if (result.Warnings.Count > 0) msg += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            _dialogs.Info(msg, "Pembayaran Tersimpan");
            if (_dialogs.Confirm("Cetak nota sekarang?", "Cetak Nota")) _printReceipt(done);
            _selected = null;
            Refresh();
        }
        catch (BusinessException ex)
        {
            // Keep the cashier's input (discount, amount received) so the error can be corrected and retried.
            _dialogs.Error(ex.Message);
        }
    }

    private void Cancel()
    {
        if (_selected is null) return;
        var sel = _selected;
        var editor = new CancelReasonEditorViewModel($"Batalkan {sel.RegNo}",
            IsWaiting ? "Registrasi dibatalkan dan keluar dari antrian." : "Treatment yang sedang dikerjakan dibatalkan. Tidak ada efek stok maupun pembayaran.",
            reason =>
            {
                if (sel.Status == RegistrationStatus.Waiting) _s.Registrations.Cancel(sel.Id, reason, User);
                else if (_treatment is not null) _s.Treatments.Cancel(_treatment.Id, reason, allowPosted: false, User);
            });
        if (_dialogs.ShowEditor(editor))
        {
            _selected = null;
            Refresh();
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
            Refresh();
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
        }
    }
}

/// <summary>Stock card per product for a date range (kartu stok).</summary>
public sealed class StockCardPageViewModel : PageViewModel
{
    private readonly PresentationServices _s;
    private long _productId;
    private DateTime? _from, _to;
    private StockCard? _card;

    public StockCardPageViewModel(PresentationServices s)
    {
        _s = s;
        var today = s.Clock.Now.Date;
        _from = new DateTime(today.Year, today.Month, 1);
        _to = today;
        Products = s.Products.GetAll().OrderBy(p => p.Name).Select(p => new Option<long>(p.Id, $"{p.Name} ({p.Code})")).ToList();
        RefreshCommand = new RelayCommand(Refresh);
    }

    public override string Title => "Kartu Stok";
    public override string Key => "trans:stockcard";
    public IReadOnlyList<Option<long>> Products { get; private set; }
    public RelayCommand RefreshCommand { get; }
    public ObservableCollection<StockCardRow> Rows { get; } = [];

    public long ProductId { get => _productId; set { if (SetProperty(ref _productId, value)) Load(); } }
    public DateTime? DateFrom { get => _from; set { if (SetProperty(ref _from, value)) Load(); } }
    public DateTime? DateTo { get => _to; set { if (SetProperty(ref _to, value)) Load(); } }

    public string UnitText => _card?.Product.Unit ?? "";
    public string OpeningText => _card is null ? "-" : $"{Quantity.Format(_card.Opening)} {UnitText}";
    public string ClosingText => _card is null ? "-" : $"{Quantity.Format(_card.Closing)} {UnitText}";
    public string InText => _card is null ? "-" : Quantity.Format(_card.Rows.Sum(r => r.QtyIn));
    public string OutText => _card is null ? "-" : Quantity.Format(_card.Rows.Sum(r => r.QtyOut));

    public override void Refresh()
    {
        Products = _s.Products.GetAll().OrderBy(p => p.Name).Select(p => new Option<long>(p.Id, $"{p.Name} ({p.Code})")).ToList();
        OnPropertyChanged(nameof(Products));
        Load();
    }

    private void Load()
    {
        Rows.Clear();
        _card = null;
        if (_productId > 0 && _from.HasValue && _to.HasValue)
        {
            var (f, t) = _from <= _to ? (_from.Value, _to.Value) : (_to.Value, _from.Value);
            try { _card = _s.StockCards.Get(_productId, f, t); }
            catch (BusinessException) { _card = null; }
            if (_card is not null) foreach (var r in _card.Rows) Rows.Add(r);
        }
        foreach (var p in new[] { nameof(UnitText), nameof(OpeningText), nameof(ClosingText), nameof(InText), nameof(OutText) })
            OnPropertyChanged(p);
    }
}
