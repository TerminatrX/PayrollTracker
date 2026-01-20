using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Employee management page with master-detail layout.
/// Displays employee list on left, selected employee details on right.
/// </summary>
public sealed partial class EmployeesPage : Page
{
    private readonly IServiceScope _scope;

    public EmployeesViewModel ViewModel { get; }

    public EmployeesPage()
    {
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ViewModel = new EmployeesViewModel(dbContext);
        this.InitializeComponent();
        this.DataContext = ViewModel;
        
        // Load employees when page is initialized
        _ = ViewModel.LoadEmployeesAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope?.Dispose();
    }
}
