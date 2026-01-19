using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PayrollManager.UI.ViewModels;

public partial class EmployeesViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private EmployeeViewModel? _selectedEmployee;
    private string _searchText = string.Empty;
    private int _activeFilterIndex = 0; // 0=All, 1=Active, 2=Inactive
    private int _payTypeFilterIndex = 0; // 0=All, 1=Salary, 2=Hourly
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private DateTime _lastSaveTime;

    public EmployeesViewModel(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        Editor = new EmployeeViewModel();
        Editor.PropertyChanged += Editor_PropertyChanged;
    }

    public ObservableCollection<Employee> AllEmployees { get; } = new();
    public ObservableCollection<Employee> FilteredEmployees { get; } = new();
    public EmployeeViewModel Editor { get; }

    public EmployeeViewModel? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    public bool HasSelection => SelectedEmployee != null;
    public bool CanDelete => Editor.Id.HasValue;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public int ActiveFilterIndex
    {
        get => _activeFilterIndex;
        set
        {
            if (SetProperty(ref _activeFilterIndex, value))
            {
                ApplyFilters();
            }
        }
    }

    public int PayTypeFilterIndex
    {
        get => _payTypeFilterIndex;
        set
        {
            if (SetProperty(ref _payTypeFilterIndex, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public DateTime LastSaveTime
    {
        get => _lastSaveTime;
        set
        {
            if (SetProperty(ref _lastSaveTime, value))
            {
                OnPropertyChanged(nameof(LastSaveTimeDisplay));
            }
        }
    }

    public string LastSaveTimeDisplay => LastSaveTime == default ? "Never" : LastSaveTime.ToString("g");

    public bool CanSave => !Editor.HasErrors && 
                           !string.IsNullOrWhiteSpace(Editor.FirstName) && 
                           !string.IsNullOrWhiteSpace(Editor.LastName);

    private void Editor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmployeeViewModel.HasErrors) ||
            e.PropertyName == nameof(EmployeeViewModel.FirstName) ||
            e.PropertyName == nameof(EmployeeViewModel.LastName))
        {
            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    public async Task LoadEmployeesAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading employees...";

        try
        {
            AllEmployees.Clear();
            var employees = await _dbContext.Employees
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();

            foreach (var employee in employees)
            {
                AllEmployees.Add(employee);
            }

            ApplyFilters();
            StatusMessage = $"Loaded {employees.Count} employees";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading employees: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NewEmployee()
    {
        Editor.Reset();
        SelectedEmployee = null;
        OnPropertyChanged(nameof(CanDelete));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveEmployeeAsync()
    {
        if (Editor.HasErrors)
        {
            StatusMessage = "Please fix validation errors before saving";
            return;
        }

        IsLoading = true;
        StatusMessage = "Saving...";

        try
        {
            if (Editor.Id.HasValue)
            {
                var employee = await _dbContext.Employees.FindAsync(Editor.Id.Value);
                if (employee != null)
                {
                    Editor.ApplyTo(employee);
                }
            }
            else
            {
                var employee = new Employee();
                Editor.ApplyTo(employee);
                _dbContext.Employees.Add(employee);
            }

            await _dbContext.SaveChangesAsync();
            LastSaveTime = DateTime.Now;
            await LoadEmployeesAsync();
            Editor.Reset();
            StatusMessage = "Employee saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteEmployeeAsync()
    {
        if (!Editor.Id.HasValue)
            return;

        IsLoading = true;
        StatusMessage = "Deleting...";

        try
        {
            var employee = await _dbContext.Employees.FindAsync(Editor.Id.Value);
            if (employee != null)
            {
                _dbContext.Employees.Remove(employee);
                await _dbContext.SaveChangesAsync();
                LastSaveTime = DateTime.Now;
                await LoadEmployeesAsync();
                Editor.Reset();
                StatusMessage = "Employee deleted successfully";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectEmployee(Employee? employee)
    {
        if (employee == null)
        {
            Editor.Reset();
            return;
        }

        Editor.LoadFrom(employee);
        LoadPaySummaryAsync(employee.Id).ConfigureAwait(false);
        OnPropertyChanged(nameof(CanDelete));
    }

    private async Task LoadPaySummaryAsync(int employeeId)
    {
        var year = DateTime.Today.Year;
        
        var lastStub = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.EmployeeId == employeeId)
            .OrderByDescending(ps => ps.PayRun!.PayDate)
            .FirstOrDefaultAsync();

        if (lastStub != null)
        {
            Editor.LastPayDate = lastStub.PayRun?.PayDate;
            Editor.LastGross = lastStub.GrossPay;
            Editor.LastNet = lastStub.NetPay;
        }

        var ytdTotals = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.EmployeeId == employeeId && ps.PayRun!.PayDate.Year == year)
            .GroupBy(ps => ps.EmployeeId)
            .Select(g => new
            {
                Gross = g.Sum(x => x.GrossPay),
                Taxes = g.Sum(x => x.TaxFederal + x.TaxState + x.TaxSocialSecurity + x.TaxMedicare),
                Net = g.Sum(x => x.NetPay)
            })
            .FirstOrDefaultAsync();

        if (ytdTotals != null)
        {
            Editor.YtdGross = ytdTotals.Gross;
            Editor.YtdTaxes = ytdTotals.Taxes;
            Editor.YtdNet = ytdTotals.Net;
        }
    }

    private void ApplyFilters()
    {
        FilteredEmployees.Clear();

        var query = AllEmployees.AsEnumerable();

        // Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            query = query.Where(e =>
                e.FirstName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.LastName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.Id.ToString().Contains(search));
        }

        // Active filter
        query = ActiveFilterIndex switch
        {
            1 => query.Where(e => e.IsActive),
            2 => query.Where(e => !e.IsActive),
            _ => query
        };

        // Pay type filter
        query = PayTypeFilterIndex switch
        {
            1 => query.Where(e => !e.IsHourly), // Salary
            2 => query.Where(e => e.IsHourly),  // Hourly
            _ => query
        };

        foreach (var employee in query)
        {
            FilteredEmployees.Add(employee);
        }
    }
}
