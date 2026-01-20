using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Services;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Pay stub details page showing complete earnings breakdown.
/// Displays employer/employee info, earnings, deductions, taxes, and YTD totals.
/// </summary>
public sealed partial class PayStubPage : Page
{
    private readonly IServiceScope _scope;

    public PayStubViewModel ViewModel { get; }

    public PayStubPage()
    {
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exportService = _scope.ServiceProvider.GetRequiredService<ExportService>();
        ViewModel = new PayStubViewModel(dbContext, exportService);
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Reload pay stubs when navigating to this page to show newly generated ones
        _ = ViewModel.LoadPayStubsAsync();

        // Handle navigation parameters
        if (e.Parameter is int payStubId)
        {
            _ = ViewModel.LoadPayStubCommand.ExecuteAsync(payStubId);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope?.Dispose();
    }
}
