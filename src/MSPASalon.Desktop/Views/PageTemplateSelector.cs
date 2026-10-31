using System.Windows;
using System.Windows.Controls;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Masters;
using MSPASalon.Presentation.ViewModels.Transactions;

namespace MSPASalon.Desktop.Views;

/// <summary>
/// Picks the view for a tab's PageViewModel. Needed because ListPageViewModel&lt;T&gt; is generic,
/// and XAML implicit DataTemplates cannot target an open generic type.
/// </summary>
public sealed class PageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ListTemplate { get; set; }
    public DataTemplate? DashboardTemplate { get; set; }
    public DataTemplate? BranchTemplate { get; set; }
    public DataTemplate? ComingSoonTemplate { get; set; }
    public DataTemplate? TreatmentTemplate { get; set; }
    public DataTemplate? StockCardTemplate { get; set; }
    public DataTemplate? ReportTemplate { get; set; }
    public DataTemplate? ResetDataTemplate { get; set; }
    public DataTemplate? RestoreTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        IListPage => ListTemplate,
        DashboardViewModel => DashboardTemplate,
        BranchSettingsViewModel => BranchTemplate,
        ComingSoonViewModel => ComingSoonTemplate,
        TreatmentPageViewModel => TreatmentTemplate,
        StockCardPageViewModel => StockCardTemplate,
        MSPASalon.Presentation.ViewModels.Reports.ReportPageViewModel => ReportTemplate,
        ResetDataPageViewModel => ResetDataTemplate,
        RestorePageViewModel => RestoreTemplate,
        _ => base.SelectTemplate(item, container),
    };
}
