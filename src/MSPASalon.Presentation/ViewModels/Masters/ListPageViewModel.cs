using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;
using MSPASalon.Presentation.Services;

namespace MSPASalon.Presentation.ViewModels.Masters;

public enum ColumnKind { Text, Money, Qty, Bool, DateTime }

/// <summary>Extra toolbar action (e.g. Reset Password). A class with properties so WPF can bind to it.</summary>
public sealed record ActionItem(string Label, IRelayCommand Command);

/// <summary>Grid column definition. Exactly one column per page should be <see cref="Star"/> (fills remaining width).</summary>
public sealed record ColumnDef(string Header, string Path, ColumnKind Kind = ColumnKind.Text, double MinWidth = 80, bool Star = false);

/// <summary>
/// Optional behaviour for <see cref="ListPageViewModel{T}"/>. Masters use the defaults (Tambah / Ubah / Hapus).
/// Transaction documents use a date filter, "Lihat / Cetak" instead of edit, and "Batalkan" (with a reason) instead of delete.
/// </summary>
public sealed class ListPageOptions<T> where T : class
{
    public string AddLabel { get; init; } = "Tambah";
    public string EditLabel { get; init; } = "Ubah";
    public string DeleteLabel { get; init; } = "Hapus";
    public bool AllowAdd { get; init; } = true;
    /// <summary>Edit opens a read-only view (e.g. receipt), so it is allowed without modify rights.</summary>
    public bool EditIsView { get; init; }
    /// <summary>Loads by date range instead of everything; enables the date filter.</summary>
    public Func<DateTime, DateTime, IReadOnlyList<T>>? LoadRange { get; init; }
    public int DefaultDays { get; init; } = 1;
    /// <summary>Replaces the default confirm-and-delete; returns true when data changed.</summary>
    public Func<T, bool>? CustomDelete { get; init; }
    /// <summary>Whether the delete/cancel action applies to this row (e.g. already cancelled documents).</summary>
    public Func<T, bool>? CanDeleteItem { get; init; }
    /// <summary>Called after an editor opened by "Add" saved successfully (e.g. "process now?" or "print receipt?").</summary>
    public Action<EditorViewModel>? AfterAdd { get; init; }
}

/// <summary>Non-generic surface bound by the WPF MasterListView.</summary>
public interface IListPage
{
    string Title { get; }
    IReadOnlyList<ColumnDef> Columns { get; }
    System.Collections.IEnumerable Items { get; }
    object? SelectedItem { get; set; }
    string SearchText { get; set; }
    string StatusText { get; }
    bool CanModify { get; }
    string AddLabel { get; }
    string EditLabel { get; }
    string DeleteLabel { get; }
    bool ShowAdd { get; }
    bool HasDateFilter { get; }
    DateTime? DateFrom { get; set; }
    DateTime? DateTo { get; set; }
    IRelayCommand AddCommand { get; }
    IRelayCommand EditCommand { get; }
    IRelayCommand DeleteCommand { get; }
    IRelayCommand RefreshCommand { get; }
    IReadOnlyList<ActionItem> ExtraActions { get; }
}

/// <summary>
/// Generic searchable list with Add / Edit / Delete for one entity type. Behaviour (load, delete, editor)
/// is supplied through delegates so the same page serves every master, users, roles and transaction documents.
/// </summary>
public sealed class ListPageViewModel<T> : PageViewModel, IListPage where T : class
{
    private readonly Func<IReadOnlyList<T>> _load;
    private readonly Func<T?, EditorViewModel?> _editorFactory;
    private readonly Func<T, string?>? _deleteWarning;
    private readonly Func<T, string?> _delete;
    private readonly Func<T, string> _searchText;
    private readonly Func<T, string> _displayName;
    private readonly IDialogService _dialogs;
    private readonly ListPageOptions<T> _options;
    private List<T> _all = [];
    private string _searchTextValue = "";
    private object? _selectedItem;
    private DateTime? _dateFrom, _dateTo;

    public ListPageViewModel(
        string key, string title, IReadOnlyList<ColumnDef> columns, IDialogService dialogs,
        Func<IReadOnlyList<T>> load, Func<T?, EditorViewModel?> editorFactory,
        Func<T, string?> delete, Func<T, string> searchText, Func<T, string> displayName,
        bool canModify, Func<T, string?>? deleteWarning = null, ListPageOptions<T>? options = null, DateTime? today = null)
    {
        Key = key; Title = title; Columns = columns; _dialogs = dialogs;
        _load = load; _editorFactory = editorFactory; _delete = delete; _searchText = searchText;
        _displayName = displayName; _deleteWarning = deleteWarning; CanModify = canModify;
        _options = options ?? new ListPageOptions<T>();
        if (_options.LoadRange is not null)
        {
            var d = (today ?? DateTime.Today).Date;
            _dateFrom = d.AddDays(1 - Math.Max(1, _options.DefaultDays));
            _dateTo = d;
        }

        AddCommand = new RelayCommand(Add, () => CanModify && _options.AllowAdd);
        EditCommand = new RelayCommand(Edit, () => (CanModify || _options.EditIsView) && Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => CanModify && Selected is { } s && (_options.CanDeleteItem?.Invoke(s) ?? true));
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public override string Key { get; }
    public override string Title { get; }
    public IReadOnlyList<ColumnDef> Columns { get; }
    public bool CanModify { get; }
    public string AddLabel => _options.AddLabel;
    public string EditLabel => _options.EditLabel;
    public string DeleteLabel => _options.DeleteLabel;
    public bool ShowAdd => _options.AllowAdd;
    public bool HasDateFilter => _options.LoadRange is not null;
    public ObservableCollection<T> Items { get; } = [];
    System.Collections.IEnumerable IListPage.Items => Items;
    public List<ActionItem> Extra { get; } = [];
    public IReadOnlyList<ActionItem> ExtraActions => Extra;
    public bool HasExtraActions => Extra.Count > 0;

    public IRelayCommand AddCommand { get; }
    public IRelayCommand EditCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand RefreshCommand { get; }

    public T? Selected => _selectedItem as T;

    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            NotifyCommands();
        }
    }

    public DateTime? DateFrom
    {
        get => _dateFrom;
        set { if (SetProperty(ref _dateFrom, value?.Date)) Refresh(); }
    }

    public DateTime? DateTo
    {
        get => _dateTo;
        set { if (SetProperty(ref _dateTo, value?.Date)) Refresh(); }
    }

    public void NotifyCommands()
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        foreach (var a in Extra) a.Command.NotifyCanExecuteChanged();
    }

    public string SearchText
    {
        get => _searchTextValue;
        set { if (SetProperty(ref _searchTextValue, value ?? "")) ApplyFilter(); }
    }

    public string StatusText => Items.Count == _all.Count ? $"{_all.Count} data" : $"{Items.Count} dari {_all.Count} data";

    public override void Refresh()
    {
        if (_options.LoadRange is { } range)
        {
            var from = _dateFrom ?? DateTime.Today;
            var to = _dateTo ?? from;
            if (to < from) (from, to) = (to, from);
            _all = range(from, to).ToList();
        }
        else
        {
            _all = _load().ToList();
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keep = Selected;
        var terms = _searchTextValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Items.Clear();
        foreach (var item in _all)
        {
            var hay = _searchText(item);
            if (terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) Items.Add(item);
        }
        SelectedItem = keep is not null && Items.Contains(keep) ? keep : null;
        OnPropertyChanged(nameof(StatusText));
    }

    private void Add()
    {
        try
        {
            if (_editorFactory(null) is { } editor && _dialogs.ShowEditor(editor))
            {
                Refresh();
                _options.AfterAdd?.Invoke(editor);
            }
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    private void Edit()
    {
        if (Selected is not { } item) return;
        try
        {
            if (_editorFactory(item) is { } editor && _dialogs.ShowEditor(editor)) Refresh();
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    private void Delete()
    {
        if (Selected is not { } item) return;
        try
        {
            if (_options.CustomDelete is { } custom)
            {
                if (custom(item)) Refresh();
                return;
            }
            var question = $"Hapus '{_displayName(item)}'?";
            var warning = _deleteWarning?.Invoke(item);
            if (!string.IsNullOrEmpty(warning)) question += Environment.NewLine + Environment.NewLine + warning;
            if (!_dialogs.Confirm(question, "Konfirmasi Hapus")) return;
            _delete(item);
            Refresh();
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
        }
    }
}
