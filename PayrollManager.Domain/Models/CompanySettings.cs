namespace PayrollManager.Domain.Models;

public class CompanySettings
{
    public int Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string CompanyAddress { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public decimal SocialSecurityPercent { get; set; } = 6.2m;

    public decimal MedicarePercent { get; set; } = 1.45m;

    public int PayPeriodsPerYear { get; set; } = 26;

    public int DefaultHoursPerPeriod { get; set; } = 80;

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYER UNEMPLOYMENT TAX
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Illinois unemployment (SUI) contribution rate as a percentage, from the employer's
    /// annual IDES rate notice.
    ///
    /// There is no correct default: IDES assigns this per employer based on benefit-ratio
    /// experience, and it changes every year. Zero means "not yet entered" and produces a
    /// warning rather than a silently wrong employer liability.
    /// </summary>
    public decimal SuiRatePercent { get; set; }

    /// <summary>
    /// Illinois SUI taxable wage base per employee per year, from the IDES rate notice.
    /// Also employer-specific to the year, so it is entered rather than assumed.
    /// </summary>
    public decimal SuiWageBase { get; set; }

    /// <summary>
    /// Whether the employer receives the full FUTA credit for timely state contributions,
    /// giving an effective 0.6% instead of the 6.0% gross rate. Illinois has not been a
    /// credit-reduction state recently, but this must be verifiable rather than assumed.
    /// </summary>
    public bool ReceivesFullFutaCredit { get; set; } = true;
}
