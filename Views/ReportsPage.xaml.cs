using Microsoft.UI.Xaml.Controls;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Reports page with sidebar navigation, filters, and summary data grid.
/// Displays payroll summary reports with KPI metrics and employee breakdowns.
/// </summary>
public sealed partial class ReportsPage : Page
{
    public ReportsViewModel ViewModel { get; }

    public ReportsPage()
    {
        ViewModel = App.GetService<ReportsViewModel>();
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }
}
