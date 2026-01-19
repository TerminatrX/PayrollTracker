using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.UI.ViewModels;

namespace PayrollManager.UI.Views;

public sealed partial class EmployeeManagementPage : Page
{
    private readonly IServiceScope _scope;

    public EmployeesViewModel ViewModel { get; }

    public EmployeeManagementPage()
    {
        _scope = App.Services.CreateScope();
        var dbContext = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ViewModel = new EmployeesViewModel(dbContext);
        this.InitializeComponent();
        this.DataContext = ViewModel;
        _ = ViewModel.LoadEmployeesAsync();

        // Keyboard shortcuts
        KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.N,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        });
        KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.S,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        });
        KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.F,
            Modifiers = Windows.System.VirtualKeyModifiers.Control
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _scope.Dispose();
    }

    private void EmployeesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is Employee employee)
        {
            ViewModel.SelectedEmployee = employee;
            // Load employee details into editor
            ViewModel.Editor.LoadFrom(employee);
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        
        // Ctrl+N - New Employee
        if (e.Key == Windows.System.VirtualKey.N && 
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            ViewModel.NewEmployeeCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+S - Save
        else if (e.Key == Windows.System.VirtualKey.S && 
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            if (ViewModel.CanSave)
            {
                ViewModel.SaveEmployeeCommand.Execute(null);
            }
            e.Handled = true;
        }
        // Ctrl+F - Focus search
        else if (e.Key == Windows.System.VirtualKey.F && 
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            // SearchBox will be available after XAML compilation
            // SearchBox?.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            e.Handled = true;
        }
    }
}
