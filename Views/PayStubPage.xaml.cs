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
    public PayStubViewModel ViewModel { get; private set; }

    public PayStubPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // ViewModel can be passed as navigation parameter or resolved from DI
        if (e.Parameter is PayStubViewModel vm)
        {
            ViewModel = vm;
        }
        else if (e.Parameter is int payStubId)
        {
            ViewModel = App.GetService<PayStubViewModel>();
            ViewModel.LoadPayStub(payStubId);
        }
        else
        {
            ViewModel = App.GetService<PayStubViewModel>();
        }

        this.DataContext = ViewModel;
    }
}
