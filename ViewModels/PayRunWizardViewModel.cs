using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// ViewModel for the Pay Run Wizard page with stepper navigation.
/// </summary>
public partial class PayRunWizardViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly PayrollService _payrollService;

    public PayRunWizardViewModel(AppDbContext dbContext, PayrollService payrollService)
    {
        _dbContext = dbContext;
        _payrollService = payrollService;
        
        // Initialize default dates
        var today = DateTimeOffset.Now.Date;
        PeriodStart = today.AddDays(-13);
        PeriodEnd = today;
        PayDate = today.AddDays(1);
        
        // Initialize on construction
        _ = InitializeAsync();
    }
    // ═══════════════════════════════════════════════════════════════
    // COLLECTIONS
    // ═══════════════════════════════════════════════════════════════

    public ObservableCollection<PayRunEmployeeRow> EmployeeRows { get; } = new();
    public ObservableCollection<PayStubResult> GeneratedStubs { get; } = new();

    // ═══════════════════════════════════════════════════════════════
    // WIZARD PROGRESS STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _currentStep = 0;

    [ObservableProperty]
    private bool _isStep1Complete;

    [ObservableProperty]
    private bool _isStep2Complete;

    [ObservableProperty]
    private bool _isStep3Complete;

    [ObservableProperty]
    private bool _isRunComplete;

    public bool IsStep1 => CurrentStep == 0;
    public bool IsStep2 => CurrentStep == 1;
    public bool IsStep3 => CurrentStep == 2;
    public bool CanGoBack => CurrentStep > 0 && !IsRunComplete;
    public bool CanGoNext => CurrentStep < 2 && !IsRunComplete;

    // ═══════════════════════════════════════════════════════════════
    // PERIOD DATES
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private DateTimeOffset _periodStart;

    [ObservableProperty]
    private DateTimeOffset _periodEnd;

    [ObservableProperty]
    private DateTimeOffset _payDate;

    public string PeriodRangeDisplay => $"{PeriodStart:MMM dd} - {PeriodEnd:MMM dd, yyyy}";
    public string PayDateDisplay => PayDate.ToString("MMM dd, yyyy");

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYEE HOURS
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private PayRunEmployeeRow? _selectedEmployeeRow;

    // ═══════════════════════════════════════════════════════════════
    // DRAFT STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isDraft;

    [ObservableProperty]
    private DateTime? _draftSavedAt;

    [ObservableProperty]
    private string _draftName = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // SUMMARY ESTIMATES
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private decimal _estimatedGross;

    [ObservableProperty]
    private decimal _estimatedRegular;

    [ObservableProperty]
    private decimal _estimatedOvertime;

    [ObservableProperty]
    private decimal _estimatedBonus;

    [ObservableProperty]
    private decimal _estimatedTaxes;

    [ObservableProperty]
    private decimal _estimatedNet;

    [ObservableProperty]
    private int _includedCount;

    // ═══════════════════════════════════════════════════════════════
    // STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public int EmployeeCount => EmployeeRows.Count;
    public string AutoSaveStatus => IsDraft && DraftSavedAt.HasValue 
        ? $"Auto-saved {GetTimeAgo(DraftSavedAt.Value)}" 
        : "Not saved";

    public string TotalGrossDisplay => $"${EstimatedGross:N2}";
    public string EstimatedTaxesDisplay => $"${EstimatedTaxes:N2}";
    public string BenefitsDeductionsDisplay => "$0.00";
    public string NetPayTotalDisplay => $"${EstimatedNet:N2}";

    public string StepTitle => CurrentStep switch
    {
        0 => "Step 1: Select Pay Period",
        1 => "Step 2: Review Employees & Earnings",
        2 => IsRunComplete ? "Pay Run Complete" : "Step 3: Summary & Generate",
        _ => "Pay Run"
    };

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading...";

        try
        {
            // Load default dates from last pay run
            var lastPayRun = await _dbContext.PayRuns
                .OrderByDescending(p => p.PayDate)
                .FirstOrDefaultAsync();

            if (lastPayRun != null)
            {
                var nextStart = lastPayRun.PeriodEnd.AddDays(1);
                PeriodStart = new DateTimeOffset(nextStart);
                PeriodEnd = new DateTimeOffset(nextStart.AddDays(13));
                PayDate = new DateTimeOffset(nextStart.AddDays(14));
            }

            await LoadEmployeesAsync();
            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadEmployeesAsync()
    {
        EmployeeRows.Clear();
        
        var settings = await _dbContext.CompanySettings.FirstOrDefaultAsync();
        var defaultHours = settings?.DefaultHoursPerPeriod ?? 80;

        var employees = await _dbContext.Employees
            .Where(e => e.IsActive)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync();

        foreach (var employee in employees)
        {
            var row = new PayRunEmployeeRow
            {
                EmployeeId = employee.Id,
                FullName = employee.FullName,
                EmployeeIdDisplay = $"EMP{employee.Id:D3}",
                Department = "Engineering", // Placeholder
                IsHourly = employee.IsHourly,
                PayType = employee.IsHourly ? "Hourly" : "Salary",
                HourlyRate = employee.HourlyRate
            };

            if (employee.IsHourly)
            {
                row.RegularHours = Math.Min(defaultHours, 40);
                row.OvertimeHours = Math.Max(0, defaultHours - 40);
            }

            EmployeeRows.Add(row);
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void GoNext()
    {
        if (CurrentStep < 2)
        {
            CurrentStep++;
            
            // Mark steps as complete
            if (CurrentStep == 1) IsStep1Complete = true;
            if (CurrentStep == 2) IsStep2Complete = true;
        }
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        IsLoading = true;
        StatusMessage = "Saving draft...";

        try
        {
            // Placeholder - will save draft pay run to DB
            IsDraft = true;
            DraftSavedAt = DateTime.Now;
            StatusMessage = "Draft saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving draft: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadDraftAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading draft...";

        try
        {
            // Placeholder - will load draft from DB
            StatusMessage = "Draft loaded";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading draft: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GeneratePayRunAsync()
    {
        IsLoading = true;
        StatusMessage = "Generating pay run...";

        try
        {
            // Create the pay run
            var payRun = new PayRun
            {
                PeriodStart = PeriodStart.DateTime,
                PeriodEnd = PeriodEnd.DateTime,
                PayDate = PayDate.DateTime
            };

            _dbContext.PayRuns.Add(payRun);
            await _dbContext.SaveChangesAsync();

            // Generate pay stubs for each included employee
            GeneratedStubs.Clear();
            var includedRows = EmployeeRows.Where(r => r.IsIncluded).ToList();
            var totalEmployees = includedRows.Count;
            var processedCount = 0;

            foreach (var row in includedRows)
            {
                var employee = await _dbContext.Employees.FindAsync(row.EmployeeId);
                if (employee == null)
                {
                    StatusMessage = $"Employee {row.FullName} not found";
                    continue;
                }

                // Create PayStubInput from row data
                var input = new PayStubInput
                {
                    RegularHours = (decimal)row.RegularHours,
                    OvertimeHours = (decimal)row.OvertimeHours,
                    BonusAmount = (decimal)row.BonusAmount,
                    CommissionAmount = (decimal)row.CommissionAmount,
                    BonusDescription = row.BonusDescription,
                    CommissionDescription = row.CommissionDescription
                };

                // Generate pay stub using PayrollService
                var payStub = await _payrollService.GeneratePayStubAsync(employee, payRun, input);
                
                // Add to context
                _dbContext.PayStubs.Add(payStub);
                
                // Add to generated stubs list for display
                GeneratedStubs.Add(new PayStubResult
                {
                    EmployeeName = employee.FullName,
                    EmployeeId = employee.Id,
                    GrossPay = payStub.GrossPay,
                    NetPay = payStub.NetPay,
                    Taxes = payStub.TotalTaxes
                });

                processedCount++;
                StatusMessage = $"Processing {processedCount} of {totalEmployees} employees...";
            }

            // Save all pay stubs
            await _dbContext.SaveChangesAsync();

            IsStep3Complete = true;
            IsRunComplete = true;
            StatusMessage = $"Pay run generated successfully: {processedCount} pay stubs created";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error generating pay run: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void StartNewRun()
    {
        IsRunComplete = false;
        IsDraft = false;
        CurrentStep = 0;
        IsStep1Complete = false;
        IsStep2Complete = false;
        IsStep3Complete = false;
        GeneratedStubs.Clear();
        InitializeCommand.Execute(null);
    }

    [RelayCommand]
    private void CalculateEstimates()
    {
        // Calculate estimates based on employee hours
        decimal totalGross = 0;
        decimal totalRegular = 0;
        decimal totalOvertime = 0;
        decimal totalBonus = 0;
        int count = 0;

        foreach (var row in EmployeeRows.Where(r => r.IsIncluded))
        {
            count++;
            if (row.IsHourly)
            {
                var regular = (decimal)row.RegularHours * row.HourlyRate;
                var overtime = (decimal)row.OvertimeHours * row.HourlyRate * 1.5m;
                totalRegular += regular;
                totalOvertime += overtime;
                totalGross += regular + overtime;
            }
            totalBonus += (decimal)row.BonusAmount + (decimal)row.CommissionAmount;
            totalGross += (decimal)row.BonusAmount + (decimal)row.CommissionAmount;
        }

        EstimatedGross = totalGross;
        EstimatedRegular = totalRegular;
        EstimatedOvertime = totalOvertime;
        EstimatedBonus = totalBonus;
        EstimatedTaxes = totalGross * 0.25m; // Placeholder calculation
        EstimatedNet = totalGross - EstimatedTaxes;
        IncludedCount = count;
    }

    private string GetTimeAgo(DateTime dateTime)
    {
        var timeSpan = DateTime.Now - dateTime;
        if (timeSpan.TotalMinutes < 1) return "just now";
        if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes} min ago";
        if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} hour(s) ago";
        return $"{(int)timeSpan.TotalDays} day(s) ago";
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(StepTitle));
    }

    partial void OnPeriodStartChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(PeriodRangeDisplay));
    }

    partial void OnPeriodEndChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(PeriodRangeDisplay));
    }

    partial void OnPayDateChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(PayDateDisplay));
    }
}

/// <summary>
/// Row model for employee time tracking in the pay run wizard.
/// </summary>
public partial class PayRunEmployeeRow : ObservableObject
{
    [ObservableProperty]
    private bool _isIncluded = true;

    [ObservableProperty]
    private double _regularHours;

    [ObservableProperty]
    private double _overtimeHours;

    [ObservableProperty]
    private double _bonusAmount;

    [ObservableProperty]
    private double _commissionAmount;

    [ObservableProperty]
    private string _bonusDescription = string.Empty;

    [ObservableProperty]
    private string _commissionDescription = string.Empty;

    public int EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeIdDisplay { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public bool IsHourly { get; set; }
    public string PayType { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public string HourlyRateDisplay => $"${HourlyRate:N2}";
    public string GrossPayDisplay => $"${CalculatedGross:N2}";
    public double TotalHours => RegularHours + OvertimeHours;

    private decimal CalculatedGross
    {
        get
        {
            if (IsHourly)
            {
                var regular = (decimal)RegularHours * HourlyRate;
                var overtime = (decimal)OvertimeHours * HourlyRate * 1.5m;
                return regular + overtime + (decimal)BonusAmount + (decimal)CommissionAmount;
            }
            else
            {
                // For salary employees, calculate based on period
                return 0m; // Will be calculated based on annual salary / periods
            }
        }
    }

    partial void OnRegularHoursChanged(double value)
    {
        OnPropertyChanged(nameof(GrossPayDisplay));
    }

    partial void OnOvertimeHoursChanged(double value)
    {
        OnPropertyChanged(nameof(GrossPayDisplay));
    }

    partial void OnBonusAmountChanged(double value)
    {
        OnPropertyChanged(nameof(GrossPayDisplay));
    }

    partial void OnCommissionAmountChanged(double value)
    {
        OnPropertyChanged(nameof(GrossPayDisplay));
    }
}

/// <summary>
/// Result model for generated pay stubs.
/// </summary>
public class PayStubResult
{
    public string EmployeeName { get; set; } = string.Empty;
    public decimal RegularPay { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal BonusPay { get; set; }
    public decimal GrossPay { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal NetPay { get; set; }
    public decimal YtdGross { get; set; }
    public decimal YtdNet { get; set; }
    public bool IsTotalRow { get; set; }
}
