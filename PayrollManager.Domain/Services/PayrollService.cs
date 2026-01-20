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

/// <summary>
/// Represents a draft pay run with period dates for preview calculations.
/// </summary>
public class PayRunDraft
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime PayDate { get; set; }
}

/// <summary>
/// Preview result of pay stub calculation without saving to database.
/// Contains the same calculated values that would be saved.
/// </summary>
public class PayStubPreview
{
    public decimal GrossPay { get; set; }
    public decimal PreTax401kDeduction { get; set; }
    public decimal TaxFederal { get; set; }
    public decimal TaxState { get; set; }
    public decimal TaxSocialSecurity { get; set; }
    public decimal TaxMedicare { get; set; }
    public decimal PostTaxDeductions { get; set; }
    public decimal TotalTaxes { get; set; }
    public decimal NetPay { get; set; }
    public decimal YtdGross { get; set; }
    public decimal YtdTaxes { get; set; }
    public decimal YtdNet { get; set; }
}

public class PayrollService
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettingsService _companySettingsService;

    // Constants
    private const decimal OvertimeMultiplier = 1.5m;
    private const decimal StandardHoursPerPeriod = 40m;
    
    // 2024 Tax Limits (update annually)
    private const decimal SocialSecurityWageBase = 168600m; // Social Security wage base for 2024
    private const decimal MedicareAdditionalThreshold = 200000m; // Additional 0.9% Medicare tax threshold
    private const decimal MedicareAdditionalRate = 0.009m; // Additional Medicare tax rate
    private const decimal Annual401kLimit = 23000m; // 2024 401(k) contribution limit

    public PayrollService(AppDbContext dbContext, CompanySettingsService companySettingsService)
    {
        _dbContext = dbContext;
        _companySettingsService = companySettingsService;
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
    /// Internal calculation result used by both preview and generation.
    /// </summary>
    private class PayStubCalculationResult
    {
        public decimal GrossPay { get; set; }
        public decimal TotalHours { get; set; }
        public decimal PreTax401k { get; set; }
        public decimal PreTaxDeductions { get; set; }
        public decimal TaxableIncome { get; set; }
        public decimal TaxFederal { get; set; }
        public decimal TaxState { get; set; }
        public decimal TaxSocialSecurity { get; set; }
        public decimal TaxMedicare { get; set; }
        public decimal PostTaxDeductions { get; set; }
        public decimal TotalTaxes { get; set; }
        public decimal NetPay { get; set; }
        public decimal YtdGross { get; set; }
        public decimal YtdTaxes { get; set; }
        public decimal YtdNet { get; set; }
        public List<EarningLine> EarningLines { get; set; } = new();
        public List<DeductionLine> DeductionLines { get; set; } = new();
        public List<TaxLine> TaxLines { get; set; } = new();
    }

    /// <summary>
    /// Core calculation logic shared between preview and generation.
    /// </summary>
    private async Task<PayStubCalculationResult> CalculatePayStubAsync(
        Employee employee,
        PayStubInput input,
        DateTime payDate,
        decimal ytdGrossPrior,
        decimal ytdTaxesPrior,
        decimal ytd401kPrior,
        decimal ytdSocialSecurityPrior,
        decimal ytdMedicarePrior,
        decimal priorNetPaySum)
    {
        // Get latest settings from service (cached, but always current)
        var companySettings = await _companySettingsService.GetSettingsAsync();
        
        // Ensure biweekly frequency (26 periods per year)
        var payPeriods = companySettings.PayPeriodsPerYear > 0
            ? companySettings.PayPeriodsPerYear
            : 26; // Default to biweekly

        // ═══════════════════════════════════════════════════════════════
        // STEP 1: CALCULATE GROSS PAY
        // ═══════════════════════════════════════════════════════════════
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
            // Salary employee - biweekly calculation
            var salaryPerPeriod = employee.AnnualSalary / payPeriods;
            earningLines.Add(new EarningLine
            {
                Type = EarningType.Regular,
                Hours = 0, // Salary employees don't track hours
                Rate = salaryPerPeriod,
                Amount = salaryPerPeriod,
                Description = $"Biweekly Salary ({payPeriods} periods/year)"
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

        // ═══════════════════════════════════════════════════════════════
        // STEP 2: CALCULATE 401K DEDUCTION (Pre-Tax)
        // ═══════════════════════════════════════════════════════════════
        var deductionLines = new List<DeductionLine>();
        
        // Calculate 401k contribution for this period
        var requested401k = grossPay * (employee.PreTax401kPercent / 100m);
        
        // Check annual limit
        var remaining401kLimit = Annual401kLimit - ytd401kPrior;
        var preTax401k = Math.Min(requested401k, remaining401kLimit);
        
        if (preTax401k > 0)
        {
            deductionLines.Add(new DeductionLine
            {
                Type = DeductionType.PreTax401k,
                Amount = preTax401k,
                Description = $"401(k) Contribution ({employee.PreTax401kPercent}%)",
                IsPreTax = true
            });
        }

        // Add health insurance (pre-tax)
        if (employee.HealthInsurancePerPeriod > 0)
        {
            deductionLines.Add(new DeductionLine
            {
                Type = DeductionType.HealthInsurance,
                Amount = employee.HealthInsurancePerPeriod,
                Description = "Health Insurance",
                IsPreTax = true
            });
        }

        // Calculate taxable income (gross minus pre-tax deductions)
        var preTaxDeductions = deductionLines.Where(d => d.IsPreTax).Sum(d => d.Amount);
        var taxableIncome = grossPay - preTaxDeductions;

        // ═══════════════════════════════════════════════════════════════
        // STEP 3: CALCULATE FICA TAXES (Social Security + Medicare)
        // ═══════════════════════════════════════════════════════════════
        var taxLines = new List<TaxLine>();

        // Social Security (6.2% up to wage base)
        // Wage base applies to gross earnings (before pre-tax deductions)
        var ytdGrossForSS = ytdGrossPrior;
        var remainingWageBase = Math.Max(0, SocialSecurityWageBase - ytdGrossForSS);
        var socialSecurityTaxableAmount = Math.Min(grossPay, remainingWageBase);
        var taxSocialSecurity = socialSecurityTaxableAmount * (companySettings.SocialSecurityPercent / 100m);

        if (taxSocialSecurity > 0)
        {
            taxLines.Add(new TaxLine
            {
                Type = TaxType.SocialSecurity,
                Amount = taxSocialSecurity,
                Rate = companySettings.SocialSecurityPercent,
                TaxableAmount = socialSecurityTaxableAmount,
                Description = $"Social Security ({companySettings.SocialSecurityPercent}%)"
            });
        }

        // Medicare (1.45% on all earnings, +0.9% on earnings over $200k)
        var medicareBaseRate = companySettings.MedicarePercent / 100m;
        var medicareBaseTax = grossPay * medicareBaseRate;

        // Additional Medicare tax (0.9%) on earnings over $200k
        var additionalMedicareTax = 0m;
        var ytdGrossForMedicare = ytdGrossPrior + grossPay;
        
        if (ytdGrossForMedicare > MedicareAdditionalThreshold)
        {
            var grossOverThreshold = Math.Max(0, ytdGrossForMedicare - MedicareAdditionalThreshold);
            var priorGrossOverThreshold = Math.Max(0, ytdGrossPrior - MedicareAdditionalThreshold);
            var currentPeriodOverThreshold = grossOverThreshold - priorGrossOverThreshold;
            
            additionalMedicareTax = currentPeriodOverThreshold * MedicareAdditionalRate;
        }

        var taxMedicare = medicareBaseTax + additionalMedicareTax;

        if (taxMedicare > 0)
        {
            taxLines.Add(new TaxLine
            {
                Type = TaxType.Medicare,
                Amount = taxMedicare,
                Rate = companySettings.MedicarePercent + (additionalMedicareTax > 0 ? 0.9m : 0m),
                TaxableAmount = grossPay,
                Description = additionalMedicareTax > 0
                    ? $"Medicare ({companySettings.MedicarePercent}% + 0.9% Additional)"
                    : $"Medicare ({companySettings.MedicarePercent}%)"
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 4: CALCULATE FEDERAL AND STATE INCOME TAXES
        // ═══════════════════════════════════════════════════════════════
        var taxFederal = taxableIncome * (companySettings.FederalTaxPercent / 100m);
        
        taxLines.Add(new TaxLine
        {
            Type = TaxType.FederalIncome,
            Amount = taxFederal,
            Rate = companySettings.FederalTaxPercent,
            TaxableAmount = taxableIncome,
            Description = $"Federal Income Tax ({companySettings.FederalTaxPercent}%)"
        });

        var taxState = taxableIncome * (companySettings.StateTaxPercent / 100m);
        
        taxLines.Add(new TaxLine
        {
            Type = TaxType.StateIncome,
            Amount = taxState,
            Rate = companySettings.StateTaxPercent,
            TaxableAmount = taxableIncome,
            Description = $"State Income Tax ({companySettings.StateTaxPercent}%)"
        });

        // ═══════════════════════════════════════════════════════════════
        // STEP 5: CALCULATE POST-TAX DEDUCTIONS
        // ═══════════════════════════════════════════════════════════════
        if (employee.OtherDeductionsPerPeriod > 0)
        {
            deductionLines.Add(new DeductionLine
            {
                Type = DeductionType.OtherPostTax,
                Amount = employee.OtherDeductionsPerPeriod,
                Description = "Other Deductions",
                IsPreTax = false
            });
        }

        var postTaxDeductions = deductionLines.Where(d => !d.IsPreTax).Sum(d => d.Amount);

        // ═══════════════════════════════════════════════════════════════
        // STEP 6: CALCULATE NET PAY
        // ═══════════════════════════════════════════════════════════════
        var totalTaxes = taxFederal + taxState + taxSocialSecurity + taxMedicare;
        var netPay = taxableIncome - totalTaxes - postTaxDeductions;

        // ═══════════════════════════════════════════════════════════════
        // STEP 7: CALCULATE YTD ACCUMULATIONS
        // ═══════════════════════════════════════════════════════════════
        var ytdGross = ytdGrossPrior + grossPay;
        var ytdTaxes = ytdTaxesPrior + totalTaxes;
        var ytdNet = priorNetPaySum + netPay;

        return new PayStubCalculationResult
        {
            GrossPay = grossPay,
            TotalHours = totalHours,
            PreTax401k = preTax401k,
            PreTaxDeductions = preTaxDeductions,
            TaxableIncome = taxableIncome,
            TaxFederal = taxFederal,
            TaxState = taxState,
            TaxSocialSecurity = taxSocialSecurity,
            TaxMedicare = taxMedicare,
            PostTaxDeductions = postTaxDeductions,
            TotalTaxes = totalTaxes,
            NetPay = netPay,
            YtdGross = ytdGross,
            YtdTaxes = ytdTaxes,
            YtdNet = ytdNet,
            EarningLines = earningLines,
            DeductionLines = deductionLines,
            TaxLines = taxLines
        };
    }

    /// <summary>
    /// Preview pay stub calculation without saving to database.
    /// Uses the same calculation logic as GeneratePayStubAsync to ensure accuracy.
    /// </summary>
    public async Task<PayStubPreview> PreviewPayStubAsync(
        Employee employee,
        PayRunDraft draft,
        PayStubInput input)
    {
        // Get YTD totals for calculations
        var year = draft.PayDate.Year;
        var priorPayStubs = await _dbContext.PayStubs
            .Where(ps => ps.EmployeeId == employee.Id && 
                         ps.PayRun!.PayDate.Year == year &&
                         ps.PayRun.PayDate < draft.PayDate)
            .ToListAsync();

        var ytdGrossPrior = priorPayStubs.Sum(ps => ps.GrossPay);
        var ytdTaxesPrior = priorPayStubs.Sum(ps => ps.TotalTaxes);
        var ytd401kPrior = priorPayStubs.Sum(ps => ps.PreTax401kDeduction);
        var ytdSocialSecurityPrior = priorPayStubs.Sum(ps => ps.TaxSocialSecurity);
        var ytdMedicarePrior = priorPayStubs.Sum(ps => ps.TaxMedicare);
        var priorNetPaySum = priorPayStubs.Sum(ps => ps.NetPay);

        // Use shared calculation logic
        var result = await CalculatePayStubAsync(
            employee,
            input,
            draft.PayDate,
            ytdGrossPrior,
            ytdTaxesPrior,
            ytd401kPrior,
            ytdSocialSecurityPrior,
            ytdMedicarePrior,
            priorNetPaySum);

        // Return preview (no database entities)
        return new PayStubPreview
        {
            GrossPay = result.GrossPay,
            PreTax401kDeduction = result.PreTax401k,
            TaxFederal = result.TaxFederal,
            TaxState = result.TaxState,
            TaxSocialSecurity = result.TaxSocialSecurity,
            TaxMedicare = result.TaxMedicare,
            PostTaxDeductions = result.PostTaxDeductions,
            TotalTaxes = result.TotalTaxes,
            NetPay = result.NetPay,
            YtdGross = result.YtdGross,
            YtdTaxes = result.YtdTaxes,
            YtdNet = result.YtdNet
        };
    }

    /// <summary>
    /// Generate a pay stub with detailed earning lines including overtime, bonus, and commission.
    /// Includes proper FICA calculations, 401k limits, YTD accumulations, and biweekly frequency support.
    /// Uses the same calculation logic as PreviewPayStubAsync to ensure consistency.
    /// </summary>
    public async Task<PayStub> GeneratePayStubAsync(
        Employee employee,
        PayRun payRun,
        PayStubInput input)
    {
        // Get YTD totals for calculations
        var year = payRun.PayDate.Year;
        var priorPayStubs = await _dbContext.PayStubs
            .Where(ps => ps.EmployeeId == employee.Id && 
                         ps.PayRun!.PayDate.Year == year &&
                         ps.PayRun.PayDate < payRun.PayDate)
            .ToListAsync();

        var ytdGrossPrior = priorPayStubs.Sum(ps => ps.GrossPay);
        var ytdTaxesPrior = priorPayStubs.Sum(ps => ps.TotalTaxes);
        var ytd401kPrior = priorPayStubs.Sum(ps => ps.PreTax401kDeduction);
        var ytdSocialSecurityPrior = priorPayStubs.Sum(ps => ps.TaxSocialSecurity);
        var ytdMedicarePrior = priorPayStubs.Sum(ps => ps.TaxMedicare);
        var priorNetPaySum = priorPayStubs.Sum(ps => ps.NetPay);

        // Use shared calculation logic
        var result = await CalculatePayStubAsync(
            employee,
            input,
            payRun.PayDate,
            ytdGrossPrior,
            ytdTaxesPrior,
            ytd401kPrior,
            ytdSocialSecurityPrior,
            ytdMedicarePrior,
            priorNetPaySum);

        // ═══════════════════════════════════════════════════════════════
        // CREATE PAY STUB ENTITY
        // ═══════════════════════════════════════════════════════════════
        var payStub = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRun.Id,
            HoursWorked = result.TotalHours,
            GrossPay = result.GrossPay,
            PreTax401kDeduction = result.PreTax401k,
            TaxFederal = result.TaxFederal,
            TaxState = result.TaxState,
            TaxSocialSecurity = result.TaxSocialSecurity,
            TaxMedicare = result.TaxMedicare,
            PostTaxDeductions = result.PostTaxDeductions,
            NetPay = result.NetPay,
            YtdGross = result.YtdGross,
            YtdTaxes = result.YtdTaxes,
            YtdNet = result.YtdNet
        };

        // Add earning lines
        foreach (var line in result.EarningLines)
        {
            payStub.EarningLines.Add(line);
        }

        // Add deduction lines
        foreach (var line in result.DeductionLines)
        {
            payStub.DeductionLines.Add(line);
        }

        // Add tax lines
        foreach (var line in result.TaxLines)
        {
            payStub.TaxLines.Add(line);
        }

        return payStub;
    }

    /// <summary>
    /// Calculate biweekly pay period dates from a given date
    /// </summary>
    public static (DateTime start, DateTime end) CalculateBiweeklyPeriod(DateTime referenceDate)
    {
        // Find the most recent Monday (or use reference date if it's Monday)
        var daysSinceMonday = ((int)referenceDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var periodStart = referenceDate.AddDays(-daysSinceMonday - 13); // Go back 2 weeks
        var periodEnd = periodStart.AddDays(13); // 14-day period (0-13 = 14 days)
        
        return (periodStart, periodEnd);
    }

    /// <summary>
    /// Get the next biweekly pay date from a reference date
    /// </summary>
    public static DateTime GetNextBiweeklyPayDate(DateTime referenceDate)
    {
        var (_, periodEnd) = CalculateBiweeklyPeriod(referenceDate);
        return periodEnd.AddDays(1); // Pay date is the day after period ends
    }
}
