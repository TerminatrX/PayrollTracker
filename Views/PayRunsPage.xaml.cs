using Microsoft.UI.Xaml.Controls;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

/// <summary>
/// Pay Run Wizard page with stepper navigation.
/// Allows configuring pay periods, entering hours, and finalizing pay runs.
/// </summary>
public sealed partial class PayRunsPage : Page
{
    public PayRunWizardViewModel ViewModel { get; }

    public PayRunsPage()
    {
        ViewModel = App.GetService<PayRunWizardViewModel>();
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }
}
