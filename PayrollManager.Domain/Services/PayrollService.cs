using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Input parameters for generating a pay stub with multiple earning types
/// </summary>
public class PayStubInput
{
    public decimal RegularHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public string? BonusDescription { get; set; }
    public string? CommissionDescription { get; set; }
}

public class PayrollService
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettings _companySettings;

    private const decimal OvertimeMultiplier = 1.5m;
    private const decimal StandardHoursPerPeriod = 40m;

    public PayrollService(AppDbContext dbContext, CompanySettings companySettings)
    {
        _dbContext = dbContext;
        _companySettings = companySettings;
    }

    /// <summary>
    /// Legacy method for backwards compatibility - converts total hours to regular/overtime
    /// </summary>
    public async Task<PayStub> GeneratePayStubAsync(
        Employee employee,
        PayRun payRun,
        decimal? hoursOverride = null)
    {
        var totalHours = hoursOverride ?? 0m;
        
        // Split hours into regular and overtime (over 40 = OT)
        var regularHours = Math.Min(totalHours, StandardHoursPerPeriod);
        var overtimeHours = Math.Max(0, totalHours - StandardHoursPerPeriod);

        var input = new PayStubInput
        {
            RegularHours = regularHours,
            OvertimeHours = overtimeHours,
            BonusAmount = 0,
            CommissionAmount = 0
        };

        return await GeneratePayStubAsync(employee, payRun, input);
    }

    /// <summary>
    /// Generate a pay stub with detailed earning lines including overtime, bonus, and commission
    /// </summary>
    public async Task<PayStub> GeneratePayStubAsync(
        Employee employee,
        PayRun payRun,
        PayStubInput input)
    {
        var payPeriods = _companySettings.PayPeriodsPerYear <= 0
            ? 26
            : _companySettings.PayPeriodsPerYear;

        var earningLines = new List<EarningLine>();
        decimal grossPay = 0m;
        decimal totalHours = 0m;

        if (employee.IsHourly)
        {
            // Regular earnings
            if (input.RegularHours > 0)
            {
                var regularAmount = input.RegularHours * employee.HourlyRate;
                earningLines.Add(new EarningLine
                {
                    Type = EarningType.Regular,
                    Hours = input.RegularHours,
                    Rate = employee.HourlyRate,
                    Amount = regularAmount,
                    Description = "Regular Pay"
                });
                grossPay += regularAmount;
                totalHours += input.RegularHours;
            }

            // Overtime earnings (1.5x rate)
            if (input.OvertimeHours > 0)
            {
                var overtimeRate = employee.HourlyRate * OvertimeMultiplier;
                var overtimeAmount = input.OvertimeHours * overtimeRate;
                earningLines.Add(new EarningLine
                {
                    Type = EarningType.Overtime,
                    Hours = input.OvertimeHours,
                    Rate = overtimeRate,
                    Amount = overtimeAmount,
                    Description = "Overtime Pay (1.5x)"
                });
                grossPay += overtimeAmount;
                totalHours += input.OvertimeHours;
            }
        }
        else
        {
            // Salary employee - single regular earning line
            var salaryPerPeriod = employee.AnnualSalary / payPeriods;
            earningLines.Add(new EarningLine
            {
                Type = EarningType.Regular,
                Hours = 0, // Salary employees don't track hours
                Rate = salaryPerPeriod,
                Amount = salaryPerPeriod,
                Description = "Salary"
            });
            grossPay += salaryPerPeriod;
        }

        // Bonus
        if (input.BonusAmount > 0)
        {
            earningLines.Add(new EarningLine
            {
                Type = EarningType.Bonus,
                Hours = 0,
                Rate = input.BonusAmount,
                Amount = input.BonusAmount,
                Description = string.IsNullOrEmpty(input.BonusDescription) ? "Bonus" : input.BonusDescription
            });
            grossPay += input.BonusAmount;
        }

        // Commission
        if (input.CommissionAmount > 0)
        {
            earningLines.Add(new EarningLine
            {
                Type = EarningType.Commission,
                Hours = 0,
                Rate = input.CommissionAmount,
                Amount = input.CommissionAmount,
                Description = string.IsNullOrEmpty(input.CommissionDescription) ? "Commission" : input.CommissionDescription
            });
            grossPay += input.CommissionAmount;
        }

        // Calculate deductions and taxes
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

        // Calculate YTD totals
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

        var payStub = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRun.Id,
            HoursWorked = totalHours,
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

        // Add earning lines to the pay stub
        foreach (var line in earningLines)
        {
            payStub.EarningLines.Add(line);
        }

        return payStub;
    }
}
