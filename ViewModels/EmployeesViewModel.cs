using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using System.Collections.ObjectModel;

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// ViewModel for the Employees page with master-detail layout.
/// </summary>
public partial class EmployeesViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;

    public EmployeesViewModel(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        Editor = new EmployeeViewModel();
        
        // Initialize collections
        Departments = new ObservableCollection<string>
        {
            "All Departments",
            "Engineering",
            "Marketing",
            "Sales",
            "HR",
            "Operations",
            "Finance"
        };
        
        Managers = new ObservableCollection<string>
        {
            "Sarah Johnson",
            "Mark Stevens",
            "Emily Chen"
        };
        
        // Load employees on initialization
        _ = LoadEmployeesAsync();
    }
    // ═══════════════════════════════════════════════════════════════
    // COLLECTIONS
    // ═══════════════════════════════════════════════════════════════

    public ObservableCollection<Employee> AllEmployees { get; } = new();
    public ObservableCollection<Employee> FilteredEmployees { get; } = new();
    public ObservableCollection<string> Departments { get; } = new();
    public ObservableCollection<string> Managers { get; } = new();

    // ═══════════════════════════════════════════════════════════════
    // SELECTION
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private Employee? _selectedEmployee;

    [ObservableProperty]
    private EmployeeViewModel _editor = new();

    public bool HasSelectedEmployee => SelectedEmployee != null;

    // ═══════════════════════════════════════════════════════════════
    // SEARCH STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchActive;

    // ═══════════════════════════════════════════════════════════════
    // FILTERS
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _statusFilterIndex = 0; // 0=All, 1=Active, 2=Inactive

    [ObservableProperty]
    private int _payTypeFilterIndex = 0; // 0=All, 1=Salary, 2=Hourly

    [ObservableProperty]
    private string? _selectedDepartment;

    [ObservableProperty]
    private bool _isStatusFilterActive = true;

    // ═══════════════════════════════════════════════════════════════
    // STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private DateTime _lastSaveTime;

    public string LastSaveTimeDisplay => LastSaveTime == default 
        ? "Never" 
        : LastSaveTime.ToString("g");

    public bool CanSave => !Editor.HasErrors &&
                           !string.IsNullOrWhiteSpace(Editor.FirstName) &&
                           !string.IsNullOrWhiteSpace(Editor.LastName);

    public bool CanDelete => Editor.Id.HasValue;

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

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

            ApplyFiltersCommand.Execute(null);
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

    [RelayCommand(CanExecute = nameof(CanDelete))]
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

    [RelayCommand]
    private void Import()
    {
        // Placeholder - will be implemented
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (SelectedEmployee != null)
        {
            Editor.LoadFrom(SelectedEmployee);
        }
        else
        {
            Editor.Reset();
        }
    }

    [RelayCommand]
    private void EditCompensation()
    {
        // Placeholder - will be implemented
    }

    [RelayCommand]
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

        // Status filter
        query = StatusFilterIndex switch
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

        // Department filter
        if (!string.IsNullOrEmpty(SelectedDepartment) && SelectedDepartment != "All Departments")
        {
            // Placeholder - will filter by department when Employee model has Department property
        }

        foreach (var employee in query)
        {
            FilteredEmployees.Add(employee);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnStatusFilterIndexChanged(int value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnPayTypeFilterIndexChanged(int value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedDepartmentChanged(string? value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        if (value != null)
        {
            Editor.LoadFrom(value);
        }
        else
        {
            Editor.Reset();
        }
    }
}
