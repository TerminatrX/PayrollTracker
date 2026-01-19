using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Service for calculating YTD, QTD, and other aggregations for payroll data.
/// </summary>
public class AggregationService
{
    private readonly AppDbContext _dbContext;

    public AggregationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get quarter number (1-4) for a given date
    /// </summary>
    public static int GetQuarter(DateTime date)
    {
        return (date.Month - 1) / 3 + 1;
    }

    /// <summary>
    /// Get quarter start and end dates for a given date
    /// </summary>
    public static (DateTime start, DateTime end) GetQuarterRange(DateTime date)
    {
        var quarter = GetQuarter(date);
        var year = date.Year;
        
        return quarter switch
        {
            1 => (new DateTime(year, 1, 1), new DateTime(year, 3, 31, 23, 59, 59)),
            2 => (new DateTime(year, 4, 1), new DateTime(year, 6, 30, 23, 59, 59)),
            3 => (new DateTime(year, 7, 1), new DateTime(year, 9, 30, 23, 59, 59)),
            4 => (new DateTime(year, 10, 1), new DateTime(year, 12, 31, 23, 59, 59)),
            _ => (new DateTime(year, 1, 1), new DateTime(year, 12, 31, 23, 59, 59))
        };
    }

    /// <summary>
    /// Get year-to-date totals for an employee
    /// </summary>
    public async Task<EmployeeTotals> GetEmployeeYtdTotalsAsync(int employeeId, int year)
    {
        var payStubs = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.EmployeeId == employeeId && 
                         ps.PayRun!.PayDate.Year == year)
            .ToListAsync();

        return new EmployeeTotals
        {
            EmployeeId = employeeId,
            Year = year,
            GrossPay = payStubs.Sum(ps => ps.GrossPay),
            FederalTax = payStubs.Sum(ps => ps.TaxFederal),
            StateTax = payStubs.Sum(ps => ps.TaxState),
            SocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity),
            Medicare = payStubs.Sum(ps => ps.TaxMedicare),
            TotalTaxes = payStubs.Sum(ps => ps.TotalTaxes),
            PreTax401k = payStubs.Sum(ps => ps.PreTax401kDeduction),
            PostTaxDeductions = payStubs.Sum(ps => ps.PostTaxDeductions),
            TotalDeductions = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
            NetPay = payStubs.Sum(ps => ps.NetPay),
            PayStubCount = payStubs.Count
        };
    }

    /// <summary>
    /// Get quarter-to-date totals for an employee
    /// </summary>
    public async Task<EmployeeTotals> GetEmployeeQtdTotalsAsync(int employeeId, DateTime referenceDate)
    {
        var (quarterStart, quarterEnd) = GetQuarterRange(referenceDate);
        
        var payStubs = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.EmployeeId == employeeId && 
                         ps.PayRun!.PayDate >= quarterStart && 
                         ps.PayRun.PayDate <= quarterEnd)
            .ToListAsync();

        return new EmployeeTotals
        {
            EmployeeId = employeeId,
            Year = referenceDate.Year,
            Quarter = GetQuarter(referenceDate),
            GrossPay = payStubs.Sum(ps => ps.GrossPay),
            FederalTax = payStubs.Sum(ps => ps.TaxFederal),
            StateTax = payStubs.Sum(ps => ps.TaxState),
            SocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity),
            Medicare = payStubs.Sum(ps => ps.TaxMedicare),
            TotalTaxes = payStubs.Sum(ps => ps.TotalTaxes),
            PreTax401k = payStubs.Sum(ps => ps.PreTax401kDeduction),
            PostTaxDeductions = payStubs.Sum(ps => ps.PostTaxDeductions),
            TotalDeductions = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
            NetPay = payStubs.Sum(ps => ps.NetPay),
            PayStubCount = payStubs.Count
        };
    }

    /// <summary>
    /// Get company-wide year-to-date totals
    /// </summary>
    public async Task<CompanyTotals> GetCompanyYtdTotalsAsync(int year)
    {
        var payStubs = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.PayRun!.PayDate.Year == year)
            .ToListAsync();

        var employeeCount = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.PayRun!.PayDate.Year == year)
            .Select(ps => ps.EmployeeId)
            .Distinct()
            .CountAsync();

        return new CompanyTotals
        {
            Year = year,
            EmployeeCount = employeeCount,
            GrossPay = payStubs.Sum(ps => ps.GrossPay),
            FederalTax = payStubs.Sum(ps => ps.TaxFederal),
            StateTax = payStubs.Sum(ps => ps.TaxState),
            SocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity),
            Medicare = payStubs.Sum(ps => ps.TaxMedicare),
            TotalTaxes = payStubs.Sum(ps => ps.TotalTaxes),
            PreTax401k = payStubs.Sum(ps => ps.PreTax401kDeduction),
            PostTaxDeductions = payStubs.Sum(ps => ps.PostTaxDeductions),
            TotalDeductions = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
            NetPay = payStubs.Sum(ps => ps.NetPay),
            PayStubCount = payStubs.Count
        };
    }

    /// <summary>
    /// Get company-wide quarter-to-date totals
    /// </summary>
    public async Task<CompanyTotals> GetCompanyQtdTotalsAsync(DateTime referenceDate)
    {
        var (quarterStart, quarterEnd) = GetQuarterRange(referenceDate);
        
        var payStubs = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.PayRun!.PayDate >= quarterStart && 
                         ps.PayRun.PayDate <= quarterEnd)
            .ToListAsync();

        var employeeCount = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Where(ps => ps.PayRun!.PayDate >= quarterStart && 
                         ps.PayRun.PayDate <= quarterEnd)
            .Select(ps => ps.EmployeeId)
            .Distinct()
            .CountAsync();

        return new CompanyTotals
        {
            Year = referenceDate.Year,
            Quarter = GetQuarter(referenceDate),
            EmployeeCount = employeeCount,
            GrossPay = payStubs.Sum(ps => ps.GrossPay),
            FederalTax = payStubs.Sum(ps => ps.TaxFederal),
            StateTax = payStubs.Sum(ps => ps.TaxState),
            SocialSecurity = payStubs.Sum(ps => ps.TaxSocialSecurity),
            Medicare = payStubs.Sum(ps => ps.TaxMedicare),
            TotalTaxes = payStubs.Sum(ps => ps.TotalTaxes),
            PreTax401k = payStubs.Sum(ps => ps.PreTax401kDeduction),
            PostTaxDeductions = payStubs.Sum(ps => ps.PostTaxDeductions),
            TotalDeductions = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
            NetPay = payStubs.Sum(ps => ps.NetPay),
            PayStubCount = payStubs.Count
        };
    }

    /// <summary>
    /// Get all employee totals for a date range
    /// </summary>
    public async Task<List<EmployeeTotals>> GetAllEmployeeTotalsAsync(DateTime startDate, DateTime endDate)
    {
        var payStubs = await _dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Include(ps => ps.Employee)
            .Where(ps => ps.PayRun!.PayDate >= startDate && 
                         ps.PayRun.PayDate <= endDate)
            .ToListAsync();

        var employeeGroups = payStubs
            .GroupBy(ps => ps.EmployeeId)
            .Select(g => new EmployeeTotals
            {
                EmployeeId = g.Key,
                EmployeeName = g.First().Employee?.FullName ?? "Unknown",
                Year = g.First().PayRun!.PayDate.Year,
                GrossPay = g.Sum(ps => ps.GrossPay),
                FederalTax = g.Sum(ps => ps.TaxFederal),
                StateTax = g.Sum(ps => ps.TaxState),
                SocialSecurity = g.Sum(ps => ps.TaxSocialSecurity),
                Medicare = g.Sum(ps => ps.TaxMedicare),
                TotalTaxes = g.Sum(ps => ps.TotalTaxes),
                PreTax401k = g.Sum(ps => ps.PreTax401kDeduction),
                PostTaxDeductions = g.Sum(ps => ps.PostTaxDeductions),
                TotalDeductions = g.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
                NetPay = g.Sum(ps => ps.NetPay),
                PayStubCount = g.Count()
            })
            .OrderBy(t => t.EmployeeName)
            .ToList();

        return employeeGroups;
    }
}

/// <summary>
/// Employee totals aggregation
/// </summary>
public class EmployeeTotals
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int? Quarter { get; set; }
    public decimal GrossPay { get; set; }
    public decimal FederalTax { get; set; }
    public decimal StateTax { get; set; }
    public decimal SocialSecurity { get; set; }
    public decimal Medicare { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal PreTax401k { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public int PayStubCount { get; set; }
}

/// <summary>
/// Company totals aggregation
/// </summary>
public class CompanyTotals
{
    public int Year { get; set; }
    public int? Quarter { get; set; }
    public int EmployeeCount { get; set; }
    public decimal GrossPay { get; set; }
    public decimal FederalTax { get; set; }
    public decimal StateTax { get; set; }
    public decimal SocialSecurity { get; set; }
    public decimal Medicare { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal PreTax401k { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public int PayStubCount { get; set; }
}
