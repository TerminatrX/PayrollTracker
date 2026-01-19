using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;

namespace PayrollManager.UI.ViewModels;

public partial class PayRunWizardViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly PayrollService _payrollService;
    
    private int _currentStep = 0;
    private DateTimeOffset _periodStart;
    private DateTimeOffset _periodEnd;
    private DateTimeOffset _payDate;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _isRunComplete;

    // Summary estimates
    private decimal _estimatedGross;
    private decimal _estimatedRegular;
    private decimal _estimatedOvertime;
    private decimal _estimatedBonus;
    private decimal _estimatedTaxes;
    private decimal _estimatedNet;
    private int _includedCount;

    public PayRunWizardViewModel(AppDbContext dbContext, PayrollService payrollService)
    {
        _dbContext = dbContext;
        _payrollService = payrollService;
        
        var today = DateTimeOffset.Now.Date;
        _periodStart = today.AddDays(-13);
        _periodEnd = today;
        _payDate = today.AddDays(1);
    }

    public ObservableCollection<PayRunEmployeeRow> EmployeeRows { get; } = new();
    public ObservableCollection<PayStubResult> GeneratedStubs { get; } = new();

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsStep1));
                OnPropertyChanged(nameof(IsStep2));
                OnPropertyChanged(nameof(IsStep3));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(StepTitle));
            }
        }
    }

    public bool IsStep1 => CurrentStep == 0;
    public bool IsStep2 => CurrentStep == 1;
    public bool IsStep3 => CurrentStep == 2;
    public bool CanGoBack => CurrentStep > 0 && !IsRunComplete;
    public bool CanGoNext => CurrentStep < 2 && !IsRunComplete;

    public string StepTitle => CurrentStep switch
    {
        0 => "Step 1: Select Pay Period",
        1 => "Step 2: Review Employees & Earnings",
        2 => IsRunComplete ? "Pay Run Complete" : "Step 3: Summary & Generate",
        _ => "Pay Run"
    };

    public DateTimeOffset PeriodStart
    {
        get => _periodStart;
        set => SetProperty(ref _periodStart, value);
    }

    public DateTimeOffset PeriodEnd
    {
        get => _periodEnd;
        set => SetProperty(ref _periodEnd, value);
    }

    public DateTimeOffset PayDate
    {
        get => _payDate;
        set => SetProperty(ref _payDate, value);
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

    public bool IsRunComplete
    {
        get => _isRunComplete;
        set
        {
            if (SetProperty(ref _isRunComplete, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(StepTitle));
            }
        }
    }

    public decimal EstimatedGross
    {
        get => _estimatedGross;
        set => SetProperty(ref _estimatedGross, value);
    }

    public decimal EstimatedRegular
    {
        get => _estimatedRegular;
        set => SetProperty(ref _estimatedRegular, value);
    }

    public decimal EstimatedOvertime
    {
        get => _estimatedOvertime;
        set => SetProperty(ref _estimatedOvertime, value);
    }

    public decimal EstimatedBonus
    {
        get => _estimatedBonus;
        set => SetProperty(ref _estimatedBonus, value);
    }

    public decimal EstimatedTaxes
    {
        get => _estimatedTaxes;
        set => SetProperty(ref _estimatedTaxes, value);
    }

    public decimal EstimatedNet
    {
        get => _estimatedNet;
        set => SetProperty(ref _estimatedNet, value);
    }

    public int IncludedCount
    {
        get => _includedCount;
        set => SetProperty(ref _includedCount, value);
    }

    [RelayCommand]
    public async Task InitializeAsync()
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
            EmployeeRows.Add(new PayRunEmployeeRow(employee, defaultHours));
        }
    }

    [RelayCommand]
    public void GoBack()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    public async Task GoNextAsync()
    {
        if (CurrentStep < 2)
        {
            CurrentStep++;
            
            if (CurrentStep == 2)
            {
                await CalculateEstimatesAsync();
            }
        }
    }

    private async Task CalculateEstimatesAsync()
    {
        IsLoading = true;
        StatusMessage = "Calculating estimates...";

        try
        {
            var settings = await _dbContext.CompanySettings.FirstOrDefaultAsync() ?? new CompanySettings();
            var payPeriods = settings.PayPeriodsPerYear > 0 ? settings.PayPeriodsPerYear : 26;

            decimal totalGross = 0;
            decimal totalRegular = 0;
            decimal totalOvertime = 0;
            decimal totalBonus = 0;
            decimal totalTaxes = 0;
            int count = 0;

            foreach (var row in EmployeeRows.Where(r => r.IsIncluded))
            {
                count++;
                var employee = row.Employee;
                decimal gross;

                if (employee.IsHourly)
                {
                    // Regular earnings
                    var regularAmount = (decimal)row.RegularHours * employee.HourlyRate;
                    totalRegular += regularAmount;

                    // Overtime at 1.5x
                    var overtimeRate = employee.HourlyRate * 1.5m;
                    var overtimeAmount = (decimal)row.OvertimeHours * overtimeRate;
                    totalOvertime += overtimeAmount;

                    gross = regularAmount + overtimeAmount;
                }
                else
                {
                    gross = employee.AnnualSalary / payPeriods;
                    totalRegular += gross;
                }

                // Add bonus and commission
                gross += (decimal)row.BonusAmount + (decimal)row.CommissionAmount;
                totalBonus += (decimal)row.BonusAmount + (decimal)row.CommissionAmount;

                var preTax401k = gross * (employee.PreTax401kPercent / 100m);
                var taxable = gross - preTax401k;

                var taxes = taxable * (settings.FederalTaxPercent / 100m) +
                           taxable * (settings.StateTaxPercent / 100m) +
                           taxable * (settings.SocialSecurityPercent / 100m) +
                           taxable * (settings.MedicarePercent / 100m);

                totalGross += gross;
                totalTaxes += taxes;
            }

            EstimatedGross = totalGross;
            EstimatedRegular = totalRegular;
            EstimatedOvertime = totalOvertime;
            EstimatedBonus = totalBonus;
            EstimatedTaxes = totalTaxes;
            EstimatedNet = totalGross - totalTaxes;
            IncludedCount = count;
            StatusMessage = "Estimates calculated";
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

    [RelayCommand]
    public async Task GeneratePayRunAsync()
    {
        IsLoading = true;
        StatusMessage = "Generating pay run...";

        try
        {
            var payRun = new PayRun
            {
                PeriodStart = PeriodStart.DateTime,
                PeriodEnd = PeriodEnd.DateTime,
                PayDate = PayDate.DateTime
            };

            _dbContext.PayRuns.Add(payRun);
            await _dbContext.SaveChangesAsync();

            GeneratedStubs.Clear();
            decimal totalGross = 0, totalTaxes = 0, totalNet = 0;

            var includedRows = EmployeeRows.Where(r => r.IsIncluded).ToList();
            int processed = 0;

            foreach (var row in includedRows)
            {
                processed++;
                StatusMessage = $"Processing {processed}/{includedRows.Count}: {row.Employee.FullName}";

                var input = new PayStubInput
                {
                    RegularHours = (decimal)row.RegularHours,
                    OvertimeHours = (decimal)row.OvertimeHours,
                    BonusAmount = (decimal)row.BonusAmount,
                    CommissionAmount = (decimal)row.CommissionAmount,
                    BonusDescription = !string.IsNullOrEmpty(row.BonusDescription) ? row.BonusDescription : null,
                    CommissionDescription = !string.IsNullOrEmpty(row.CommissionDescription) ? row.CommissionDescription : null
                };

                var payStub = await _payrollService.GeneratePayStubAsync(row.Employee, payRun, input);
                _dbContext.PayStubs.Add(payStub);

                var result = new PayStubResult
                {
                    EmployeeName = row.Employee.FullName,
                    RegularPay = payStub.RegularEarnings,
                    OvertimePay = payStub.OvertimeEarnings,
                    BonusPay = payStub.BonusEarnings + payStub.CommissionEarnings,
                    GrossPay = payStub.GrossPay,
                    TotalTaxes = payStub.TotalTaxes,
                    NetPay = payStub.NetPay,
                    YtdGross = payStub.YtdGross,
                    YtdNet = payStub.YtdNet
                };

                GeneratedStubs.Add(result);
                totalGross += payStub.GrossPay;
                totalTaxes += payStub.TotalTaxes;
                totalNet += payStub.NetPay;
            }

            await _dbContext.SaveChangesAsync();

            // Add totals row
            GeneratedStubs.Add(new PayStubResult
            {
                EmployeeName = "TOTAL",
                RegularPay = GeneratedStubs.Where(s => !s.IsTotalRow).Sum(s => s.RegularPay),
                OvertimePay = GeneratedStubs.Where(s => !s.IsTotalRow).Sum(s => s.OvertimePay),
                BonusPay = GeneratedStubs.Where(s => !s.IsTotalRow).Sum(s => s.BonusPay),
                GrossPay = totalGross,
                TotalTaxes = totalTaxes,
                NetPay = totalNet,
                IsTotalRow = true
            });

            IsRunComplete = true;
            StatusMessage = $"Pay run complete! Generated {includedRows.Count} pay stubs.";
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
    public void StartNewRun()
    {
        IsRunComplete = false;
        CurrentStep = 0;
        GeneratedStubs.Clear();
        _ = InitializeAsync();
    }
}

public partial class PayRunEmployeeRow : ObservableObject
{
    private bool _isIncluded = true;
    private double _regularHours;
    private double _overtimeHours;
    private double _bonusAmount;
    private double _commissionAmount;
    private string _bonusDescription = string.Empty;
    private string _commissionDescription = string.Empty;

    public PayRunEmployeeRow(Employee employee, int defaultHours)
    {
        Employee = employee;
        
        if (employee.IsHourly)
        {
            // Default: up to 40 regular, rest as overtime
            _regularHours = Math.Min(defaultHours, 40);
            _overtimeHours = Math.Max(0, defaultHours - 40);
        }
    }

    public Employee Employee { get; }

    public string FullName => Employee.FullName;
    public bool IsHourly => Employee.IsHourly;
    public string PayType => Employee.IsHourly ? "Hourly" : "Salary";
    public string RateDisplay => Employee.IsHourly
        ? $"{Employee.HourlyRate:C}/hr"
        : $"{Employee.AnnualSalary:C}/yr";
    
    public string OvertimeRateDisplay => Employee.IsHourly
        ? $"{Employee.HourlyRate * 1.5m:C}/hr (1.5x)"
        : "N/A";

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    public double RegularHours
    {
        get => _regularHours;
        set => SetProperty(ref _regularHours, value);
    }

    public double OvertimeHours
    {
        get => _overtimeHours;
        set => SetProperty(ref _overtimeHours, value);
    }

    public double BonusAmount
    {
        get => _bonusAmount;
        set => SetProperty(ref _bonusAmount, value);
    }

    public double CommissionAmount
    {
        get => _commissionAmount;
        set => SetProperty(ref _commissionAmount, value);
    }

    public string BonusDescription
    {
        get => _bonusDescription;
        set => SetProperty(ref _bonusDescription, value);
    }

    public string CommissionDescription
    {
        get => _commissionDescription;
        set => SetProperty(ref _commissionDescription, value);
    }

    // Computed total hours for display
    public double TotalHours => RegularHours + OvertimeHours;
}

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
