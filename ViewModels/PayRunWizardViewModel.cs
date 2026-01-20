using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Text;

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// Represents the steps in the Pay Run Wizard.
/// </summary>
public enum PayRunStep
{
    Dates,      // Step 1: Set pay period dates
    Employees,  // Step 2: Enter employee hours and earnings
    Summary,    // Step 3: Review and finalize
    Complete    // Step 4: Pay run completed
}

/// <summary>
/// ViewModel for the Pay Run Wizard page with stepper navigation.
/// </summary>
public partial class PayRunWizardViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly PayrollService _payrollService;
    private readonly CompanySettingsService _companySettingsService;

    public PayRunWizardViewModel(AppDbContext dbContext, PayrollService payrollService, CompanySettingsService companySettingsService)
    {
        _dbContext = dbContext;
        _payrollService = payrollService;
        _companySettingsService = companySettingsService;
        
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
    private PayRunStep _currentStep = PayRunStep.Dates;

    [ObservableProperty]
    private bool _isStep1Complete;

    [ObservableProperty]
    private bool _isStep2Complete;

    [ObservableProperty]
    private bool _isStep3Complete;

    [ObservableProperty]
    private bool _isRunComplete;

    public bool IsStep1 => CurrentStep == PayRunStep.Dates;
    public bool IsStep2 => CurrentStep == PayRunStep.Employees;
    public bool IsStep3 => CurrentStep == PayRunStep.Summary;
    public bool IsComplete => CurrentStep == PayRunStep.Complete;

    /// <summary>
    /// Can go back if not on Dates step and not complete.
    /// </summary>
    public bool CanGoBack => CurrentStep != PayRunStep.Dates && CurrentStep != PayRunStep.Complete;

    /// <summary>
    /// Can go next if current step is valid and not on Summary or Complete.
    /// </summary>
    public bool CanGoNext => IsCurrentStepValid && CurrentStep != PayRunStep.Summary && CurrentStep != PayRunStep.Complete;

    /// <summary>
    /// Can finalize if on Summary step and all data is valid.
    /// </summary>
    public bool CanFinalize => CurrentStep == PayRunStep.Summary && IsCurrentStepValid;

    /// <summary>
    /// Validates the current step's data.
    /// </summary>
    public bool IsCurrentStepValid => CurrentStep switch
    {
        PayRunStep.Dates => IsDatesStepValid,
        PayRunStep.Employees => IsEmployeesStepValid,
        PayRunStep.Summary => IsSummaryStepValid,
        PayRunStep.Complete => true,
        _ => false
    };

    /// <summary>
    /// Validates Dates step: PeriodStart, PeriodEnd, and PayDate must be set and valid.
    /// </summary>
    private bool IsDatesStepValid
    {
        get
        {
            if (PeriodStart == default || PeriodEnd == default || PayDate == default)
                return false;

            // PeriodEnd must be after PeriodStart
            if (PeriodEnd <= PeriodStart)
                return false;

            // PayDate should be after PeriodEnd
            if (PayDate <= PeriodEnd)
                return false;

            return true;
        }
    }

    /// <summary>
    /// Validates Employees step: At least one employee must be selected.
    /// </summary>
    private bool IsEmployeesStepValid
    {
        get
        {
            return EmployeeRows.Any(r => r.IsIncluded);
        }
    }

    /// <summary>
    /// Validates Summary step: All previous steps must be valid.
    /// </summary>
    private bool IsSummaryStepValid
    {
        get
        {
            return IsDatesStepValid && IsEmployeesStepValid;
        }
    }

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
    private decimal _estimatedDeductions;

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

    [ObservableProperty]
    private bool _includeInactive = false;

    public int EmployeeCount => EmployeeRows.Count;
    public string AutoSaveStatus => IsDraft && DraftSavedAt.HasValue 
        ? $"Auto-saved {GetTimeAgo(DraftSavedAt.Value)}" 
        : "Not saved";

    public string TotalGrossDisplay => $"${EstimatedGross:N2}";
    public string EstimatedTaxesDisplay => $"${EstimatedTaxes:N2}";
    public string BenefitsDeductionsDisplay => $"${EstimatedDeductions:N2}";
    public string NetPayTotalDisplay => $"${EstimatedNet:N2}";

    public string StepTitle => CurrentStep switch
    {
        PayRunStep.Dates => "Step 1: Select Pay Period",
        PayRunStep.Employees => "Step 2: Review Employees & Earnings",
        PayRunStep.Summary => IsRunComplete ? "Pay Run Complete" : "Step 3: Summary & Generate",
        PayRunStep.Complete => "Pay Run Complete",
        _ => "Pay Run"
    };

    // ═══════════════════════════════════════════════════════════════
    // STEP 1 PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    public SolidColorBrush Step1Background => GetStepBackground(0);
    public SolidColorBrush Step1BorderBrush => GetStepBorderBrush(0);
    public string Step1Icon => GetStepIcon(0);
    public SolidColorBrush Step1Foreground => GetStepForeground(0);
    public SolidColorBrush Step1ConnectorColor => GetStepConnectorColor(0);
    public FontWeight Step1FontWeight => GetStepFontWeight(0);
    public SolidColorBrush Step1TextColor => GetStepTextColor(0);
    public string Step1Status => GetStepStatus(0);

    // ═══════════════════════════════════════════════════════════════
    // STEP 2 PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    public SolidColorBrush Step2Background => GetStepBackground(1);
    public SolidColorBrush Step2BorderBrush => GetStepBorderBrush(1);
    public string Step2Icon => GetStepIcon(1);
    public SolidColorBrush Step2Foreground => GetStepForeground(1);
    public SolidColorBrush Step2ConnectorColor => GetStepConnectorColor(1);
    public FontWeight Step2FontWeight => GetStepFontWeight(1);
    public SolidColorBrush Step2TextColor => GetStepTextColor(1);
    public string Step2Status => GetStepStatus(1);

    // ═══════════════════════════════════════════════════════════════
    // STEP 3 PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    public SolidColorBrush Step3Background => GetStepBackground(2);
    public SolidColorBrush Step3BorderBrush => GetStepBorderBrush(2);
    public string Step3Icon => GetStepIcon(2);
    public SolidColorBrush Step3Foreground => GetStepForeground(2);
    public FontWeight Step3FontWeight => GetStepFontWeight(2);
    public SolidColorBrush Step3TextColor => GetStepTextColor(2);
    public string Step3Status => GetStepStatus(2);

    // ═══════════════════════════════════════════════════════════════
    // STEP STYLING HELPERS
    // ═══════════════════════════════════════════════════════════════

    private SolidColorBrush GetStepBackground(int stepIndex)
    {
        var step = (PayRunStep)stepIndex;
        var isComplete = step switch
        {
            PayRunStep.Dates => IsStep1Complete,
            PayRunStep.Employees => IsStep2Complete,
            PayRunStep.Summary => IsStep3Complete,
            PayRunStep.Complete => IsRunComplete,
            _ => false
        };

        if (isComplete || (int)step < (int)CurrentStep)
        {
            // Complete - green background
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94)); // Success green
        }
        else if (step == CurrentStep)
        {
            // Current - accent/primary background
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 140, 238)); // Primary blue
        }
        else
        {
            // Not started - gray background
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128)); // Gray
        }
    }

    private SolidColorBrush GetStepBorderBrush(int stepIndex)
    {
        var step = (PayRunStep)stepIndex;
        var isComplete = step switch
        {
            PayRunStep.Dates => IsStep1Complete,
            PayRunStep.Employees => IsStep2Complete,
            PayRunStep.Summary => IsStep3Complete,
            PayRunStep.Complete => IsRunComplete,
            _ => false
        };

        if (isComplete || (int)step < (int)CurrentStep)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 163, 74)); // Darker green
        }
        else if (step == CurrentStep)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 123, 214)); // Darker blue
        }
        else
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 75, 85, 99)); // Darker gray
        }
    }

    private string GetStepIcon(int stepIndex)
    {
        var step = (PayRunStep)stepIndex;
        var isComplete = step switch
        {
            PayRunStep.Dates => IsStep1Complete,
            PayRunStep.Employees => IsStep2Complete,
            PayRunStep.Summary => IsStep3Complete,
            PayRunStep.Complete => IsRunComplete,
            _ => false
        };

        if (isComplete || (int)step < (int)CurrentStep)
        {
            return "\uE73E"; // Checkmark icon
        }
        else if (step == CurrentStep)
        {
            return step switch
            {
                PayRunStep.Dates => "\uE787", // Calendar icon
                PayRunStep.Employees => "\uE8A5", // Edit icon
                PayRunStep.Summary => "\uE8B8", // Review icon
                PayRunStep.Complete => "\uE73E", // Checkmark icon
                _ => "\uE713"  // Circle icon
            };
        }
        else
        {
            return "\uE76C"; // Circle icon (empty)
        }
    }

    private SolidColorBrush GetStepForeground(int stepIndex)
    {
        // Icon foreground is always white for visibility
        return new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    private SolidColorBrush GetStepConnectorColor(int stepIndex)
    {
        // Connector shows progress - green if next step is complete or current
        var step = (PayRunStep)stepIndex;
        
        if (step == PayRunStep.Dates)
        {
            // Dates connector - green if Dates step is complete or we're past it
            if (IsStep1Complete || CurrentStep != PayRunStep.Dates)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94));
        }
        else if (step == PayRunStep.Employees)
        {
            // Employees connector - green if Employees step is complete or we're past it
            if (IsStep2Complete || (int)CurrentStep > (int)PayRunStep.Employees)
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94));
        }
        
        // Default gray connector
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 107, 114, 128));
    }

    private FontWeight GetStepFontWeight(int stepIndex)
    {
        var step = (PayRunStep)stepIndex;
        if (step == CurrentStep)
        {
            return FontWeights.Bold;
        }
        return FontWeights.Normal;
    }

    private SolidColorBrush GetStepTextColor(int stepIndex)
    {
        var step = (PayRunStep)stepIndex;
        var isComplete = step switch
        {
            PayRunStep.Dates => IsStep1Complete,
            PayRunStep.Employees => IsStep2Complete,
            PayRunStep.Summary => IsStep3Complete,
            PayRunStep.Complete => IsRunComplete,
            _ => false
        };

        if (isComplete || (int)step < (int)CurrentStep)
        {
            // Complete - success green text
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94));
        }
        else if (step == CurrentStep)
        {
            // Current - primary/accent text
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 140, 238));
        }
        else
        {
            // Not started - gray text
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 122, 132, 148));
        }
    }

    private string GetStepStatus(int stepIndex)
    {
        var step = (PayRunStep)stepIndex;
        var isComplete = step switch
        {
            PayRunStep.Dates => IsStep1Complete,
            PayRunStep.Employees => IsStep2Complete,
            PayRunStep.Summary => IsStep3Complete,
            PayRunStep.Complete => IsRunComplete,
            _ => false
        };

        if (isComplete || (int)step < (int)CurrentStep)
        {
            return "Complete";
        }
        else if (step == CurrentStep)
        {
            return "In progress";
        }
        else
        {
            return "Not started";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading...";

        try
        {
            // Load default dates from last pay run using PayPeriodCalculator
            var lastPayRun = await _dbContext.PayRuns
                .OrderByDescending(p => p.PayDate)
                .FirstOrDefaultAsync();

            // Get company settings to determine pay frequency
            var companySettings = await _companySettingsService.GetSettingsAsync();
            var payFrequency = PayPeriodCalculator.GetPayFrequency(companySettings.PayPeriodsPerYear);

            // Calculate next period using PayPeriodCalculator
            var nextPeriod = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, payFrequency);

            PeriodStart = new DateTimeOffset(nextPeriod.PeriodStart);
            PeriodEnd = new DateTimeOffset(nextPeriod.PeriodEnd);
            PayDate = new DateTimeOffset(nextPeriod.PayDate);

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
        
        var settings = await _companySettingsService.GetSettingsAsync();
        var defaultHours = settings.DefaultHoursPerPeriod;

        // Filter by IsActive unless IncludeInactive is true
        var employeesQuery = _dbContext.Employees.AsQueryable();
        
        if (!IncludeInactive)
        {
            employeesQuery = employeesQuery.Where(e => e.IsActive);
        }

        var employees = await employeesQuery
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
                HourlyRate = employee.HourlyRate,
                IsActive = employee.IsActive
            };

            if (employee.IsHourly)
            {
                row.RegularHours = Math.Min(defaultHours, 40);
                row.OvertimeHours = Math.Max(0, defaultHours - 40);
            }

            // Subscribe to property changes to update validation and estimates
            row.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PayRunEmployeeRow.IsIncluded))
                {
                    OnEmployeeSelectionChanged();
                }
                else if (CurrentStep == PayRunStep.Summary &&
                         (e.PropertyName == nameof(PayRunEmployeeRow.RegularHours) ||
                          e.PropertyName == nameof(PayRunEmployeeRow.OvertimeHours) ||
                          e.PropertyName == nameof(PayRunEmployeeRow.BonusAmount) ||
                          e.PropertyName == nameof(PayRunEmployeeRow.CommissionAmount)))
                {
                    // Recalculate estimates when hours/earnings change on Summary step
                    _ = CalculateEstimatesAsync();
                }
            };

            EmployeeRows.Add(row);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        // Clear any validation messages
        StatusMessage = string.Empty;

        // Never go before Dates step - state is preserved in properties
        if (CurrentStep == PayRunStep.Employees)
        {
            CurrentStep = PayRunStep.Dates;
        }
        else if (CurrentStep == PayRunStep.Summary)
        {
            CurrentStep = PayRunStep.Employees;
        }
        
        // Update command states
        GoBackCommand.NotifyCanExecuteChanged();
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void GoNext()
    {
        // Validate current step before proceeding
        if (!IsCurrentStepValid)
        {
            StatusMessage = GetValidationErrorMessage();
            return;
        }

        // Clear any previous validation messages
        StatusMessage = string.Empty;

        // Mark current step as complete
        if (CurrentStep == PayRunStep.Dates)
        {
            IsStep1Complete = true;
            CurrentStep = PayRunStep.Employees;
            // Load employees only if not already loaded (preserve state when navigating back)
            if (EmployeeRows.Count == 0)
            {
                _ = LoadEmployeesAsync();
            }
        }
        else if (CurrentStep == PayRunStep.Employees)
        {
            IsStep2Complete = true;
            CurrentStep = PayRunStep.Summary;
            // Recalculate estimates when entering Summary using PreviewPayStub
            _ = CalculateEstimatesAsync();
        }
        
        // Update command states
        GoBackCommand.NotifyCanExecuteChanged();
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanFinalize))]
    private async Task FinalizeAsync()
    {
        // Finalize is the same as GeneratePayRun, but only enabled on Summary step
        await GeneratePayRunAsync();
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
        // Validate all steps before generating
        if (!IsSummaryStepValid)
        {
            StatusMessage = GetValidationErrorMessage();
            return;
        }

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
                    EmployeeId = employee.Id,
                    EmployeeName = employee.FullName,
                    GrossPay = payStub.GrossPay,
                    NetPay = payStub.NetPay,
                    TotalTaxes = payStub.TotalTaxes,
                    Taxes = payStub.TotalTaxes
                });

                processedCount++;
                StatusMessage = $"Processing {processedCount} of {totalEmployees} employees...";
            }

            // Save all pay stubs
            await _dbContext.SaveChangesAsync();

            IsStep3Complete = true;
            IsRunComplete = true;
            CurrentStep = PayRunStep.Complete;
            StatusMessage = $"Pay run generated successfully: {processedCount} pay stubs created";
            
            // Update command states
            GoBackCommand.NotifyCanExecuteChanged();
            GoNextCommand.NotifyCanExecuteChanged();
            FinalizeCommand.NotifyCanExecuteChanged();
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

    /// <summary>
    /// Gets a validation error message for the current step.
    /// </summary>
    private string GetValidationErrorMessage()
    {
        return CurrentStep switch
        {
            PayRunStep.Dates => "Please set valid Period Start, Period End, and Pay Date before continuing.",
            PayRunStep.Employees => "Please select at least one employee to include in the pay run.",
            PayRunStep.Summary => "Please ensure all required information is complete.",
            _ => "Please complete the current step before continuing."
        };
    }

    [RelayCommand]
    private void StartNewRun()
    {
        IsRunComplete = false;
        IsDraft = false;
        CurrentStep = PayRunStep.Dates;
        IsStep1Complete = false;
        IsStep2Complete = false;
        IsStep3Complete = false;
        GeneratedStubs.Clear();
        
        // Update command states
        GoBackCommand.NotifyCanExecuteChanged();
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
        
        InitializeCommand.Execute(null);
    }

    [RelayCommand]
    public async Task CalculateEstimatesAsync()
    {
        // Use PreviewPayStub to get accurate calculations matching final pay stubs
        decimal totalGross = 0;
        decimal totalTaxes = 0;
        decimal totalNet = 0;
        decimal totalDeductions = 0;
        decimal totalRegular = 0;
        decimal totalOvertime = 0;
        decimal totalBonus = 0;
        int count = 0;

        // Create draft pay run from current period dates
        var draft = new PayRunDraft
        {
            PeriodStart = PeriodStart.DateTime,
            PeriodEnd = PeriodEnd.DateTime,
            PayDate = PayDate.DateTime
        };

        foreach (var row in EmployeeRows.Where(r => r.IsIncluded))
        {
            var employee = await _dbContext.Employees.FindAsync(row.EmployeeId);
            if (employee == null)
                continue;

            count++;

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

            // Use PreviewPayStub to get accurate calculations
            var preview = await _payrollService.PreviewPayStubAsync(employee, draft, input);

            totalGross += preview.GrossPay;
            totalTaxes += preview.TotalTaxes;
            totalNet += preview.NetPay;
            totalDeductions += preview.PreTax401kDeduction + preview.PostTaxDeductions;

            // Calculate breakdown for display
            if (row.IsHourly)
            {
                var regular = (decimal)row.RegularHours * row.HourlyRate;
                var overtime = (decimal)row.OvertimeHours * row.HourlyRate * 1.5m;
                totalRegular += regular;
                totalOvertime += overtime;
            }
            totalBonus += (decimal)row.BonusAmount + (decimal)row.CommissionAmount;
        }

        EstimatedGross = totalGross;
        EstimatedRegular = totalRegular;
        EstimatedOvertime = totalOvertime;
        EstimatedBonus = totalBonus;
        EstimatedTaxes = totalTaxes; // Now uses actual calculated taxes
        EstimatedDeductions = totalDeductions; // Now uses actual calculated deductions
        EstimatedNet = totalNet; // Now uses actual calculated net pay
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

    partial void OnCurrentStepChanged(PayRunStep value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanFinalize));
        OnPropertyChanged(nameof(IsCurrentStepValid));
        OnPropertyChanged(nameof(StepTitle));
        
        // Notify commands to update their CanExecute state
        GoBackCommand.NotifyCanExecuteChanged();
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
        
        // Update step styling properties
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step1BorderBrush));
        OnPropertyChanged(nameof(Step1Icon));
        OnPropertyChanged(nameof(Step1Foreground));
        OnPropertyChanged(nameof(Step1ConnectorColor));
        OnPropertyChanged(nameof(Step1FontWeight));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step1Status));
        
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step2BorderBrush));
        OnPropertyChanged(nameof(Step2Icon));
        OnPropertyChanged(nameof(Step2Foreground));
        OnPropertyChanged(nameof(Step2ConnectorColor));
        OnPropertyChanged(nameof(Step2FontWeight));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step2Status));
        
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step3BorderBrush));
        OnPropertyChanged(nameof(Step3Icon));
        OnPropertyChanged(nameof(Step3Foreground));
        OnPropertyChanged(nameof(Step3FontWeight));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step3Status));
    }

    partial void OnIsStep1CompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(Step1Background));
        OnPropertyChanged(nameof(Step1BorderBrush));
        OnPropertyChanged(nameof(Step1Icon));
        OnPropertyChanged(nameof(Step1ConnectorColor));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step1Status));
        OnPropertyChanged(nameof(Step2ConnectorColor));
    }

    partial void OnIsStep2CompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(Step2Background));
        OnPropertyChanged(nameof(Step2BorderBrush));
        OnPropertyChanged(nameof(Step2Icon));
        OnPropertyChanged(nameof(Step2ConnectorColor));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step2Status));
    }

    partial void OnIsStep3CompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(Step3Background));
        OnPropertyChanged(nameof(Step3BorderBrush));
        OnPropertyChanged(nameof(Step3Icon));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step3Status));
    }

    partial void OnPeriodStartChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(PeriodRangeDisplay));
        OnPropertyChanged(nameof(IsCurrentStepValid));
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnPeriodEndChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(PeriodRangeDisplay));
        OnPropertyChanged(nameof(IsCurrentStepValid));
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnPayDateChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(PayDateDisplay));
        OnPropertyChanged(nameof(IsCurrentStepValid));
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIncludeInactiveChanged(bool value)
    {
        // Reload employees when toggle changes
        _ = LoadEmployeesAsync();
    }

    /// <summary>
    /// Called when employee selection changes to update validation.
    /// </summary>
    private void OnEmployeeSelectionChanged()
    {
        OnPropertyChanged(nameof(IsCurrentStepValid));
        GoNextCommand.NotifyCanExecuteChanged();
        FinalizeCommand.NotifyCanExecuteChanged();
        
        // Recalculate estimates if we're on Summary step
        if (CurrentStep == PayRunStep.Summary)
        {
            _ = CalculateEstimatesAsync();
        }
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
    public bool IsActive { get; set; } = true;
    public string HourlyRateDisplay => $"${HourlyRate:N2}";
    public string GrossPayDisplay => $"${CalculatedGross:N2}";
    public double TotalHours => RegularHours + OvertimeHours;
    
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FullName))
                return "??";
            
            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
            if (parts.Length == 1 && parts[0].Length >= 2)
                return parts[0].Substring(0, 2).ToUpperInvariant();
            return "??";
        }
    }
    
    public SolidColorBrush InitialsBackground
    {
        get
        {
            // Generate a consistent color based on the name
            var hash = FullName.GetHashCode();
            var colors = new[]
            {
                Windows.UI.Color.FromArgb(255, 43, 140, 238),   // Blue
                Windows.UI.Color.FromArgb(255, 34, 197, 94),    // Green
                Windows.UI.Color.FromArgb(255, 249, 115, 22),   // Orange
                Windows.UI.Color.FromArgb(255, 168, 85, 247),   // Purple
                Windows.UI.Color.FromArgb(255, 236, 72, 153),  // Pink
                Windows.UI.Color.FromArgb(255, 14, 165, 233),  // Cyan
                Windows.UI.Color.FromArgb(255, 251, 191, 36),  // Yellow
                Windows.UI.Color.FromArgb(255, 239, 68, 68),   // Red
            };
            var color = colors[Math.Abs(hash) % colors.Length];
            return new SolidColorBrush(color);
        }
    }
    
    public SolidColorBrush InitialsForeground => new SolidColorBrush(Microsoft.UI.Colors.White);
    
    public SolidColorBrush OvertimeColor => OvertimeHours > 0 
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 249, 115, 22)) // Orange for overtime
        : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

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
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal RegularPay { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal BonusPay { get; set; }
    public decimal GrossPay { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal Taxes { get; set; } // Alias for TotalTaxes for compatibility
    public decimal NetPay { get; set; }
    public decimal YtdGross { get; set; }
    public decimal YtdNet { get; set; }
    public bool IsTotalRow { get; set; }
}
