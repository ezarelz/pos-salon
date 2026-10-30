using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MSPASalon.Desktop.Converters;
using MSPASalon.Presentation.ViewModels.Masters;

namespace MSPASalon.Desktop.Views;

/// <summary>View-only code: builds grid columns from the ViewModel's column definitions and maps keyboard shortcuts.</summary>
public partial class MasterListView : UserControl
{
    private static readonly MoneyConverter Money = new();
    private static readonly QtyConverter Qty = new();
    private static readonly CheckMarkConverter Check = new();
    private static readonly DateTimeTextConverter When = new();

    public MasterListView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildColumns();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private IListPage? ListPage => DataContext as IListPage;

    private void BuildColumns()
    {
        ListGrid.Columns.Clear();
        if (ListPage is null) return;
        foreach (var def in ListPage.Columns)
        {
            var binding = new Binding(def.Path) { Mode = BindingMode.OneWay };
            Style? style = null;
            switch (def.Kind)
            {
                case ColumnKind.Money:
                    binding.Converter = Money;
                    style = (Style)FindResource("RightCell");
                    break;
                case ColumnKind.Qty:
                    binding.Converter = Qty;
                    style = (Style)FindResource("RightCell");
                    break;
                case ColumnKind.Bool:
                    binding.Converter = Check;
                    style = (Style)FindResource("CenterCell");
                    break;
                case ColumnKind.DateTime:
                    binding.Converter = When;
                    break;
            }
            var column = new DataGridTextColumn
            {
                Header = def.Header,
                Binding = binding,
                MinWidth = def.MinWidth,
                Width = def.Star ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                SortMemberPath = def.Path,
            };
            if (style is not null) column.ElementStyle = style;
            ListGrid.Columns.Add(column);
        }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only react to double-clicks on a row, not on the header or scrollbar.
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ListGrid, source) is DataGridRow
            && ListPage?.EditCommand.CanExecute(null) == true)
            ListPage.EditCommand.Execute(null);
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ListPage is null) return;
        if (e.Key == Key.Enter && ListPage.EditCommand.CanExecute(null)) { ListPage.EditCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Delete && ListPage.DeleteCommand.CanExecute(null)) { ListPage.DeleteCommand.Execute(null); e.Handled = true; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (ListPage is null || e.Handled) return;
        if (e.Key == Key.F5) { ListPage.RefreshCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control && ListPage.AddCommand.CanExecute(null))
        {
            ListPage.AddCommand.Execute(null);
            e.Handled = true;
        }
    }
}
