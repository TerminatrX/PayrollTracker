using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Pay stub details page showing complete earnings breakdown.
/// Displays employer/employee info, earnings, deductions, taxes, and YTD totals.
/// </summary>
public sealed partial class PayStubPage : Page
{
    public PayStubViewModel ViewModel { get; }

    public PayStubPage()
    {
        ViewModel = App.GetService<PayStubViewModel>();
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Handle navigation parameters
        if (e.Parameter is int payStubId)
        {
            _ = ViewModel.LoadPayStubCommand.ExecuteAsync(payStubId);
        }
    }
}
