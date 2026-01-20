using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Services;
using System.Collections.ObjectModel;

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// ViewModel for the Reports page with filters and summary data.
/// </summary>
public partial class ReportsViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly AggregationService _aggregationService;
    private readonly ExportService _exportService;

    public ReportsViewModel(AppDbContext dbContext, AggregationService aggregationService, ExportService exportService)
    {
        _dbContext = dbContext;
        _aggregationService = aggregationService;
        _exportService = exportService;
        _selectedYear = DateTime.Now.Year;
        _customStartDate = new DateTimeOffset(new DateTime(_selectedYear, 1, 1));
        _customEndDate = DateTimeOffset.Now;

        // Populate years
        for (int y = DateTime.Now.Year; y >= DateTime.Now.Year - 5; y--)
        {
            AvailableYears.Add(y);
        }
        
        // Populate departments
        Departments = new ObservableCollection<string>
        {
            "All Departments",
            "Engineering",
            "Marketing",
            "Sales",
            "HR",
            "Operations"
        };
        
        // Load initial report data
        _ = RunReportAsync();
    }
    // ═══════════════════════════════════════════════════════════════
    // COLLECTIONS
    // ═══════════════════════════════════════════════════════════════

    public ObservableCollection<int> AvailableYears { get; } = new();
    public ObservableCollection<string> Departments { get; } = new();
    public ObservableCollection<EmployeeReportRow> EmployeeTotals { get; } = new();
    public ObservableCollection<EmployeeSummaryRow> EmployeeSummaries { get; } = new();

    // ═══════════════════════════════════════════════════════════════
    // SELECTION
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private EmployeeSummaryRow? _selectedEmployeeSummary;

    // ═══════════════════════════════════════════════════════════════
    // SEARCH STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isSearchActive;

    // ═══════════════════════════════════════════════════════════════
    // FILTERS
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _selectedYear = DateTime.Now.Year;

    [ObservableProperty]
    private int _selectedPeriodIndex = 2; // Q3 default

    [ObservableProperty]
    private int _periodIndex = 4; // Full Year

    [ObservableProperty]
    private DateTimeOffset _customStartDate;

    [ObservableProperty]
    private DateTimeOffset _customEndDate;

    [ObservableProperty]
    private string? _selectedDepartment;

    public bool IsCustomDateRange => PeriodIndex == 5;

    // ═══════════════════════════════════════════════════════════════
    // PAGINATION
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _pageSize = 10;

    public int TotalEmployeeCount => EmployeeSummaries.Count;
    public string CurrentPageRange => $"{(_currentPage - 1) * _pageSize + 1}-{Math.Min(_currentPage * _pageSize, TotalEmployeeCount)}";

    // ═══════════════════════════════════════════════════════════════
    // COMPANY TOTALS (computed from PayStubs in date range)
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private decimal _companyTotalGross;

    [ObservableProperty]
    private decimal _companyTotalTaxes;

    [ObservableProperty]
    private decimal _companyTotalBenefits;

    [ObservableProperty]
    private decimal _companyTotalNet;

    // Individual tax components for detailed display
    [ObservableProperty]
    private decimal _companyGross;

    [ObservableProperty]
    private decimal _companyFederalTax;

    [ObservableProperty]
    private decimal _companyStateTax;

    [ObservableProperty]
    private decimal _companySocialSecurity;

    [ObservableProperty]
    private decimal _companyMedicare;

    [ObservableProperty]
    private decimal _companyNet;

    // ═══════════════════════════════════════════════════════════════
    // KPI TILES
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private decimal _totalGross;

    [ObservableProperty]
    private decimal _totalTaxes;

    [ObservableProperty]
    private decimal _totalBenefits;

    [ObservableProperty]
    private decimal _totalNet;

    // ═══════════════════════════════════════════════════════════════
    // DISPLAY PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    public string LastUpdatedDisplay => $"Last updated: {DateTime.Now:MMM dd, yyyy, hh:mm tt}";
    public string GrossTrend => "+2.4% vs last period";
    public string TaxesTrend => "-1.1% vs last period";
    public string BenefitsTrend => "+0.5% vs last period";
    public string NetTrend => "+3.2% vs last period";

    // ═══════════════════════════════════════════════════════════════
    // STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task RunReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Running report...";

        try
        {
            var (startDate, endDate) = GetDateRange();
            await LoadReportDataAsync(startDate, endDate);
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

    /// <summary>
    /// Loads report data for the specified date range.
    /// Queries PayStubs joined with PayRuns and Employees where PayRun.PayDate is in the interval.
    /// </summary>
    public async Task LoadReportDataAsync(DateTime startDate, DateTime endDate)
    {
        // Query PayStubs joined with PayRuns and Employees where PayRun.PayDate is in the interval
        var payStubs = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Include(ps => ps.Employee)
            .Where(ps => ps.PayRun != null && 
                         ps.PayRun.PayDate >= startDate && 
                         ps.PayRun.PayDate <= endDate)
            .ToListAsync();

        // Compute company totals
        CompanyTotalGross = payStubs.Sum(ps => ps.GrossPay);
        CompanyTotalTaxes = payStubs.Sum(ps => ps.TotalTaxes);
        CompanyTotalBenefits = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions);
        CompanyTotalNet = payStubs.Sum(ps => ps.NetPay);

        // Set individual tax components for display
        CompanyGross = CompanyTotalGross;
        CompanyFederalTax = payStubs.Sum(ps => ps.TaxFederal);
        CompanyStateTax = payStubs.Sum(ps => ps.TaxState);
        CompanySocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity);
        CompanyMedicare = payStubs.Sum(ps => ps.TaxMedicare);
        CompanyTotalTaxes = CompanyFederalTax + CompanyStateTax + CompanySocialSecurity + CompanyMedicare;
        CompanyNet = CompanyTotalNet;

        // Set KPI tile values
        TotalGross = CompanyTotalGross;
        TotalTaxes = CompanyTotalTaxes;
        TotalBenefits = CompanyTotalBenefits;
        TotalNet = CompanyTotalNet;

        // Compute per-employee totals over the same window
        var employeeGroups = payStubs
            .GroupBy(ps => ps.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Employee = g.First().Employee,
                GrossPay = g.Sum(ps => ps.GrossPay),
                FederalTax = g.Sum(ps => ps.TaxFederal),
                StateTax = g.Sum(ps => ps.TaxState),
                SocialSecurity = g.Sum(ps => ps.TaxSocialSecurity),
                Medicare = g.Sum(ps => ps.TaxMedicare),
                TotalTaxes = g.Sum(ps => ps.TotalTaxes),
                PreTax401k = g.Sum(ps => ps.PreTax401kDeduction),
                PostTaxDeductions = g.Sum(ps => ps.PostTaxDeductions),
                TotalDeductions = g.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
                NetPay = g.Sum(ps => ps.NetPay)
            })
            .OrderBy(g => g.Employee?.FullName ?? "Unknown")
            .ToList();

        // Populate EmployeeTotals collection
        EmployeeTotals.Clear();
        EmployeeSummaries.Clear();

        foreach (var group in employeeGroups)
        {
            var employeeName = group.Employee?.FullName ?? "Unknown";
            
            EmployeeTotals.Add(new EmployeeReportRow
            {
                EmployeeId = group.EmployeeId,
                EmployeeName = employeeName,
                GrossPay = group.GrossPay,
                FederalTax = group.FederalTax,
                StateTax = group.StateTax,
                SocialSecurity = group.SocialSecurity,
                Medicare = group.Medicare,
                TotalTaxes = group.TotalTaxes,
                TotalDeductions = group.TotalDeductions,
                NetPay = group.NetPay
            });

            // Also populate the summary rows for the new grid
            var nameParts = employeeName.Split(' ');
            var initials = nameParts.Length >= 2
                ? $"{nameParts[0][0]}{nameParts[^1][0]}"
                : employeeName[..Math.Min(2, employeeName.Length)];

            EmployeeSummaries.Add(new EmployeeSummaryRow
            {
                EmployeeId = $"EMP{group.EmployeeId:D3}",
                EmployeeName = employeeName,
                Initials = initials.ToUpper(),
                DepartmentType = group.Employee?.Department ?? "Full-Time",
                GrossPay = group.GrossPay,
                FederalTax = group.FederalTax,
                StateTax = group.StateTax,
                Benefits = group.TotalDeductions,
                NetPay = group.NetPay,
                Status = "PROCESSED"
            });
        }

        OnPropertyChanged(nameof(TotalEmployeeCount));
        OnPropertyChanged(nameof(CurrentPageRange));

        StatusMessage = $"Report complete: {employeeGroups.Count} employees, {startDate:d} - {endDate:d}";
    }

    private (DateTime start, DateTime end) GetDateRange()
    {
        var year = SelectedYear;
        return PeriodIndex switch
        {
            0 => GetQuarterRange(year, 1),   // Q1
            1 => GetQuarterRange(year, 2),   // Q2
            2 => GetQuarterRange(year, 3),   // Q3
            3 => GetQuarterRange(year, 4),   // Q4
            4 => GetYearRange(year),         // Full Year
            5 => (CustomStartDate.DateTime, CustomEndDate.DateTime), // Custom
            _ => GetYearRange(year)
        };
    }

    /// <summary>
    /// Gets the date range for a full year.
    /// </summary>
    public static (DateTime start, DateTime end) GetYearRange(int year)
    {
        return (new DateTime(year, 1, 1), new DateTime(year, 12, 31, 23, 59, 59));
    }

    /// <summary>
    /// Gets the date range for a specific quarter of a year.
    /// </summary>
    public static (DateTime start, DateTime end) GetQuarterRange(int year, int quarter)
    {
        return quarter switch
        {
            1 => (new DateTime(year, 1, 1), new DateTime(year, 3, 31, 23, 59, 59)),   // Q1
            2 => (new DateTime(year, 4, 1), new DateTime(year, 6, 30, 23, 59, 59)),   // Q2
            3 => (new DateTime(year, 7, 1), new DateTime(year, 9, 30, 23, 59, 59)),   // Q3
            4 => (new DateTime(year, 10, 1), new DateTime(year, 12, 31, 23, 59, 59)), // Q4
            _ => (new DateTime(year, 1, 1), new DateTime(year, 12, 31, 23, 59, 59))
        };
    }

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        IsLoading = true;
        StatusMessage = "Exporting to CSV...";

        try
        {
            var (startDate, endDate) = GetDateRange();
            
            // Query PayStubs directly for the date range
            var payStubs = await _dbContext.PayStubs
                .Include(ps => ps.PayRun)
                .Include(ps => ps.Employee)
                .Where(ps => ps.PayRun != null && 
                             ps.PayRun.PayDate >= startDate && 
                             ps.PayRun.PayDate <= endDate)
                .ToListAsync();

            // Compute company totals
            var companyTotalGross = payStubs.Sum(ps => ps.GrossPay);
            var companyTotalTaxes = payStubs.Sum(ps => ps.TotalTaxes);
            var companyTotalBenefits = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions);
            var companyTotalNet = payStubs.Sum(ps => ps.NetPay);

            var companyTotals = new CompanyTotals
            {
                Year = SelectedYear,
                EmployeeCount = payStubs.Select(ps => ps.EmployeeId).Distinct().Count(),
                GrossPay = companyTotalGross,
                FederalTax = payStubs.Sum(ps => ps.TaxFederal),
                StateTax = payStubs.Sum(ps => ps.TaxState),
                SocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity),
                Medicare = payStubs.Sum(ps => ps.TaxMedicare),
                TotalTaxes = companyTotalTaxes,
                PreTax401k = payStubs.Sum(ps => ps.PreTax401kDeduction),
                PostTaxDeductions = payStubs.Sum(ps => ps.PostTaxDeductions),
                TotalDeductions = companyTotalBenefits,
                NetPay = companyTotalNet,
                PayStubCount = payStubs.Count
            };

            // Convert EmployeeReportRow to EmployeeTotals
            var employeeTotals = EmployeeTotals.Select(row => new EmployeeTotals
            {
                EmployeeId = row.EmployeeId,
                EmployeeName = row.EmployeeName,
                GrossPay = row.GrossPay,
                FederalTax = row.FederalTax,
                StateTax = row.StateTax,
                SocialSecurity = row.SocialSecurity,
                Medicare = row.Medicare,
                TotalTaxes = row.TotalTaxes,
                PreTax401k = 0, // Will be calculated from deductions
                PostTaxDeductions = 0,
                TotalDeductions = row.TotalDeductions,
                NetPay = row.NetPay
            }).ToList();

            var filePath = await _exportService.ExportReportToCsvAsync(
                employeeTotals,
                companyTotals,
                startDate,
                endDate);

            LastExportPath = filePath;
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
    private async Task ExportPdfAsync()
    {
        IsLoading = true;
        StatusMessage = "Exporting to PDF...";

        try
        {
            var (startDate, endDate) = GetDateRange();
            
            // Query PayStubs directly for the date range
            var payStubs = await _dbContext.PayStubs
                .Include(ps => ps.PayRun)
                .Include(ps => ps.Employee)
                .Where(ps => ps.PayRun != null && 
                             ps.PayRun.PayDate >= startDate && 
                             ps.PayRun.PayDate <= endDate)
                .ToListAsync();

            // Compute company totals
            var companyTotalGross = payStubs.Sum(ps => ps.GrossPay);
            var companyTotalTaxes = payStubs.Sum(ps => ps.TotalTaxes);
            var companyTotalBenefits = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions);
            var companyTotalNet = payStubs.Sum(ps => ps.NetPay);

            var companyTotals = new CompanyTotals
            {
                Year = SelectedYear,
                EmployeeCount = payStubs.Select(ps => ps.EmployeeId).Distinct().Count(),
                GrossPay = companyTotalGross,
                FederalTax = payStubs.Sum(ps => ps.TaxFederal),
                StateTax = payStubs.Sum(ps => ps.TaxState),
                SocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity),
                Medicare = payStubs.Sum(ps => ps.TaxMedicare),
                TotalTaxes = companyTotalTaxes,
                PreTax401k = payStubs.Sum(ps => ps.PreTax401kDeduction),
                PostTaxDeductions = payStubs.Sum(ps => ps.PostTaxDeductions),
                TotalDeductions = companyTotalBenefits,
                NetPay = companyTotalNet,
                PayStubCount = payStubs.Count
            };

            // Convert EmployeeReportRow to EmployeeTotals
            var employeeTotals = EmployeeTotals.Select(row => new EmployeeTotals
            {
                EmployeeId = row.EmployeeId,
                EmployeeName = row.EmployeeName,
                GrossPay = row.GrossPay,
                FederalTax = row.FederalTax,
                StateTax = row.StateTax,
                SocialSecurity = row.SocialSecurity,
                Medicare = row.Medicare,
                TotalTaxes = row.TotalTaxes,
                PreTax401k = 0,
                PostTaxDeductions = 0,
                TotalDeductions = row.TotalDeductions,
                NetPay = row.NetPay
            }).ToList();

            var filePath = await _exportService.ExportReportToPdfAsync(
                employeeTotals,
                companyTotals,
                startDate,
                endDate);

            LastExportPath = filePath;
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
    private void NavigateToPayrollRun()
    {
        // Placeholder - will navigate
    }

    [RelayCommand]
    private void NavigateToTaxForms()
    {
        // Placeholder - will navigate
    }

    [RelayCommand]
    private void NavigateToBenefits()
    {
        // Placeholder - will navigate
    }

    [RelayCommand]
    private void NavigateToArchive()
    {
        // Placeholder - will navigate
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        var maxPages = (TotalEmployeeCount + PageSize - 1) / PageSize;
        if (CurrentPage < maxPages)
        {
            CurrentPage++;
        }
    }

    [RelayCommand]
    private void ApplyFilters()
    {
        // Filter EmployeeSummaries based on search and department
        var query = EmployeeSummaries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            query = query.Where(e =>
                e.EmployeeName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.EmployeeId.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.ToLowerInvariant();
            query = query.Where(e =>
                e.EmployeeName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.DepartmentType.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SelectedDepartment) && SelectedDepartment != "All Departments")
        {
            // Placeholder - will filter by department when available
        }

        // Apply pagination
        var filtered = query.ToList();
        var paged = filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        // Note: In a real implementation, you'd want to maintain the full filtered list
        // and only display the paged results. For now, we'll just filter the collection.
    }

    partial void OnSearchTextChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedYearChanged(int value)
    {
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedPeriodIndexChanged(int value)
    {
        PeriodIndex = value;
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnPeriodIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsCustomDateRange));
        ApplyFiltersCommand.Execute(null);
    }

    partial void OnSelectedDepartmentChanged(string? value)
    {
        ApplyFiltersCommand.Execute(null);
    }
}

/// <summary>
/// Row model for the employee totals report grid.
/// </summary>
public class EmployeeReportRow
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal GrossPay { get; set; }
    public decimal FederalTax { get; set; }
    public decimal StateTax { get; set; }
    public decimal SocialSecurity { get; set; }
    public decimal Medicare { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
}

/// <summary>
/// Row model for the employee summary data grid with display formatting.
/// </summary>
public class EmployeeSummaryRow
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string DepartmentType { get; set; } = string.Empty;
    
    public decimal GrossPay { get; set; }
    public decimal FederalTax { get; set; }
    public decimal StateTax { get; set; }
    public decimal Benefits { get; set; }
    public decimal NetPay { get; set; }
    
    public string Status { get; set; } = "PROCESSED";
    
    // Display properties
    public string GrossPayDisplay => $"${GrossPay:N2}";
    public string FederalTaxDisplay => $"${FederalTax:N2}";
    public string StateTaxDisplay => $"${StateTax:N2}";
    public string BenefitsDisplay => $"${Benefits:N2}";
    public string NetPayDisplay => $"${NetPay:N2}";
}
