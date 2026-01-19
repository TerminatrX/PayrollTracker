using Microsoft.UI.Xaml.Controls;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Employee management page with master-detail layout.
/// Displays employee list on left, selected employee details on right.
/// </summary>
public sealed partial class EmployeesPage : Page
{
    public EmployeesViewModel ViewModel { get; }

    public EmployeesPage()
    {
        ViewModel = App.GetService<EmployeesViewModel>();
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }
}
