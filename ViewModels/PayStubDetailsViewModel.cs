using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;

// NOTE: This ViewModel only displays stored PayStub values from the database.
// It NEVER recalculates or regenerates pay stub values using PayrollService.
// All values are read directly from the PayStub entity and its related collections.

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// ViewModel for PayStubDetailsPage that wraps a PayStub and exposes all fields as bindable properties.
/// </summary>
public partial class PayStubDetailsViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly ExportService _exportService;
    private PayStub? _payStub;
    private Employee? _employee;

    public PayStubDetailsViewModel(AppDbContext dbContext, ExportService exportService)
    {
        _dbContext = dbContext;
        _exportService = exportService;
    }

    // ═══════════════════════════════════════════════════════════════
    // HEADER INFORMATION
    // ═══════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _employeeName = string.Empty;

    [ObservableProperty]
    private string _payPeriodText = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // PAY AMOUNTS - Direct bindings to PayStub
    // ═══════════════════════════════════════════════════════════

    [ObservableProperty]
    private decimal _grossPay;

    [ObservableProperty]
    private decimal _preTax401k;

    [ObservableProperty]
    private decimal _taxFederal;

    [ObservableProperty]
    private decimal _taxState;

    [ObservableProperty]
    private decimal _taxSocialSecurity;

    [ObservableProperty]
    private decimal _taxMedicare;

    [ObservableProperty]
    private decimal _totalTaxes;

    [ObservableProperty]
    private decimal _healthInsurance;

    [ObservableProperty]
    private decimal _otherDeductions;

    [ObservableProperty]
    private decimal _totalDeductions;

    [ObservableProperty]
    private decimal _netPay;

    // ═══════════════════════════════════════════════════════════════
    // YTD AMOUNTS
    // ═══════════════════════════════════════════════════════════

    [ObservableProperty]
    private decimal _ytdGross;

    [ObservableProperty]
    private decimal _ytdTaxes;

    [ObservableProperty]
    private decimal _ytdDeductions;

    [ObservableProperty]
    private decimal _ytdNet;

    // ═══════════════════════════════════════════════════════════════
    // EARNINGS BREAKDOWN
    // ═══════════════════════════════════════════════════════════

    [ObservableProperty]
    private decimal _regularEarnings;

    [ObservableProperty]
    private decimal _overtimeEarnings;

    [ObservableProperty]
    private decimal _bonusEarnings;

    [ObservableProperty]
    private decimal _commissionEarnings;

    // ═══════════════════════════════════════════════════════════════
    // EARNING LINES
    // ═══════════════════════════════════════════════════════════

    public ObservableCollection<EarningLineDisplay> EarningLines { get; } = new();

    // ═══════════════════════════════════════════════════════════════
    // DEBUG MODE (only functional in Debug builds)
    // ═══════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isDebugModeEnabled;

    [ObservableProperty]
    private string _debugInfo = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════

    public event EventHandler? NavigateBackRequested;

    [RelayCommand]
    private void NavigateBack()
    {
        NavigateBackRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DownloadCsvAsync()
    {
        if (_payStub == null)
        {
            return;
        }

        try
        {
            var filePath = await _exportService.ExportPayStubToCsvAsync(_payStub.Id);
            // Could show a message here if needed
        }
        catch (Exception ex)
        {
            // Handle error
            System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DownloadPdfAsync()
    {
        if (_payStub == null)
        {
            return;
        }

        try
        {
            var filePath = await _exportService.ExportPayStubToPdfAsync(_payStub.Id);
            // Could show a message here if needed
        }
        catch (Exception ex)
        {
            // Handle error
            System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // LOAD METHODS
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Loads pay stub from a PayStubNavigationParameter (used when navigating from PayRunsPage or Employee pages).
    /// </summary>
    public void LoadPayStub(PayStub payStub, Employee employee)
    {
        _payStub = payStub;
        _employee = employee;
        PopulateFromPayStub();
    }

    /// <summary>
    /// Loads pay stub by ID (used when navigating with just a pay stub ID).
    /// </summary>
    public async Task LoadPayStubByIdAsync(int payStubId)
    {
        var payStub = await _dbContext.PayStubs
            .Include(ps => ps.Employee)
            .Include(ps => ps.PayRun)
            .Include(ps => ps.EarningLines)
            .Include(ps => ps.DeductionLines)
            .Include(ps => ps.TaxLines)
            .FirstOrDefaultAsync(ps => ps.Id == payStubId);

        if (payStub != null && payStub.Employee != null)
        {
            _payStub = payStub;
            _employee = payStub.Employee;
            PopulateFromPayStub();
        }
    }

    private void PopulateFromPayStub()
    {
        if (_payStub == null || _employee == null) return;

        // Header
        EmployeeName = _employee.FullName;
        PayPeriodText = _payStub.PayRun != null
            ? $"Pay Period: {_payStub.PayRun.PeriodStart:MMM dd} - {_payStub.PayRun.PeriodEnd:MMM dd, yyyy} | Pay Date: {_payStub.PayRun.PayDate:MMM dd, yyyy}"
            : "Pay Period: N/A";

        // Pay amounts - direct bindings (negate taxes and deductions for display)
        GrossPay = _payStub.GrossPay;
        PreTax401k = -_payStub.PreTax401kDeduction;
        TaxFederal = -_payStub.TaxFederal;
        TaxState = -_payStub.TaxState;
        TaxSocialSecurity = -_payStub.TaxSocialSecurity;
        TaxMedicare = -_payStub.TaxMedicare;
        TotalTaxes = -_payStub.TotalTaxes;
        HealthInsurance = -_employee.HealthInsurancePerPeriod;
        OtherDeductions = -_employee.OtherDeductionsPerPeriod;
        TotalDeductions = -_payStub.PostTaxDeductions;
        NetPay = _payStub.NetPay;

        // YTD amounts
        YtdGross = _payStub.YtdGross;
        YtdTaxes = _payStub.YtdTaxes;
        YtdDeductions = _payStub.YtdGross - _payStub.YtdNet - _payStub.YtdTaxes;
        YtdNet = _payStub.YtdNet;

        // Earnings breakdown
        RegularEarnings = _payStub.RegularEarnings;
        OvertimeEarnings = _payStub.OvertimeEarnings;
        BonusEarnings = _payStub.BonusEarnings;
        CommissionEarnings = _payStub.CommissionEarnings;

        // Load earning lines
        EarningLines.Clear();
        foreach (var line in _payStub.EarningLines.OrderBy(e => e.Type))
        {
            EarningLines.Add(new EarningLineDisplay(line));
        }

        // If no earning lines exist (legacy data), create a synthetic one
        if (EarningLines.Count == 0)
        {
            EarningLines.Add(new EarningLineDisplay
            {
                TypeDisplay = _employee.IsHourly ? "Regular" : "Salary",
                Description = _employee.IsHourly ? "Regular Pay" : "Salary",
                Hours = _payStub.HoursWorked,
                Rate = _employee.IsHourly ? _employee.HourlyRate : _payStub.GrossPay,
                Amount = _payStub.GrossPay
            });
        }

#if DEBUG
        UpdateDebugInfo();
#else
        // In Release builds, debug info is always empty
        DebugInfo = string.Empty;
        IsDebugModeEnabled = false;
#endif
    }

#if DEBUG
    partial void OnIsDebugModeEnabledChanged(bool value)
    {
        if (value)
        {
            UpdateDebugInfo();
        }
        else
        {
            DebugInfo = string.Empty;
        }
    }

    private void UpdateDebugInfo()
    {
        if (_payStub == null || _employee == null)
        {
            DebugInfo = "No pay stub loaded";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"PayStub ID: {_payStub.Id}");
        sb.AppendLine($"Employee ID: {_employee.Id}");
        sb.AppendLine($"PayRun ID: {_payStub.PayRunId}");
        sb.AppendLine();
        sb.AppendLine("Raw Values:");
        sb.AppendLine($"  GrossPay: {_payStub.GrossPay:N2}");
        sb.AppendLine($"  PreTax401k: {_payStub.PreTax401kDeduction:N2}");
        sb.AppendLine($"  TaxFederal: {_payStub.TaxFederal:N2}");
        sb.AppendLine($"  TaxState: {_payStub.TaxState:N2}");
        sb.AppendLine($"  TaxSocialSecurity: {_payStub.TaxSocialSecurity:N2}");
        sb.AppendLine($"  TaxMedicare: {_payStub.TaxMedicare:N2}");
        sb.AppendLine($"  PostTaxDeductions: {_payStub.PostTaxDeductions:N2}");
        sb.AppendLine($"  NetPay: {_payStub.NetPay:N2}");
        sb.AppendLine($"  YtdGross: {_payStub.YtdGross:N2}");
        sb.AppendLine($"  YtdTaxes: {_payStub.YtdTaxes:N2}");
        sb.AppendLine($"  YtdNet: {_payStub.YtdNet:N2}");
        sb.AppendLine();
        sb.AppendLine($"EarningLines Count: {_payStub.EarningLines.Count}");
        sb.AppendLine($"DeductionLines Count: {_payStub.DeductionLines.Count}");
        sb.AppendLine($"TaxLines Count: {_payStub.TaxLines.Count}");

        DebugInfo = sb.ToString();
    }
#else
    // In Release builds, these are no-ops
    partial void OnIsDebugModeEnabledChanged(bool value) { }
#endif
}

/// <summary>
/// Display model for earning lines in the DataGrid.
/// </summary>
public class EarningLineDisplay
{
    public EarningLineDisplay() { }

    public EarningLineDisplay(EarningLine line)
    {
        TypeDisplay = line.Type.ToString();
        Description = line.Description;
        Hours = line.Hours;
        Rate = line.Rate;
        Amount = line.Amount;
    }

    public string TypeDisplay { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    public string HoursDisplay => Hours > 0 ? Hours.ToString("F2") : "—";
    public string RateDisplay => Rate.ToString("C2");
    public string AmountDisplay => Amount.ToString("C2");
    public bool IsTotalRow { get; set; }
}
