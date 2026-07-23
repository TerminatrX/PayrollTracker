namespace PayrollManager.Domain.Services.Tax;

/// <summary>
/// Statutory federal payroll figures for a single tax year.
///
/// These are law, not preferences - they must never be user-editable, and they must be keyed
/// by the year of the PAY DATE (constructive receipt), not the period worked.
///
/// Every value carries a source citation on the year it is defined in
/// <see cref="StaticFederalTaxRuleProvider"/>. When adding a year, cite the primary source
/// (IRS/SSA) - do not copy figures from memory or from secondary reporting.
/// </summary>
public sealed record FederalTaxRules
{
    public required int Year { get; init; }

    /// <summary>OASDI contribution and benefit base - wages above this are not SS-taxable.</summary>
    public required decimal SocialSecurityWageBase { get; init; }

    /// <summary>Employee Social Security rate, as a fraction (0.062 = 6.2%).</summary>
    public required decimal SocialSecurityRate { get; init; }

    /// <summary>Employee Medicare rate, as a fraction (0.0145 = 1.45%). No wage base applies.</summary>
    public required decimal MedicareRate { get; init; }

    /// <summary>
    /// Wage threshold above which Additional Medicare Tax is withheld.
    /// An employer withholds at this threshold WITHOUT REGARD TO FILING STATUS; the employee
    /// reconciles the filing-status thresholds on Form 8959. Do not make this filing-aware.
    /// </summary>
    public required decimal AdditionalMedicareThreshold { get; init; }

    /// <summary>Additional Medicare rate (0.009 = 0.9%). Employee only - no employer match.</summary>
    public required decimal AdditionalMedicareRate { get; init; }

    /// <summary>IRC §402(g) elective deferral limit for the year (401(k) employee contributions).</summary>
    public required decimal ElectiveDeferralLimit { get; init; }

    /// <summary>Age-50+ catch-up contribution allowance, on top of the §402(g) limit.</summary>
    public required decimal CatchUpContributionLimit { get; init; }

    /// <summary>FUTA taxable wage base per employee per year (employer-paid).</summary>
    public required decimal FutaWageBase { get; init; }

    /// <summary>Gross FUTA rate before the state unemployment credit.</summary>
    public required decimal FutaGrossRate { get; init; }

    /// <summary>Standard FUTA credit for timely state unemployment contributions.</summary>
    public required decimal FutaStandardCredit { get; init; }

    /// <summary>Effective FUTA rate in a state with no credit reduction (typically 0.6%).</summary>
    public decimal FutaNetRate => FutaGrossRate - FutaStandardCredit;
}

/// <summary>
/// Thrown when payroll is calculated for a year with no verified statutory figures loaded.
/// Failing loudly is deliberate: silently reusing another year's wage base or deferral limit
/// would mis-withhold for every employee without any visible symptom.
/// </summary>
public sealed class TaxRulesNotAvailableException : InvalidOperationException
{
    public int Year { get; }

    public TaxRulesNotAvailableException(int year, IEnumerable<int> availableYears)
        : base($"No verified federal tax rules are loaded for {year}. " +
               $"Available years: {string.Join(", ", availableYears)}. " +
               "Add the year to StaticFederalTaxRuleProvider with a primary-source citation " +
               "before running payroll with a pay date in that year.")
    {
        Year = year;
    }
}

public interface ITaxRuleProvider
{
    /// <summary>
    /// Returns statutory federal figures for the given tax year.
    /// </summary>
    /// <exception cref="TaxRulesNotAvailableException">The year has no verified figures loaded.</exception>
    FederalTaxRules GetFederalRules(int year);

    /// <summary>Years for which verified figures are loaded.</summary>
    IReadOnlyCollection<int> AvailableYears { get; }
}

/// <summary>
/// Compiled-in federal tax rules, one entry per verified year.
///
/// Verified 2026-07-21 against primary sources:
///   - Social Security wage base: IRS Topic no. 751 (https://www.irs.gov/taxtopics/tc751)
///     and SSA Contribution and Benefit Base (https://www.ssa.gov/oact/cola/cbb.html)
///   - §402(g) elective deferral limits: IRS Notice 2025-67 and IRS newsroom release
///     "401(k) limit increases to $24,500 for 2026"
///     (https://www.irs.gov/newsroom/401k-limit-increases-to-24500-for-2026-ira-limit-increases-to-7500)
///   - FICA rates and Additional Medicare Tax: IRS Topic no. 751
///   - FUTA: IRS Publication 15 (Circular E)
///
/// IMPORTANT: re-verify every January. These figures change annually.
/// </summary>
public sealed class StaticFederalTaxRuleProvider : ITaxRuleProvider
{
    // Rates below are statutory and have been stable for many years, but are still stored
    // per-year so that a future change is expressed as data rather than as a code edit.
    private const decimal EmployeeSocialSecurityRate = 0.062m;
    private const decimal EmployeeMedicareRate = 0.0145m;
    private const decimal AdditionalMedicareRateValue = 0.009m;
    private const decimal AdditionalMedicareThresholdValue = 200_000m;
    private const decimal FutaWageBaseValue = 7_000m;
    private const decimal FutaGrossRateValue = 0.060m;
    private const decimal FutaStandardCreditValue = 0.054m;

    private static readonly IReadOnlyDictionary<int, FederalTaxRules> Rules =
        new Dictionary<int, FederalTaxRules>
        {
            [2024] = Build(2024, socialSecurityWageBase: 168_600m, electiveDeferralLimit: 23_000m, catchUp: 7_500m),
            [2025] = Build(2025, socialSecurityWageBase: 176_100m, electiveDeferralLimit: 23_500m, catchUp: 7_500m),
            [2026] = Build(2026, socialSecurityWageBase: 184_500m, electiveDeferralLimit: 24_500m, catchUp: 8_000m),
        };

    private static FederalTaxRules Build(
        int year, decimal socialSecurityWageBase, decimal electiveDeferralLimit, decimal catchUp) =>
        new()
        {
            Year = year,
            SocialSecurityWageBase = socialSecurityWageBase,
            SocialSecurityRate = EmployeeSocialSecurityRate,
            MedicareRate = EmployeeMedicareRate,
            AdditionalMedicareThreshold = AdditionalMedicareThresholdValue,
            AdditionalMedicareRate = AdditionalMedicareRateValue,
            ElectiveDeferralLimit = electiveDeferralLimit,
            CatchUpContributionLimit = catchUp,
            FutaWageBase = FutaWageBaseValue,
            FutaGrossRate = FutaGrossRateValue,
            FutaStandardCredit = FutaStandardCreditValue
        };

    public IReadOnlyCollection<int> AvailableYears => (IReadOnlyCollection<int>)Rules.Keys;

    public FederalTaxRules GetFederalRules(int year)
    {
        if (!Rules.TryGetValue(year, out var rules))
        {
            throw new TaxRulesNotAvailableException(year, Rules.Keys.OrderBy(y => y));
        }

        return rules;
    }
}
