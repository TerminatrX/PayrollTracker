using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
using WinRT.Interop;
using PayrollManager.UI.Utils;

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

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

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
    private void ShowExportMenu()
    {
        // The flyout is handled by XAML, this command just needs to exist for binding
    }

    [RelayCommand]
    private async Task SaveToDesktopAsync()
    {
        if (_payStub == null || _employee == null || _payStub.PayRun == null)
        {
            return;
        }

        try
        {
            // Get desktop folder
            var desktopFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            
            var fileName = $"PayStub_{_employee.LastName}_{_payStub.PayRun.PayDate:yyyyMMdd}.pdf";
            var file = await desktopFolder.CreateFileAsync(fileName, Windows.Storage.CreationCollisionOption.ReplaceExisting);

            // Generate PDF bytes
            var companySettings = await _dbContext.CompanySettings.FirstOrDefaultAsync();
            if (companySettings == null)
            {
                var settingsService = new CompanySettingsService(_dbContext);
                companySettings = await settingsService.GetSettingsAsync();
            }
            
            var pdfBytes = _exportService.GeneratePayStubPdfBytes(_payStub, companySettings);
            
            // Write to file
            await Windows.Storage.FileIO.WriteBytesAsync(file, pdfBytes);
            
            // Show success message
            StatusMessage = $"PDF saved to Desktop: {fileName}";
            System.Diagnostics.Debug.WriteLine($"PDF saved successfully to Desktop: {fileName}");
            _ = ClearStatusMessageAfterDelay();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving PDF: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"PDF export error: {ex.Message}");
            _ = ClearStatusMessageAfterDelay();
        }
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (_payStub == null || _employee == null || _payStub.PayRun == null)
        {
            return;
        }

        try
        {
            // Generate PDF bytes
            var companySettings = await _dbContext.CompanySettings.FirstOrDefaultAsync();
            if (companySettings == null)
            {
                var settingsService = new CompanySettingsService(_dbContext);
                companySettings = await settingsService.GetSettingsAsync();
            }
            
            var pdfBytes = _exportService.GeneratePayStubPdfBytes(_payStub, companySettings);
            
            // Save PDF to temp file
            var tempFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder;
            var tempFile = await tempFolder.CreateFileAsync($"PayStub_{_payStub.Id}.pdf", 
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            await Windows.Storage.FileIO.WriteBytesAsync(tempFile, pdfBytes);
            
            // Launch print dialog
            await Windows.System.Launcher.LaunchFileAsync(tempFile, new Windows.System.LauncherOptions
            {
                DisplayApplicationPicker = false
            });
            
            StatusMessage = "Print dialog opened";
            _ = ClearStatusMessageAfterDelay();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error printing: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Print error: {ex.Message}");
            _ = ClearStatusMessageAfterDelay();
        }
    }

    [RelayCommand]
    private async Task PrintPreviewAsync()
    {
        if (_payStub == null || _employee == null || _payStub.PayRun == null)
        {
            return;
        }

        try
        {
            // Generate PDF bytes
            var companySettings = await _dbContext.CompanySettings.FirstOrDefaultAsync();
            if (companySettings == null)
            {
                var settingsService = new CompanySettingsService(_dbContext);
                companySettings = await settingsService.GetSettingsAsync();
            }
            
            var pdfBytes = _exportService.GeneratePayStubPdfBytes(_payStub, companySettings);
            
            // Save PDF to temp file
            var tempFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder;
            var tempFile = await tempFolder.CreateFileAsync($"PayStub_{_payStub.Id}.pdf", 
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            await Windows.Storage.FileIO.WriteBytesAsync(tempFile, pdfBytes);
            
            // Open PDF in default viewer (which will show print preview)
            await Windows.System.Launcher.LaunchFileAsync(tempFile);
            
            StatusMessage = "Print preview opened";
            _ = ClearStatusMessageAfterDelay();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening print preview: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Print preview error: {ex.Message}");
            _ = ClearStatusMessageAfterDelay();
        }
    }

    private async Task ClearStatusMessageAfterDelay()
    {
        await Task.Delay(5000); // Clear after 5 seconds
        StatusMessage = string.Empty;
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
