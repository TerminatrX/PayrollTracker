using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Services;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

public sealed partial class PayrollRunPage : Page
{
    private readonly IServiceScope _scope;

    public PayRunWizardViewModel ViewModel { get; }

    public PayrollRunPage()
    {
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payrollService = _scope.ServiceProvider.GetRequiredService<PayrollService>();
        ViewModel = new PayRunWizardViewModel(dbContext, payrollService);
        this.InitializeComponent();
        this.DataContext = ViewModel;
        _ = ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope.Dispose();
    }
}
