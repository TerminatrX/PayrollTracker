namespace PayrollManager.Domain.Services.Tax;

/// <summary>
/// Illinois withholding allowances, from Form IL-W-4.
/// </summary>
/// <param name="BasicAllowances">
/// IL-W-4 Line 1: allowances for the employee, spouse, and dependents.
/// </param>
/// <param name="AdditionalAllowances">
/// IL-W-4 Line 2: additional allowances for age 65 or older and/or legally blind.
/// </param>
public sealed record IllinoisW4Info(int BasicAllowances = 0, int AdditionalAllowances = 0);

/// <summary>
/// Illinois statutory withholding figures for a tax year.
/// </summary>
public sealed record IllinoisTaxRules
{
    public required int Year { get; init; }

    /// <summary>Flat Illinois income tax rate as a fraction (0.0495 = 4.95%).</summary>
    public required decimal Rate { get; init; }

    /// <summary>Annual value of one IL-W-4 Line 1 basic allowance.</summary>
    public required decimal BasicAllowanceAmount { get; init; }

    /// <summary>Annual value of one IL-W-4 Line 2 additional allowance.</summary>
    public required decimal AdditionalAllowanceAmount { get; init; }
}

/// <summary>
/// Illinois income tax withholding, per Booklet IL-700-T.
///
/// Illinois is a flat-rate state, so the whole calculation is:
///
///     withheld = rate x (wages - (annual allowance value / pay periods per year))
///
/// Source: Illinois Department of Revenue, 2026 Booklet IL-700-T
/// (https://tax.illinois.gov/forms/withholding/currentyear/il-700-t-withholding-guide-tables.html)
/// Verified 2026-07-21.
///
/// Illinois taxable wages follow the federal taxable wage base, so both §125 premiums and
/// 401(k) deferrals reduce the amount passed in here.
/// </summary>
public static class IllinoisWithholdingCalculator
{
    private static readonly IReadOnlyDictionary<int, IllinoisTaxRules> Rules =
        new Dictionary<int, IllinoisTaxRules>
        {
            [2026] = new()
            {
                Year = 2026,
                Rate = 0.0495m,
                BasicAllowanceAmount = 2_925m,
                AdditionalAllowanceAmount = 1_000m
            }
        };

    public static IllinoisTaxRules GetRules(int year)
    {
        if (!Rules.TryGetValue(year, out var rules))
        {
            throw new TaxRulesNotAvailableException(year, Rules.Keys.OrderBy(y => y));
        }

        return rules;
    }

    /// <summary>
    /// Computes Illinois income tax to withhold for one pay period.
    /// </summary>
    /// <param name="taxableWagesThisPeriod">Period wages after pre-tax deductions.</param>
    /// <param name="payPeriodsPerYear">Number of pay periods in the year.</param>
    /// <param name="ilW4">The employee's IL-W-4 allowances.</param>
    /// <param name="year">Year of the pay date.</param>
    public static decimal Calculate(
        decimal taxableWagesThisPeriod,
        int payPeriodsPerYear,
        IllinoisW4Info ilW4,
        int year)
    {
        if (payPeriodsPerYear <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payPeriodsPerYear), payPeriodsPerYear, "Pay periods per year must be positive.");
        }

        var rules = GetRules(year);

        var annualAllowanceValue =
            ilW4.BasicAllowances * rules.BasicAllowanceAmount +
            ilW4.AdditionalAllowances * rules.AdditionalAllowanceAmount;

        var allowancePerPeriod = annualAllowanceValue / payPeriodsPerYear;

        // Floored at zero: allowances exceeding wages withhold nothing, never a negative.
        var taxableAfterAllowances = Math.Max(0m, taxableWagesThisPeriod - allowancePerPeriod);

        return Money.Round(taxableAfterAllowances * rules.Rate);
    }
}
