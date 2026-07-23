using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services.Tax;

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

    /// <summary>Employer-paid taxes for this period. A company cost, never withheld.</summary>
    public decimal EmployerSocialSecurity { get; set; }
    public decimal EmployerMedicare { get; set; }
    public decimal EmployerFuta { get; set; }
    public decimal EmployerSui { get; set; }

    public decimal TotalEmployerTaxes =>
        EmployerSocialSecurity + EmployerMedicare + EmployerFuta + EmployerSui;

    /// <summary>Conditions raised while computing employer taxes, e.g. SUI not configured.</summary>
    public IReadOnlyList<string> EmployerTaxWarnings { get; set; } = Array.Empty<string>();
}

public class PayrollService
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettingsService _companySettingsService;
    private readonly ITaxRuleProvider _taxRuleProvider;

    // Constants
    private const decimal OvertimeMultiplier = 1.5m;
    private const decimal StandardHoursPerPeriod = 40m;

    // Annual statutory limits (wage base, §402(g) limit, Additional Medicare threshold) are
    // NOT constants here - they change every year and are supplied per pay-date year by
    // ITaxRuleProvider. See Services/Tax/FederalTaxRules.cs.

    public PayrollService(
        AppDbContext dbContext,
        CompanySettingsService companySettingsService,
        ITaxRuleProvider? taxRuleProvider = null)
    {
        _dbContext = dbContext;
        _companySettingsService = companySettingsService;
        _taxRuleProvider = taxRuleProvider ?? new StaticFederalTaxRuleProvider();
    }

    /// <summary>
    /// Generates a pay stub from a pay-period hours TOTAL, splitting it into regular and
    /// overtime by assuming the hours were spread evenly across the period's workweeks.
    ///
    /// Replaces a previous overload that split the period total at 40 hours, which treated
    /// half of an ordinary 80-hour biweekly period as overtime.
    ///
    /// Only use this when hours really were even across workweeks. Otherwise split actual
    /// per-workweek hours with <see cref="FlsaOvertime.SplitByWorkweek"/> and pass explicit
    /// regular/overtime hours via <see cref="PayStubInput"/>.
    /// </summary>
    public async Task<PayStub> GeneratePayStubFromPeriodHoursAsync(
        Employee employee,
        PayRun payRun,
        decimal totalPeriodHours)
    {
        var settings = await _companySettingsService.GetSettingsAsync();
        var workweeks = FlsaOvertime.WorkweeksInPeriod(settings.PayPeriodsPerYear);
        var (regularHours, overtimeHours) = FlsaOvertime.SplitEvenlyAcrossWorkweeks(totalPeriodHours, workweeks);

        return await GeneratePayStubAsync(employee, payRun, new PayStubInput
        {
            RegularHours = regularHours,
            OvertimeHours = overtimeHours
        });
    }

    /// <summary>
    /// Year-to-date figures from an employee's already-posted stubs, as of a pay date.
    /// </summary>
    private sealed record YtdPriors(
        decimal Gross,
        decimal Taxes,
        decimal Contribution401k,
        decimal SocialSecurityWages,
        decimal MedicareWages,
        decimal NetPay);

    /// <summary>
    /// Loads year-to-date priors for an employee. Shared by preview and generation so the two
    /// can never drift apart - the preview a user approves must match what is posted.
    /// </summary>
    private async Task<YtdPriors> GetYtdPriorsAsync(int employeeId, DateTime payDate)
    {
        var year = payDate.Year;
        var priorPayStubs = await _dbContext.PayStubs
            .Include(ps => ps.TaxLines)
            .Where(ps => ps.EmployeeId == employeeId &&
                         ps.PayRun!.PayDate.Year == year &&
                         ps.PayRun.PayDate < payDate)
            .ToListAsync();

        return new YtdPriors(
            Gross: priorPayStubs.Sum(ps => ps.GrossPay),
            Taxes: priorPayStubs.Sum(ps => ps.TotalTaxes),
            Contribution401k: priorPayStubs.Sum(ps => ps.PreTax401kDeduction),
            SocialSecurityWages: SumTaxableWages(priorPayStubs, TaxType.SocialSecurity),
            MedicareWages: SumTaxableWages(priorPayStubs, TaxType.Medicare),
            NetPay: priorPayStubs.Sum(ps => ps.NetPay));
    }

    /// <summary>
    /// Sums the wages a given tax was actually assessed on across prior stubs.
    /// </summary>
    private static decimal SumTaxableWages(IEnumerable<PayStub> stubs, TaxType type) =>
        stubs.Sum(ps =>
        {
            var line = ps.TaxLines.FirstOrDefault(t => t.Type == type);

            // Stubs predating the tax-line migration (20260119200000_AddDeductionAndTaxLines)
            // carry no lines. Those were computed on gross, so gross is the faithful basis for
            // them - reconstructing history differently would restate what was already paid.
            return line?.TaxableAmount ?? ps.GrossPay;
        });

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
        public EmployerTaxResult EmployerTaxes { get; set; } = new();
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
        decimal ytdSocialSecurityWagesPrior,
        decimal ytdMedicareWagesPrior,
        decimal priorNetPaySum)
    {
        // Get latest settings from service (cached, but always current)
        var companySettings = await _companySettingsService.GetSettingsAsync();

        // Statutory limits are keyed by the year of the PAY DATE (constructive receipt).
        // Throws if the year has no verified figures rather than silently reusing another year.
        var rules = _taxRuleProvider.GetFederalRules(payDate.Year);

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
                var regularAmount = Money.Round(input.RegularHours * employee.HourlyRate);
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
                var overtimeAmount = Money.Round(input.OvertimeHours * overtimeRate);
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
            var salaryPerPeriod = Money.Round(employee.AnnualSalary / payPeriods);
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
            var bonusAmount = Money.Round(input.BonusAmount);
            earningLines.Add(new EarningLine
            {
                Type = EarningType.Bonus,
                Hours = 0,
                Rate = bonusAmount,
                Amount = bonusAmount,
                Description = string.IsNullOrEmpty(input.BonusDescription) ? "Bonus" : input.BonusDescription
            });
            grossPay += bonusAmount;
        }

        // Commission
        if (input.CommissionAmount > 0)
        {
            var commissionAmount = Money.Round(input.CommissionAmount);
            earningLines.Add(new EarningLine
            {
                Type = EarningType.Commission,
                Hours = 0,
                Rate = commissionAmount,
                Amount = commissionAmount,
                Description = string.IsNullOrEmpty(input.CommissionDescription) ? "Commission" : input.CommissionDescription
            });
            grossPay += commissionAmount;
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 2: CALCULATE 401K DEDUCTION (Pre-Tax)
        // ═══════════════════════════════════════════════════════════════
        var deductionLines = new List<DeductionLine>();
        
        // Calculate 401k contribution for this period
        var requested401k = Money.Round(grossPay * (employee.PreTax401kPercent / 100m));

        // Check annual limit (§402(g), year-indexed)
        var remaining401kLimit = Math.Max(0m, rules.ElectiveDeferralLimit - ytd401kPrior);
        var preTax401k = Money.Round(Math.Min(requested401k, remaining401kLimit));
        
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
                Amount = Money.Round(employee.HealthInsurancePerPeriod),
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

        // FICA wages are gross LESS §125 cafeteria-plan premiums (health/dental/vision).
        // 401(k) deferrals do NOT reduce FICA wages - see DeductionTypeExtensions.ReducesFicaWages.
        var ficaExemptDeductions = deductionLines
            .Where(d => d.IsPreTax && d.Type.ReducesFicaWages())
            .Sum(d => d.Amount);
        // Clamped at zero: if pre-tax deductions exceed gross (e.g. a zero-hours period), FICA
        // wages are zero, not negative. A negative basis would emit negative FICA withholding.
        var ficaWages = Money.Round(Math.Max(0m, grossPay - ficaExemptDeductions));

        // Social Security (6.2% up to the year's wage base).
        // The wage base is tracked against YTD SS-TAXABLE wages, not YTD gross - tracking it
        // against gross would retire the base early for anyone with §125 deductions.
        var remainingWageBase = Math.Max(0, rules.SocialSecurityWageBase - ytdSocialSecurityWagesPrior);
        var socialSecurityTaxableAmount = Math.Max(0m, Math.Min(ficaWages, remainingWageBase));
        var taxSocialSecurity = Money.Round(socialSecurityTaxableAmount * (companySettings.SocialSecurityPercent / 100m));

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

        // Medicare (1.45% on all FICA wages - no wage base - plus 0.9% Additional Medicare
        // Tax on wages above the statutory threshold).
        var medicareBaseRate = companySettings.MedicarePercent / 100m;
        var medicareBaseTax = ficaWages * medicareBaseRate;

        // Additional Medicare Tax is withheld at the threshold WITHOUT REGARD TO FILING STATUS.
        // The employee reconciles filing-status thresholds on Form 8959; the employer does not.
        var additionalMedicareTax = 0m;
        var ytdMedicareWagesAfter = ytdMedicareWagesPrior + ficaWages;

        if (ytdMedicareWagesAfter > rules.AdditionalMedicareThreshold)
        {
            var wagesOverThreshold = ytdMedicareWagesAfter - rules.AdditionalMedicareThreshold;
            var priorWagesOverThreshold = Math.Max(0, ytdMedicareWagesPrior - rules.AdditionalMedicareThreshold);
            var currentPeriodOverThreshold = wagesOverThreshold - priorWagesOverThreshold;

            additionalMedicareTax = currentPeriodOverThreshold * rules.AdditionalMedicareRate;
        }

        var taxMedicare = Money.Round(medicareBaseTax + additionalMedicareTax);

        if (taxMedicare > 0)
        {
            taxLines.Add(new TaxLine
            {
                Type = TaxType.Medicare,
                Amount = taxMedicare,
                Rate = companySettings.MedicarePercent + (additionalMedicareTax > 0 ? 0.9m : 0m),
                TaxableAmount = ficaWages,
                Description = additionalMedicareTax > 0
                    ? $"Medicare ({companySettings.MedicarePercent}% + 0.9% Additional)"
                    : $"Medicare ({companySettings.MedicarePercent}%)"
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // STEP 4: CALCULATE FEDERAL AND STATE INCOME TAXES
        // ═══════════════════════════════════════════════════════════════
        // Income-tax wages are floored at zero. A period where pre-tax deductions exceed gross
        // must withhold nothing, not produce a negative (which would offset other employees'
        // liability once these rows are summed into a tax report).
        var incomeTaxWages = Math.Max(0m, taxableIncome);

        // Federal: IRS Pub 15-T Percentage Method (Worksheet 1A), replacing the previous flat
        // percentage, which mis-withheld for essentially every employee.
        var w4 = new FederalW4Info
        {
            FilingStatus = employee.FilingStatus,
            MultipleJobsChecked = employee.W4MultipleJobsChecked,
            DependentsAndOtherCredits = employee.W4DependentsAndOtherCredits,
            OtherIncome = employee.W4OtherIncome,
            Deductions = employee.W4Deductions,
            ExtraWithholdingPerPeriod = employee.W4ExtraWithholding
        };

        var taxFederal = FederalWithholdingCalculator.Calculate(
            incomeTaxWages, payPeriods, w4, payDate.Year);

        taxLines.Add(new TaxLine
        {
            Type = TaxType.FederalIncome,
            Amount = taxFederal,
            // Effective rate, for display only - federal withholding is bracket-based, so
            // there is no single statutory "rate" to record here.
            Rate = incomeTaxWages > 0 ? Money.Round(taxFederal / incomeTaxWages * 100m) : 0m,
            TaxableAmount = incomeTaxWages,
            Description = $"Federal Income Tax ({employee.FilingStatus})"
        });

        // Illinois: flat 4.95% less IL-W-4 allowances (Booklet IL-700-T).
        var ilW4 = new IllinoisW4Info(employee.IlBasicAllowances, employee.IlAdditionalAllowances);
        var illinoisRules = IllinoisWithholdingCalculator.GetRules(payDate.Year);

        var taxState = IllinoisWithholdingCalculator.Calculate(
            incomeTaxWages, payPeriods, ilW4, payDate.Year);

        taxLines.Add(new TaxLine
        {
            Type = TaxType.StateIncome,
            Amount = taxState,
            Rate = illinoisRules.Rate * 100m,
            TaxableAmount = incomeTaxWages,
            Description = $"IL Income Tax ({illinoisRules.Rate * 100m:0.##}%)"
        });

        // ═══════════════════════════════════════════════════════════════
        // STEP 5: CALCULATE POST-TAX DEDUCTIONS
        // ═══════════════════════════════════════════════════════════════
        if (employee.OtherDeductionsPerPeriod > 0)
        {
            deductionLines.Add(new DeductionLine
            {
                Type = DeductionType.OtherPostTax,
                Amount = Money.Round(employee.OtherDeductionsPerPeriod),
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

        // ═══════════════════════════════════════════════════════════════
        // STEP 8: EMPLOYER-PAID TAXES (a company cost, NOT withheld from the employee)
        // ═══════════════════════════════════════════════════════════════
        // Medicare wages are uncapped, so they are the correct YTD basis for the unemployment
        // wage bases; Social Security wages stop accumulating at the SS base.
        var employerTaxes = EmployerTaxCalculator.Calculate(
            ficaWages, ytdSocialSecurityWagesPrior, ytdMedicareWagesPrior, companySettings, rules);

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
            EmployerTaxes = employerTaxes,
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
        var priors = await GetYtdPriorsAsync(employee.Id, draft.PayDate);

        // Use shared calculation logic
        var result = await CalculatePayStubAsync(
            employee,
            input,
            draft.PayDate,
            priors.Gross,
            priors.Taxes,
            priors.Contribution401k,
            priors.SocialSecurityWages,
            priors.MedicareWages,
            priors.NetPay);

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
            YtdNet = result.YtdNet,
            EmployerSocialSecurity = result.EmployerTaxes.SocialSecurityMatch,
            EmployerMedicare = result.EmployerTaxes.MedicareMatch,
            EmployerFuta = result.EmployerTaxes.Futa,
            EmployerSui = result.EmployerTaxes.Sui,
            EmployerTaxWarnings = result.EmployerTaxes.Warnings
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
        var priors = await GetYtdPriorsAsync(employee.Id, payRun.PayDate);

        // Use shared calculation logic
        var result = await CalculatePayStubAsync(
            employee,
            input,
            payRun.PayDate,
            priors.Gross,
            priors.Taxes,
            priors.Contribution401k,
            priors.SocialSecurityWages,
            priors.MedicareWages,
            priors.NetPay);

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
            YtdNet = result.YtdNet,
            EmployerSocialSecurity = result.EmployerTaxes.SocialSecurityMatch,
            EmployerMedicare = result.EmployerTaxes.MedicareMatch,
            EmployerFuta = result.EmployerTaxes.Futa,
            EmployerSui = result.EmployerTaxes.Sui
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
