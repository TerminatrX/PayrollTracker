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
    // COMPANY TOTALS
    // ═══════════════════════════════════════════════════════════════

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
    private decimal _companyTotalTaxes;

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
            var (start, end) = GetDateRange();

            // Use AggregationService to get employee and company totals
            var employeeTotalsList = await _aggregationService.GetAllEmployeeTotalsAsync(start, end);
            
            // Get company totals based on period type
            CompanyTotals? companyTotals;
            if (PeriodIndex == 4) // Full Year
            {
                companyTotals = await _aggregationService.GetCompanyYtdTotalsAsync(SelectedYear);
            }
            else if (PeriodIndex >= 0 && PeriodIndex <= 3) // Quarter
            {
                companyTotals = await _aggregationService.GetCompanyQtdTotalsAsync(end);
            }
            else // Custom range
            {
                // Calculate company totals for custom range
                var query = _dbContext.PayStubs
                    .Include(ps => ps.PayRun)
                    .Where(ps => ps.PayRun!.PayDate >= start && ps.PayRun.PayDate <= end);

                var totals = await query
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        EmployeeCount = g.Select(x => x.EmployeeId).Distinct().Count(),
                        Gross = g.Sum(x => x.GrossPay),
                        Federal = g.Sum(x => x.TaxFederal),
                        State = g.Sum(x => x.TaxState),
                        SS = g.Sum(x => x.TaxSocialSecurity),
                        Medicare = g.Sum(x => x.TaxMedicare),
                        PreTax401k = g.Sum(x => x.PreTax401kDeduction),
                        PostTax = g.Sum(x => x.PostTaxDeductions),
                        Net = g.Sum(x => x.NetPay),
                        Count = g.Count()
                    })
                    .FirstOrDefaultAsync();

                companyTotals = totals != null ? new CompanyTotals
                {
                    Year = SelectedYear,
                    EmployeeCount = totals.EmployeeCount,
                    GrossPay = totals.Gross,
                    FederalTax = totals.Federal,
                    StateTax = totals.State,
                    SocialSecurity = totals.SS,
                    Medicare = totals.Medicare,
                    TotalTaxes = totals.Federal + totals.State + totals.SS + totals.Medicare,
                    PreTax401k = totals.PreTax401k,
                    PostTaxDeductions = totals.PostTax,
                    TotalDeductions = totals.PreTax401k + totals.PostTax,
                    NetPay = totals.Net,
                    PayStubCount = totals.Count
                } : null;
            }

            // Populate EmployeeTotals collection
            EmployeeTotals.Clear();
            EmployeeSummaries.Clear();
            
            foreach (var totals in employeeTotalsList)
            {
                EmployeeTotals.Add(new EmployeeReportRow
                {
                    EmployeeId = totals.EmployeeId,
                    EmployeeName = totals.EmployeeName,
                    GrossPay = totals.GrossPay,
                    FederalTax = totals.FederalTax,
                    StateTax = totals.StateTax,
                    SocialSecurity = totals.SocialSecurity,
                    Medicare = totals.Medicare,
                    TotalTaxes = totals.TotalTaxes,
                    TotalDeductions = totals.TotalDeductions,
                    NetPay = totals.NetPay
                });
                
                // Also populate the summary rows for the new grid
                var nameParts = totals.EmployeeName.Split(' ');
                var initials = nameParts.Length >= 2 
                    ? $"{nameParts[0][0]}{nameParts[^1][0]}" 
                    : totals.EmployeeName[..Math.Min(2, totals.EmployeeName.Length)];
                
                EmployeeSummaries.Add(new EmployeeSummaryRow
                {
                    EmployeeId = $"EMP{totals.EmployeeId:D3}",
                    EmployeeName = totals.EmployeeName,
                    Initials = initials.ToUpper(),
                    DepartmentType = "Full-Time",
                    GrossPay = totals.GrossPay,
                    FederalTax = totals.FederalTax,
                    StateTax = totals.StateTax,
                    Benefits = totals.TotalDeductions,
                    NetPay = totals.NetPay,
                    Status = "PROCESSED"
                });
            }
            
            OnPropertyChanged(nameof(TotalEmployeeCount));
            OnPropertyChanged(nameof(CurrentPageRange));

            // Set company totals
            if (companyTotals != null)
            {
                CompanyGross = companyTotals.GrossPay;
                CompanyFederalTax = companyTotals.FederalTax;
                CompanyStateTax = companyTotals.StateTax;
                CompanySocialSecurity = companyTotals.SocialSecurity;
                CompanyMedicare = companyTotals.Medicare;
                CompanyTotalTaxes = companyTotals.TotalTaxes;
                CompanyNet = companyTotals.NetPay;

                // Set KPI tile values
                TotalGross = CompanyGross;
                TotalTaxes = CompanyTotalTaxes;
                TotalBenefits = companyTotals.TotalDeductions;
                TotalNet = CompanyNet;
            }

            StatusMessage = $"Report complete: {employeeTotalsList.Count} employees, {start:d} - {end:d}";
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

    private (DateTime start, DateTime end) GetDateRange()
    {
        var year = SelectedYear;
        return PeriodIndex switch
        {
            0 => (new DateTime(year, 1, 1), new DateTime(year, 3, 31, 23, 59, 59)),   // Q1
            1 => (new DateTime(year, 4, 1), new DateTime(year, 6, 30, 23, 59, 59)),   // Q2
            2 => (new DateTime(year, 7, 1), new DateTime(year, 9, 30, 23, 59, 59)),   // Q3
            3 => (new DateTime(year, 10, 1), new DateTime(year, 12, 31, 23, 59, 59)), // Q4
            4 => (new DateTime(year, 1, 1), new DateTime(year, 12, 31, 23, 59, 59)),  // Full Year
            5 => (CustomStartDate.DateTime, CustomEndDate.DateTime),                   // Custom
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
            var (start, end) = GetDateRange();
            
            // Get company totals
            CompanyTotals? companyTotals = null;
            if (PeriodIndex == 4) // Full Year
            {
                companyTotals = await _aggregationService.GetCompanyYtdTotalsAsync(SelectedYear);
            }
            else if (PeriodIndex >= 0 && PeriodIndex <= 3) // Quarter
            {
                companyTotals = await _aggregationService.GetCompanyQtdTotalsAsync(end);
            }

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
                start,
                end);

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
            var (start, end) = GetDateRange();
            
            // Get company totals
            CompanyTotals? companyTotals = null;
            if (PeriodIndex == 4) // Full Year
            {
                companyTotals = await _aggregationService.GetCompanyYtdTotalsAsync(SelectedYear);
            }
            else if (PeriodIndex >= 0 && PeriodIndex <= 3) // Quarter
            {
                companyTotals = await _aggregationService.GetCompanyQtdTotalsAsync(end);
            }

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
                start,
                end);

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
