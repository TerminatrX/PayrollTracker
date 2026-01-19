using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

public sealed partial class ReportsPage : Page
{
    private readonly IServiceScope _scope;

    public ReportsViewModel ViewModel { get; }

    public ReportsPage()
    {
        InitializeComponent();
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ViewModel = new ReportsViewModel(dbContext);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope.Dispose();
    }
}
