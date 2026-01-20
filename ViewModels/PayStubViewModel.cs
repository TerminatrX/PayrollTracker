using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// ViewModel for displaying pay stub details including earnings, deductions, and taxes.
/// </summary>
public partial class PayStubViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly ExportService _exportService;

    public PayStubViewModel(AppDbContext dbContext, ExportService exportService)
    {
        _dbContext = dbContext;
        _exportService = exportService;
        
        // Initialize years
        for (int y = DateTime.Now.Year; y >= DateTime.Now.Year - 5; y--)
        {
            AvailableYears.Add(y);
        }
        
        // Initialize to list mode (no pay stub selected)
        PayStubId = 0;
        SelectedPayStub = null;
        
        // Load pay stubs on initialization
        _ = LoadPayStubsAsync();
    }
    // ═══════════════════════════════════════════════════════════════
    // COLLECTIONS
    // ═══════════════════════════════════════════════════════════════

    public ObservableCollection<PayStubListItem> PayStubs { get; } = new();
    public ObservableCollection<PayStubListItem> FilteredPayStubs { get; } = new();
    public ObservableCollection<FinancialLineItem> EarningLines { get; } = new();
    public ObservableCollection<FinancialLineItem> DeductionLines { get; } = new();
    public ObservableCollection<FinancialLineItem> TaxLines { get; } = new();

    // ═══════════════════════════════════════════════════════════════
    // SELECTION
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private PayStubListItem? _selectedPayStub;

    [ObservableProperty]
    private int _payStubId;
    
    public bool HasSelectedPayStub => SelectedPayStub != null && PayStubId > 0;

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
    private int _yearFilter = DateTime.Now.Year;

    [ObservableProperty]
    private int _periodFilterIndex = 0; // 0=All, 1=Q1, 2=Q2, 3=Q3, 4=Q4

    [ObservableProperty]
    private string? _selectedEmployeeFilter;

    public ObservableCollection<int> AvailableYears { get; } = new();
    public ObservableCollection<string> EmployeeNames { get; } = new();

    // ═══════════════════════════════════════════════════════════════
    // PAY STUB DETAILS
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _statementNumber = "#00000";

    [ObservableProperty]
    private string _periodRange = "Jan 01 - Jan 15, 2024";

    [ObservableProperty]
    private string _payDate = "Jan 20, 2024";

    // Company Info
    [ObservableProperty]
    private string _companyName = "TechFlow Solutions Inc.";

    [ObservableProperty]
    private string _companyAddress = "123 Enterprise Way, Suite 500\nSeattle, WA 98101";

    [ObservableProperty]
    private string _companyEin = "12-3456789";

    [ObservableProperty]
    private string _companyCopyright = "© 2024 TechFlow Solutions Inc.";

    // Employee Info
    [ObservableProperty]
    private string _employeeName = "John Doe";

    [ObservableProperty]
    private string _employeeId = "#EMP-00001";

    [ObservableProperty]
    private string _taxStatus = "Single / 01";

    // Pay Amounts
    [ObservableProperty]
    private decimal _grossPay;

    [ObservableProperty]
    private decimal _netPay;

    [ObservableProperty]
    private decimal _totalGross;

    [ObservableProperty]
    private decimal _totalDeductions;

    [ObservableProperty]
    private decimal _totalTaxes;

    // YTD Amounts
    [ObservableProperty]
    private decimal _ytdGross;

    [ObservableProperty]
    private decimal _ytdDeductions;

    [ObservableProperty]
    private decimal _ytdTaxes;

    [ObservableProperty]
    private decimal _ytdNetPay;

    // ═══════════════════════════════════════════════════════════════
    // STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task LoadPayStubsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading pay stubs...";

        try
        {
            PayStubs.Clear();
            var payStubs = await _dbContext.PayStubs
                .Include(ps => ps.Employee)
                .Include(ps => ps.PayRun)
                .OrderByDescending(ps => ps.PayRun!.PayDate)
                .ToListAsync();

            foreach (var payStub in payStubs)
            {
                PayStubs.Add(new PayStubListItem
                {
                    Id = payStub.Id,
                    StatementNumber = $"#{payStub.Id:D5}",
                    EmployeeName = payStub.Employee?.FullName ?? "Unknown",
                    PeriodRange = payStub.PayRun != null
                        ? $"{payStub.PayRun.PeriodStart:MMM dd} - {payStub.PayRun.PeriodEnd:MMM dd, yyyy}"
                        : "N/A",
                    PayDate = payStub.PayRun?.PayDate ?? DateTime.MinValue,
                    GrossPay = payStub.GrossPay,
                    NetPay = payStub.NetPay
                });
            }

            ApplyFiltersCommand.Execute(null);
            
            // Update year filter to show the most recent year if current year has no pay stubs
            if (payStubs.Count > 0 && FilteredPayStubs.Count == 0)
            {
                var mostRecentYear = payStubs.Max(ps => ps.PayRun?.PayDate.Year ?? 0);
                if (mostRecentYear > 0 && mostRecentYear != YearFilter)
                {
                    // If the most recent pay stub is from a different year, update the filter
                    YearFilter = mostRecentYear;
                    ApplyFiltersCommand.Execute(null);
                }
            }
            StatusMessage = $"Loaded {payStubs.Count} pay stubs";
            
            // If no pay stub is selected, ensure we're in list mode
            if (SelectedPayStub == null && PayStubId == 0)
            {
                OnPropertyChanged(nameof(HasSelectedPayStub));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading pay stubs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadPayStubAsync(int payStubId)
    {
        PayStubId = payStubId;
        IsLoading = true;
        StatusMessage = "Loading pay stub...";

        try
        {
            var payStub = await _dbContext.PayStubs
                .Include(ps => ps.Employee)
                .Include(ps => ps.PayRun)
                .Include(ps => ps.EarningLines)
                .Include(ps => ps.DeductionLines)
                .Include(ps => ps.TaxLines)
                .FirstOrDefaultAsync(ps => ps.Id == payStubId);

            if (payStub != null)
            {
                PopulateFromPayStub(payStub);
                OnPropertyChanged(nameof(HasSelectedPayStub));
            }
            else
            {
                StatusMessage = "Pay stub not found";
                PayStubId = 0;
                OnPropertyChanged(nameof(HasSelectedPayStub));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading pay stub: {ex.Message}";
            PayStubId = 0;
            OnPropertyChanged(nameof(HasSelectedPayStub));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateFromPayStub(PayStub payStub)
    {
        StatementNumber = $"#{payStub.Id:D5}";
        PeriodRange = payStub.PayRun != null
            ? $"{payStub.PayRun.PeriodStart:MMM dd} - {payStub.PayRun.PeriodEnd:MMM dd, yyyy}"
            : "N/A";
        PayDate = payStub.PayRun?.PayDate.ToString("MMM dd, yyyy") ?? "N/A";

        EmployeeName = payStub.Employee?.FullName ?? "Unknown";
        EmployeeId = payStub.Employee != null ? $"#EMP-{payStub.Employee.Id:D5}" : "#N/A";

        GrossPay = payStub.GrossPay;
        NetPay = payStub.NetPay;
        TotalGross = payStub.GrossPay;
        TotalTaxes = payStub.TotalTaxes;
        TotalDeductions = payStub.PreTax401kDeduction + payStub.PostTaxDeductions;

        YtdGross = payStub.YtdGross;
        YtdTaxes = payStub.YtdTaxes;
        YtdNetPay = payStub.YtdNet;
        YtdDeductions = payStub.YtdGross - payStub.YtdNet - payStub.YtdTaxes;

        // Populate earning lines
        EarningLines.Clear();
        foreach (var line in payStub.EarningLines)
        {
            EarningLines.Add(new FinancialLineItem
            {
                Description = line.Description,
                RateDisplay = line.Type == EarningType.Regular || line.Type == EarningType.Overtime
                    ? $"{line.Hours:F2} @ ${line.Rate:F2}"
                    : "Lump Sum",
                AmountDisplay = $"${line.Amount:N2}"
            });
        }

        // Populate deduction lines
        DeductionLines.Clear();
        foreach (var line in payStub.DeductionLines)
        {
            DeductionLines.Add(new FinancialLineItem
            {
                Description = line.Description,
                AmountDisplay = $"${line.Amount:N2}"
            });
        }

        // Populate tax lines
        TaxLines.Clear();
        foreach (var line in payStub.TaxLines)
        {
            TaxLines.Add(new FinancialLineItem
            {
                Description = line.Description,
                AmountDisplay = $"${line.Amount:N2}"
            });
        }
    }

    [RelayCommand]
    private void NavigateHome()
    {
        // Placeholder - will navigate to home
    }

    [RelayCommand]
    private void NavigatePayStubs()
    {
        // Placeholder - will navigate to pay stubs list
    }

    [RelayCommand]
    private void ViewBreakdown()
    {
        // Placeholder - will show detailed breakdown
    }

    [RelayCommand]
    private async Task DownloadCsvAsync()
    {
        if (SelectedPayStub == null)
        {
            StatusMessage = "Please select a pay stub to export";
            return;
        }

        IsLoading = true;
        StatusMessage = "Exporting to CSV...";

        try
        {
            var filePath = await _exportService.ExportPayStubToCsvAsync(SelectedPayStub.Id);
            StatusMessage = $"Exported to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadPdfAsync()
    {
        if (SelectedPayStub == null)
        {
            StatusMessage = "Please select a pay stub to export";
            return;
        }

        IsLoading = true;
        StatusMessage = "Exporting to PDF...";

        try
        {
            var filePath = await _exportService.ExportPayStubToPdfAsync(SelectedPayStub.Id);
            StatusMessage = $"Exported to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ContactSupport()
    {
        // Placeholder - will open support
    }

    [RelayCommand]
    private async Task ExportPayStubCsvAsync(int payStubId)
    {
        IsLoading = true;
        StatusMessage = "Exporting to CSV...";

        try
        {
            var filePath = await _exportService.ExportPayStubToCsvAsync(payStubId);
            StatusMessage = $"Exported to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportPayStubPdfAsync(int payStubId)
    {
        IsLoading = true;
        StatusMessage = "Exporting to PDF...";

        try
        {
            var filePath = await _exportService.ExportPayStubToPdfAsync(payStubId);
            StatusMessage = $"Exported to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event EventHandler? NavigateBackRequested;

    [RelayCommand]
    private void NavigateBack()
    {
        NavigateBackRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ApplyFilters()
    {
        FilteredPayStubs.Clear();

        if (PayStubs.Count == 0)
        {
            return; // No pay stubs loaded yet
        }

        var query = PayStubs.AsEnumerable();

        // Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            query = query.Where(ps =>
                ps.EmployeeName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                ps.StatementNumber.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        // Year filter - filter by selected year
        query = query.Where(ps => ps.PayDate.Year == YearFilter);

        // Period filter
        if (PeriodFilterIndex > 0 && PeriodFilterIndex < 5)
        {
            var quarter = PeriodFilterIndex;
            query = query.Where(ps =>
            {
                var month = ps.PayDate.Month;
                return quarter == 1 && month >= 1 && month <= 3 ||
                       quarter == 2 && month >= 4 && month <= 6 ||
                       quarter == 3 && month >= 7 && month <= 9 ||
                       quarter == 4 && month >= 10 && month <= 12;
            });
        }

        // Employee filter
        if (!string.IsNullOrEmpty(SelectedEmployeeFilter))
        {
            query = query.Where(ps => ps.EmployeeName == SelectedEmployeeFilter);
        }

        foreach (var payStub in query)
        {
            FilteredPayStubs.Add(payStub);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnYearFilterChanged(int value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnPeriodFilterIndexChanged(int value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedEmployeeFilterChanged(string? value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedPayStubChanged(PayStubListItem? value)
    {
        if (value != null)
        {
            LoadPayStubCommand.Execute(value.Id);
        }
        else
        {
            // Clear detail view when selection is cleared
            PayStubId = 0;
            StatementNumber = "#00000";
            PeriodRange = "";
            PayDate = "";
            EmployeeName = "";
            EmployeeId = "";
            GrossPay = 0;
            NetPay = 0;
            TotalGross = 0;
            TotalDeductions = 0;
            TotalTaxes = 0;
            YtdGross = 0;
            YtdDeductions = 0;
            YtdTaxes = 0;
            YtdNetPay = 0;
            EarningLines.Clear();
            DeductionLines.Clear();
            TaxLines.Clear();
        }
        OnPropertyChanged(nameof(HasSelectedPayStub));
    }
    
    partial void OnPayStubIdChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelectedPayStub));
    }
}

/// <summary>
/// List item model for pay stub list view.
/// </summary>
public class PayStubListItem
{
    public int Id { get; set; }
    public string StatementNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string PeriodRange { get; set; } = string.Empty;
    public DateTime PayDate { get; set; }
    public decimal GrossPay { get; set; }
    public decimal NetPay { get; set; }
}

/// <summary>
/// Represents a line item in a financial table (earnings, deductions, or taxes).
/// </summary>
public class FinancialLineItem
{
    public string Description { get; set; } = string.Empty;
    public string RateDisplay { get; set; } = string.Empty;
    public string AmountDisplay { get; set; } = string.Empty;
}
