using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IServiceScope _scope;

    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ViewModel = new SettingsViewModel(dbContext);
        _ = ViewModel.LoadSettingsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope.Dispose();
    }
}
