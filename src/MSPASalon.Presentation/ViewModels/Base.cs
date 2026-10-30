using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MSPASalon.Core.Domain;

namespace MSPASalon.Presentation.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

/// <summary>Content of a tab in the main window.</summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    /// <summary>Navigation key; opening the same key twice re-activates the existing tab.</summary>
    public abstract string Key { get; }

    /// <summary>Called when the tab is (re)activated.</summary>
    public virtual void Refresh() { }
}

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Base for modal editors. The view shows <see cref="ErrorText"/> in a fixed footer next to the
/// Save/Cancel buttons, so validation messages and actions are always visible regardless of window size.
/// </summary>
public abstract class EditorViewModel : ViewModelBase
{
    private string? _errorText;
    private bool _isSaving;

    protected EditorViewModel()
    {
        SaveCommand = new RelayCommand(ExecuteSave, () => !_isSaving);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
    }

    public abstract string Title { get; }
    public virtual string SaveLabel => "Simpan";
    public virtual string CancelLabel => "Batal";
    /// <summary>Preferred dialog size (DIP); the window clamps it to the screen.</summary>
    public virtual double PreferredWidth => 720;
    public virtual double PreferredHeight => 560;

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised with true after a successful save, false on cancel.</summary>
    public event EventHandler<bool>? CloseRequested;

    public string? ErrorText
    {
        get => _errorText;
        protected set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorText);

    /// <summary>Validates input and persists. Throw <see cref="BusinessException"/> for user-facing errors.</summary>
    protected abstract void Save();

    private void ExecuteSave()
    {
        _isSaving = true;
        SaveCommand.NotifyCanExecuteChanged();
        try
        {
            ErrorText = null;
            Save();
            CloseRequested?.Invoke(this, true);
        }
        catch (BusinessException ex)
        {
            ErrorText = string.Join(Environment.NewLine, ex.Errors);
        }
        finally
        {
            _isSaving = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Test/automation hook: runs the same logic as the Save button and returns success.</summary>
    public bool TrySave()
    {
        var ok = false;
        void Handler(object? s, bool r) => ok = r;
        CloseRequested += Handler;
        try { SaveCommand.Execute(null); }
        finally { CloseRequested -= Handler; }
        return ok;
    }
}

/// <summary>Collects parse errors of text inputs (money/qty) before the entity is validated.</summary>
public sealed class InputErrors
{
    public List<string> Items { get; } = [];

    public decimal Money(string? text, string label, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required) Items.Add($"{label} wajib diisi.");
            return 0m;
        }
        if (!Core.Domain.Money.TryParse(text, out var v)) { Items.Add($"{label} tidak valid (gunakan Rupiah bulat, mis. 28.000)."); return 0m; }
        return v;
    }

    public decimal Qty(string? text, string label, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (required) Items.Add($"{label} wajib diisi.");
            return 0m;
        }
        if (!Quantity.TryParse(text, out var v)) { Items.Add($"{label} tidak valid (angka, maksimal {Quantity.Decimals} desimal, mis. 0,1)."); return 0m; }
        return v;
    }

    public decimal? OptionalQty(string? text, string label) =>
        string.IsNullOrWhiteSpace(text) ? null : Qty(text, label);

    public void ThrowIfAny()
    {
        if (Items.Count > 0) throw new BusinessException(Items);
    }

    /// <summary>Throws with parse errors plus entity validation errors together.</summary>
    public void ThrowIfAny(IEnumerable<string> more)
    {
        var all = Items.Concat(more).ToList();
        if (all.Count > 0) throw new BusinessException(all);
    }
}
