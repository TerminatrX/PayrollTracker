using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

public class PayrollService
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettings _companySettings;

    public PayrollService(AppDbContext dbContext, CompanySettings companySettings)
    {
        _dbContext = dbContext;
        _companySettings = companySettings;
    }

    public async Task<PayStub> GeneratePayStubAsync(
        Employee employee,
        PayRun payRun,
        decimal? hoursOverride = null)
    {
        var hoursWorked = hoursOverride ?? 0m;
        var payPeriods = _companySettings.PayPeriodsPerYear <= 0
            ? 26
            : _companySettings.PayPeriodsPerYear;

        var grossPay = employee.IsHourly
            ? hoursWorked * employee.HourlyRate
            : employee.AnnualSalary / payPeriods;

        var preTax401k = grossPay * (employee.PreTax401kPercent / 100m);
        var taxableIncome = grossPay - preTax401k;

        var taxFederal = taxableIncome * (_companySettings.FederalTaxPercent / 100m);
        var taxState = taxableIncome * (_companySettings.StateTaxPercent / 100m);
        var taxSocialSecurity = taxableIncome * (_companySettings.SocialSecurityPercent / 100m);
        var taxMedicare = taxableIncome * (_companySettings.MedicarePercent / 100m);

        var postTaxDeductions = employee.HealthInsurancePerPeriod + employee.OtherDeductionsPerPeriod;
        var netPay = taxableIncome
                     - taxFederal
                     - taxState
                     - taxSocialSecurity
                     - taxMedicare
                     - postTaxDeductions;

        var year = payRun.PayDate.Year;
        var priorTotals = await _dbContext.PayStubs
            .Where(ps => ps.EmployeeId == employee.Id && ps.PayRun!.PayDate.Year == year)
            .GroupBy(ps => ps.EmployeeId)
            .Select(g => new
            {
                Gross = g.Sum(x => x.GrossPay),
                Net = g.Sum(x => x.NetPay)
            })
            .FirstOrDefaultAsync();

        var ytdGross = (priorTotals?.Gross ?? 0m) + grossPay;
        var ytdNet = (priorTotals?.Net ?? 0m) + netPay;

        return new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRun.Id,
            HoursWorked = hoursWorked,
            GrossPay = grossPay,
            PreTax401kDeduction = preTax401k,
            TaxFederal = taxFederal,
            TaxState = taxState,
            TaxSocialSecurity = taxSocialSecurity,
            TaxMedicare = taxMedicare,
            PostTaxDeductions = postTaxDeductions,
            NetPay = netPay,
            YtdGross = ytdGross,
            YtdNet = ytdNet
        };
    }
}
