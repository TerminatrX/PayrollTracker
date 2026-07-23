using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using PayrollManager.Domain.Services.Tax;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Employer-paid payroll taxes. These are an employer COST and must never be confused with
/// employee withholding.
/// </summary>
public class EmployerTaxCalculatorTests
{
    private static readonly FederalTaxRules Rules2026 =
        new StaticFederalTaxRuleProvider().GetFederalRules(2026);

    private static CompanySettings Settings(decimal suiRate = 3.0m, decimal suiWageBase = 13_590m) => new()
    {
        SuiRatePercent = suiRate,
        SuiWageBase = suiWageBase,
        ReceivesFullFutaCredit = true
    };

    [Fact]
    public void EmployerMatches_MirrorTheEmployeeFicaRates()
    {
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 0m, ytdTotalFicaWagesPrior: 0m, Settings(), Rules2026);

        Assert.Equal(186m, result.SocialSecurityMatch);   // 3000 * 6.2%
        Assert.Equal(43.50m, result.MedicareMatch);       // 3000 * 1.45%
    }

    [Fact]
    public void Futa_StopsAtTheSevenThousandWageBase()
    {
        // 6,000 already paid leaves 1,000 of the 7,000 FUTA base.
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 6_000m, ytdTotalFicaWagesPrior: 6_000m, Settings(), Rules2026);

        Assert.Equal(6m, result.Futa);   // 1000 * 0.6%
    }

    [Fact]
    public void Futa_IsZero_OnceTheWageBaseIsExhausted()
    {
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 50_000m, ytdTotalFicaWagesPrior: 50_000m, Settings(), Rules2026);

        Assert.Equal(0m, result.Futa);
    }

    [Fact]
    public void Futa_UsesTheGrossRate_WhenTheStateCreditIsNotClaimed()
    {
        var settings = Settings();
        settings.ReceivesFullFutaCredit = false;

        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 0m, ytdTotalFicaWagesPrior: 0m, settings, Rules2026);

        Assert.Equal(180m, result.Futa);   // 3000 * 6.0%
        Assert.Contains(result.Warnings, w => w.Contains("6.0%"));
    }

    [Fact]
    public void SocialSecurityMatch_StopsAtTheWageBase()
    {
        // 182,900 prior leaves 1,600 under the 2026 base of 184,500.
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 182_900m, ytdTotalFicaWagesPrior: 182_900m, Settings(), Rules2026);

        Assert.Equal(99.20m, result.SocialSecurityMatch);   // 1600 * 6.2%
        Assert.Equal(43.50m, result.MedicareMatch);         // Medicare has no base
    }

    [Fact]
    public void Sui_UsesTheConfiguredRateAndBase()
    {
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 0m, ytdTotalFicaWagesPrior: 0m,
            Settings(suiRate: 3.0m, suiWageBase: 13_590m), Rules2026);

        Assert.Equal(90m, result.Sui);   // 3000 * 3.0%
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Sui_StopsAtTheStateWageBase()
    {
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 13_000m, ytdTotalFicaWagesPrior: 13_000m,
            Settings(suiRate: 3.0m, suiWageBase: 13_590m), Rules2026);

        Assert.Equal(17.70m, result.Sui);   // 590 remaining * 3.0%
    }

    [Fact]
    public void UnconfiguredSui_WarnsRatherThanGuessing()
    {
        // There is no defensible default SUI rate - IDES assigns it per employer, per year.
        // Guessing would silently misstate employer liability.
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 0m, ytdTotalFicaWagesPrior: 0m,
            Settings(suiRate: 0m, suiWageBase: 0m), Rules2026);

        Assert.Equal(0m, result.Sui);
        Assert.Contains(result.Warnings, w => w.Contains("IDES rate notice"));
    }

    [Fact]
    public void TotalEmployerCost_SumsTheFourComponents()
    {
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 3_000m, ytdSocialSecurityWagesPrior: 0m, ytdTotalFicaWagesPrior: 0m,
            Settings(suiRate: 3.0m, suiWageBase: 13_590m), Rules2026);

        // 186.00 SS + 43.50 Medicare + 18.00 FUTA (3000 * 0.6%) + 90.00 SUI
        Assert.Equal(18m, result.Futa);
        Assert.Equal(337.50m, result.Total);
    }

    [Fact]
    public void UnemploymentBases_UseUncappedWages_NotSocialSecurityCappedWages()
    {
        // Above the Social Security wage base, the SS accumulator stops growing while actual
        // wages keep rising. If the unemployment bases were measured against the capped
        // figure, an exhausted FUTA/SUI base would silently re-open and the employer would be
        // charged unemployment tax all over again on a high earner.
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: 5_000m,
            ytdSocialSecurityWagesPrior: 184_500m,   // capped: stopped growing
            ytdTotalFicaWagesPrior: 260_000m,        // actual wages paid this year
            Settings(suiRate: 3.0m, suiWageBase: 13_590m),
            Rules2026);

        Assert.Equal(0m, result.SocialSecurityMatch);   // wage base fully used
        Assert.Equal(72.50m, result.MedicareMatch);     // 5000 * 1.45%, no base
        Assert.Equal(0m, result.Futa);                  // exhausted long ago, stays exhausted
        Assert.Equal(0m, result.Sui);                   // likewise
    }

    [Fact]
    public void NegativeWages_ProduceNoEmployerTax()
    {
        var result = EmployerTaxCalculator.Calculate(
            ficaWages: -500m, ytdSocialSecurityWagesPrior: 0m, ytdTotalFicaWagesPrior: 0m, Settings(), Rules2026);

        Assert.Equal(0m, result.Total);
    }
}
