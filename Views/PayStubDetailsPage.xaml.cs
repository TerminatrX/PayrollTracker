using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

public sealed partial class PayStubDetailsPage : Page
{
    private readonly IServiceScope _scope;

    public PayStubDetailsViewModel ViewModel { get; }

    public PayStubDetailsPage()
    {
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exportService = _scope.ServiceProvider.GetRequiredService<ExportService>();
        ViewModel = new PayStubDetailsViewModel(dbContext, exportService);
        InitializeComponent();
        this.DataContext = ViewModel;
        
        // Subscribe to navigation request
        ViewModel.NavigateBackRequested += (s, e) =>
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        // Support navigation from PayRunsPage or Employee pages with PayStubNavigationParameter
        if (e.Parameter is PayStubNavigationParameter param)
        {
            ViewModel.LoadPayStub(param.PayStub, param.Employee);
        }
        // Support navigation with just a PayStub ID (for future use)
        else if (e.Parameter is int payStubId)
        {
            _ = ViewModel.LoadPayStubByIdAsync(payStubId);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope?.Dispose();
    }
}

/// <summary>
/// Navigation parameter for PayStubDetailsPage.
/// Used when navigating from PayRunsPage or Employee pages.
/// </summary>
public class PayStubNavigationParameter
{
    public required PayStub PayStub { get; set; }
    public required Employee Employee { get; set; }
}
