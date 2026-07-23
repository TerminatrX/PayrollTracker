using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services.Tax;

/// <summary>
/// The W-4 inputs a federal withholding calculation depends on.
/// Mirrors Form W-4 (2020 and later). A pre-2020 W-4 must be converted before use.
/// </summary>
public sealed record FederalW4Info
{
    /// <summary>Step 1(c) filing status.</summary>
    public FilingStatus FilingStatus { get; init; } = FilingStatus.Single;

    /// <summary>Step 2(c) checkbox - multiple jobs / spouse works.</summary>
    public bool MultipleJobsChecked { get; init; }

    /// <summary>Step 3 - annual credit for dependents and other credits.</summary>
    public decimal DependentsAndOtherCredits { get; init; }

    /// <summary>Step 4(a) - other annual income not from jobs.</summary>
    public decimal OtherIncome { get; init; }

    /// <summary>Step 4(b) - annual deductions beyond the standard deduction.</summary>
    public decimal Deductions { get; init; }

    /// <summary>Step 4(c) - additional flat amount to withhold each pay period.</summary>
    public decimal ExtraWithholdingPerPeriod { get; init; }
}

/// <summary>
/// Federal income tax withholding using the IRS Publication 15-T Percentage Method for
/// Automated Payroll Systems (Worksheet 1A).
///
/// This replaces the previous flat-percentage model, which mis-withheld for essentially
/// every employee. Worksheet 1A step numbering is preserved in the comments so the code can
/// be checked line-by-line against the published worksheet.
/// </summary>
public static class FederalWithholdingCalculator
{
    /// <summary>
    /// Computes federal income tax to withhold for one pay period.
    /// </summary>
    /// <param name="taxableWagesThisPeriod">
    /// Wages for the period after pre-tax deductions that reduce federal taxable wages
    /// (§125 premiums and 401(k) deferrals both reduce this).
    /// </param>
    /// <param name="payPeriodsPerYear">Number of pay periods in the year (26 for biweekly).</param>
    /// <param name="w4">The employee's Form W-4 data.</param>
    /// <param name="year">Year of the pay date, used to select the published tables.</param>
    public static decimal Calculate(
        decimal taxableWagesThisPeriod,
        int payPeriodsPerYear,
        FederalW4Info w4,
        int year)
    {
        if (payPeriodsPerYear <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payPeriodsPerYear), payPeriodsPerYear, "Pay periods per year must be positive.");
        }

        // ── Step 1: Adjust the employee's wage amount ──────────────────────────
        // 1a/1b/1c: annualize this period's wages.
        var annualWages = taxableWagesThisPeriod * payPeriodsPerYear;

        // 1d/1e: add Step 4(a) other income.
        var annualWagesPlusOtherIncome = annualWages + w4.OtherIncome;

        // 1f: Step 4(b) deductions.
        // 1g: standard-deduction adjustment, but only when Step 2 is NOT checked - the
        //     Step-2-checked schedules already have it built in at half value.
        var step1g = w4.MultipleJobsChecked
            ? 0m
            : FederalWithholdingTables.GetStandardDeductionAdjustment(year, w4.FilingStatus);

        // 1h/1i: adjusted annual wage amount, floored at zero.
        var adjustedAnnualWage = Math.Max(0m, annualWagesPlusOtherIncome - (w4.Deductions + step1g));

        // ── Step 2: Figure the tentative withholding amount ────────────────────
        var schedule = FederalWithholdingTables.GetSchedule(year, w4.FilingStatus, w4.MultipleJobsChecked);
        var tentativeAnnual = schedule.ComputeAnnualWithholding(adjustedAnnualWage);

        // 2i: back to a per-period amount.
        var tentativePerPeriod = tentativeAnnual / payPeriodsPerYear;

        // ── Step 3: Account for tax credits ────────────────────────────────────
        // 3a/3b: Step 3 credits are an ANNUAL amount, so they are spread across periods.
        var creditsPerPeriod = w4.DependentsAndOtherCredits / payPeriodsPerYear;

        // 3c: credits cannot drive withholding below zero - the employer never refunds here.
        var afterCredits = Math.Max(0m, tentativePerPeriod - creditsPerPeriod);

        // ── Step 4: Figure the final amount to withhold ────────────────────────
        // 4a/4b: Step 4(c) extra withholding is added AFTER the zero floor, because an
        // employee who requests extra withholding gets it even when the table yields zero.
        return Money.Round(afterCredits + w4.ExtraWithholdingPerPeriod);
    }
}
