using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using System.Collections.ObjectModel;
using System.Text;

namespace PayrollManager.UI.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    
    private int _selectedYear;
    private int _periodIndex = 4; // Full Year
    private DateTimeOffset _customStartDate;
    private DateTimeOffset _customEndDate;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _lastExportPath = string.Empty;

    // Totals
    private decimal _companyGross;
    private decimal _companyFederalTax;
    private decimal _companyStateTax;
    private decimal _companySocialSecurity;
    private decimal _companyMedicare;
    private decimal _companyTotalTaxes;
    private decimal _companyNet;

    public ReportsViewModel(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        _selectedYear = DateTime.Now.Year;
        _customStartDate = new DateTimeOffset(new DateTime(_selectedYear, 1, 1));
        _customEndDate = DateTimeOffset.Now;

        // Populate years
        for (int y = DateTime.Now.Year; y >= DateTime.Now.Year - 5; y--)
        {
            AvailableYears.Add(y);
        }
    }

    public ObservableCollection<int> AvailableYears { get; } = new();
    public ObservableCollection<EmployeeReportRow> EmployeeTotals { get; } = new();

    public int SelectedYear
    {
        get => _selectedYear;
        set => SetProperty(ref _selectedYear, value);
    }

    public int PeriodIndex
    {
        get => _periodIndex;
        set
        {
            if (SetProperty(ref _periodIndex, value))
            {
                OnPropertyChanged(nameof(IsCustomDateRange));
            }
        }
    }

    public bool IsCustomDateRange => PeriodIndex == 5;

    public DateTimeOffset CustomStartDate
    {
        get => _customStartDate;
        set => SetProperty(ref _customStartDate, value);
    }

    public DateTimeOffset CustomEndDate
    {
        get => _customEndDate;
        set => SetProperty(ref _customEndDate, value);
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

    public string LastExportPath
    {
        get => _lastExportPath;
        set => SetProperty(ref _lastExportPath, value);
    }

    // Company Totals
    public decimal CompanyGross
    {
        get => _companyGross;
        set => SetProperty(ref _companyGross, value);
    }

    public decimal CompanyFederalTax
    {
        get => _companyFederalTax;
        set => SetProperty(ref _companyFederalTax, value);
    }

    public decimal CompanyStateTax
    {
        get => _companyStateTax;
        set => SetProperty(ref _companyStateTax, value);
    }

    public decimal CompanySocialSecurity
    {
        get => _companySocialSecurity;
        set => SetProperty(ref _companySocialSecurity, value);
    }

    public decimal CompanyMedicare
    {
        get => _companyMedicare;
        set => SetProperty(ref _companyMedicare, value);
    }

    public decimal CompanyTotalTaxes
    {
        get => _companyTotalTaxes;
        set => SetProperty(ref _companyTotalTaxes, value);
    }

    public decimal CompanyNet
    {
        get => _companyNet;
        set => SetProperty(ref _companyNet, value);
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
    public async Task RunReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Running report...";

        try
        {
            var (start, end) = GetDateRange();

            var query = _dbContext.PayStubs
                .Include(ps => ps.PayRun)
                .Include(ps => ps.Employee)
                .Where(ps => ps.PayRun!.PayDate >= start && ps.PayRun.PayDate <= end);

            // Get per-employee totals
            var employeeRows = await query
                .GroupBy(ps => new { ps.Employee!.Id, ps.Employee.FirstName, ps.Employee.LastName })
                .Select(g => new EmployeeReportRow
                {
                    EmployeeId = g.Key.Id,
                    EmployeeName = g.Key.FirstName + " " + g.Key.LastName,
                    GrossPay = g.Sum(x => x.GrossPay),
                    FederalTax = g.Sum(x => x.TaxFederal),
                    StateTax = g.Sum(x => x.TaxState),
                    SocialSecurity = g.Sum(x => x.TaxSocialSecurity),
                    Medicare = g.Sum(x => x.TaxMedicare),
                    TotalTaxes = g.Sum(x => x.TaxFederal + x.TaxState + x.TaxSocialSecurity + x.TaxMedicare),
                    TotalDeductions = g.Sum(x => x.PreTax401kDeduction + x.PostTaxDeductions),
                    NetPay = g.Sum(x => x.NetPay)
                })
                .OrderBy(r => r.EmployeeName)
                .ToListAsync();

            EmployeeTotals.Clear();
            foreach (var row in employeeRows)
            {
                EmployeeTotals.Add(row);
            }

            // Get company totals
            var totals = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Gross = g.Sum(x => x.GrossPay),
                    Federal = g.Sum(x => x.TaxFederal),
                    State = g.Sum(x => x.TaxState),
                    SS = g.Sum(x => x.TaxSocialSecurity),
                    Medicare = g.Sum(x => x.TaxMedicare),
                    Net = g.Sum(x => x.NetPay)
                })
                .FirstOrDefaultAsync();

            CompanyGross = totals?.Gross ?? 0;
            CompanyFederalTax = totals?.Federal ?? 0;
            CompanyStateTax = totals?.State ?? 0;
            CompanySocialSecurity = totals?.SS ?? 0;
            CompanyMedicare = totals?.Medicare ?? 0;
            CompanyTotalTaxes = CompanyFederalTax + CompanyStateTax + CompanySocialSecurity + CompanyMedicare;
            CompanyNet = totals?.Net ?? 0;

            StatusMessage = $"Report complete: {employeeRows.Count} employees, {start:d} - {end:d}";
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
    public async Task ExportToCsvAsync()
    {
        if (EmployeeTotals.Count == 0)
        {
            await RunReportAsync();
        }

        IsLoading = true;
        StatusMessage = "Exporting...";

        try
        {
            var (start, end) = GetDateRange();
            var periodLabel = PeriodIndex switch
            {
                0 => "Q1",
                1 => "Q2",
                2 => "Q3",
                3 => "Q4",
                4 => "FullYear",
                _ => "Custom"
            };

            var fileName = $"payroll_report_{SelectedYear}_{periodLabel}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

            var builder = new StringBuilder();
            builder.AppendLine("Employee,Gross,Federal Tax,State Tax,Social Security,Medicare,Total Taxes,Deductions,Net Pay");

            foreach (var row in EmployeeTotals)
            {
                builder.AppendLine($"{Escape(row.EmployeeName)},{row.GrossPay},{row.FederalTax},{row.StateTax},{row.SocialSecurity},{row.Medicare},{row.TotalTaxes},{row.TotalDeductions},{row.NetPay}");
            }

            builder.AppendLine();
            builder.AppendLine($"COMPANY TOTALS,{CompanyGross},{CompanyFederalTax},{CompanyStateTax},{CompanySocialSecurity},{CompanyMedicare},{CompanyTotalTaxes},,{CompanyNet}");

            await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8);
            LastExportPath = $"Exported to: {path}";
            StatusMessage = "Export complete!";
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

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

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
