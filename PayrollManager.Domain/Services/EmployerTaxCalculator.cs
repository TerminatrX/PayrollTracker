using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services.Tax;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Employer-paid payroll taxes for one employee for one pay period.
///
/// These are a COST TO THE EMPLOYER, not withholding. They must never be added to the
/// employee's tax total or netted out of their pay - doing so would understate take-home pay
/// and overstate withheld liability. They are reported separately for exactly that reason.
/// </summary>
public sealed record EmployerTaxResult
{
    /// <summary>Employer's matching Social Security contribution (6.2%, same wage base).</summary>
    public decimal SocialSecurityMatch { get; init; }

    /// <summary>
    /// Employer's matching Medicare contribution (1.45%). Note there is NO employer match on
    /// the 0.9% Additional Medicare Tax - that is employee-only.
    /// </summary>
    public decimal MedicareMatch { get; init; }

    /// <summary>Federal unemployment tax, on wages up to the FUTA wage base.</summary>
    public decimal Futa { get; init; }

    /// <summary>State unemployment tax, on wages up to the state wage base.</summary>
    public decimal Sui { get; init; }

    public decimal Total => SocialSecurityMatch + MedicareMatch + Futa + Sui;

    /// <summary>Conditions the employer should see, e.g. an unconfigured SUI rate.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public static class EmployerTaxCalculator
{
    /// <summary>
    /// Computes employer-side taxes for a pay period.
    /// </summary>
    /// <param name="ficaWages">This period's FICA wages (gross less §125 premiums).</param>
    /// <param name="ytdSocialSecurityWagesPrior">
    /// YTD wages already SUBJECT TO Social Security. This accumulator stops growing once the
    /// wage base is reached, which is exactly what the employer SS match needs.
    /// </param>
    /// <param name="ytdTotalFicaWagesPrior">
    /// YTD FICA wages with no cap applied. Unemployment bases (FUTA $7,000, SUI) must be
    /// measured against this, not against the Social-Security-capped figure - those two
    /// diverge above the SS wage base, and using the capped value there would silently
    /// re-open an exhausted unemployment base.
    /// </param>
    /// <param name="settings">Company settings supplying the employer-specific SUI figures.</param>
    /// <param name="rules">Statutory federal figures for the pay date's year.</param>
    public static EmployerTaxResult Calculate(
        decimal ficaWages,
        decimal ytdSocialSecurityWagesPrior,
        decimal ytdTotalFicaWagesPrior,
        CompanySettings settings,
        FederalTaxRules rules)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(rules);

        var warnings = new List<string>();
        ficaWages = Math.Max(0m, ficaWages);

        // Social Security match: same 6.2% and same wage base as the employee side.
        var ssRemaining = Math.Max(0m, rules.SocialSecurityWageBase - ytdSocialSecurityWagesPrior);
        var ssTaxable = Math.Min(ficaWages, ssRemaining);
        var socialSecurityMatch = Money.Round(ssTaxable * rules.SocialSecurityRate);

        // Medicare match: 1.45% on all wages, no base, and NO match on Additional Medicare.
        var medicareMatch = Money.Round(ficaWages * rules.MedicareRate);

        // FUTA: on wages up to the federal wage base, measured against uncapped YTD wages.
        var futaRemaining = Math.Max(0m, rules.FutaWageBase - ytdTotalFicaWagesPrior);
        var futaTaxable = Math.Min(ficaWages, futaRemaining);
        var futaRate = settings.ReceivesFullFutaCredit ? rules.FutaNetRate : rules.FutaGrossRate;
        var futa = Money.Round(futaTaxable * futaRate);

        if (!settings.ReceivesFullFutaCredit)
        {
            warnings.Add(
                "FUTA is being computed at the full 6.0% gross rate because the full state credit " +
                "is not being claimed. Confirm this against the current IRS credit-reduction list.");
        }

        // SUI: employer-specific rate and wage base from the annual IDES rate notice.
        decimal sui = 0m;

        if (settings.SuiRatePercent <= 0m || settings.SuiWageBase <= 0m)
        {
            warnings.Add(
                "State unemployment (SUI) is not configured, so employer unemployment cost is " +
                "understated. Enter the rate and taxable wage base from your annual IDES rate " +
                "notice in Company Settings.");
        }
        else
        {
            var suiRemaining = Math.Max(0m, settings.SuiWageBase - ytdTotalFicaWagesPrior);
            var suiTaxable = Math.Min(ficaWages, suiRemaining);
            sui = Money.Round(suiTaxable * (settings.SuiRatePercent / 100m));
        }

        return new EmployerTaxResult
        {
            SocialSecurityMatch = socialSecurityMatch,
            MedicareMatch = medicareMatch,
            Futa = futa,
            Sui = sui,
            Warnings = warnings
        };
    }
}
