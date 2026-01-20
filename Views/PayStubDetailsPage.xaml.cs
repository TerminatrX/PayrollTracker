using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Models;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

public sealed partial class PayStubDetailsPage : Page
{
    public PayStubDetailsViewModel ViewModel { get; }

    public PayStubDetailsPage()
    {
        ViewModel = App.GetService<PayStubDetailsViewModel>();
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
