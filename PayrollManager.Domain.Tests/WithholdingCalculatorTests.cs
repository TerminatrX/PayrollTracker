using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services.Tax;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Tests for the federal (Pub 15-T Worksheet 1A) and Illinois (IL-700-T) withholding engines.
///
/// Federal expectations are worked through the published worksheet by hand, step by step, so
/// a failure points at a specific worksheet line rather than at "the number changed".
/// </summary>
public class WithholdingCalculatorTests
{
    private const int Biweekly = 26;
    private const int Year = 2026;

    // ═══════════════════════════════════════════════════════════════
    // TABLE INTEGRITY
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FilingStatus.Single, false)]
    [InlineData(FilingStatus.MarriedFilingJointly, false)]
    [InlineData(FilingStatus.MarriedFilingSeparately, false)]
    [InlineData(FilingStatus.HeadOfHousehold, false)]
    [InlineData(FilingStatus.Single, true)]
    [InlineData(FilingStatus.MarriedFilingJointly, true)]
    [InlineData(FilingStatus.MarriedFilingSeparately, true)]
    [InlineData(FilingStatus.HeadOfHousehold, true)]
    public void EveryPublishedSchedule_SatisfiesTheChainInvariant(FilingStatus status, bool step2)
    {
        // Construction runs the validator; reaching this line means all brackets are
        // contiguous, progressive, and have base amounts consistent with the brackets below.
        var schedule = FederalWithholdingTables.GetSchedule(Year, status, step2);

        Assert.Equal(8, schedule.Brackets.Count);
        Assert.Equal(0m, schedule.Brackets[0].LowerBound);
        Assert.Null(schedule.Brackets[^1].UpperBound);
        Assert.Equal(0.37m, schedule.Brackets[^1].MarginalRate);
    }

    [Fact]
    public void ChainInvariant_RejectsATranscriptionError()
    {
        // This is the check that caught a real error while transcribing Pub 15-T. Prove it bites:
        // the second bracket's base should be (20,000 - 10,000) * 10% = 1,000, not 999.
        var ex = Assert.Throws<InvalidTaxScheduleException>(() => new WithholdingRateSchedule(
            "bad table",
            new[]
            {
                new TaxBracket(0m, 10_000m, 0m, 0.00m),
                new TaxBracket(10_000m, 20_000m, 0m, 0.10m),
                new TaxBracket(20_000m, null, 999m, 0.12m),
            }));

        Assert.Contains("999", ex.Message);
        Assert.Contains("1,000", ex.Message);
    }

    [Fact]
    public void ChainInvariant_RejectsAGapBetweenBrackets()
    {
        Assert.Throws<InvalidTaxScheduleException>(() => new WithholdingRateSchedule(
            "gappy table",
            new[]
            {
                new TaxBracket(0m, 10_000m, 0m, 0.00m),
                new TaxBracket(15_000m, null, 0m, 0.10m),
            }));
    }

    [Fact]
    public void ChainInvariant_RejectsPercentagesWrittenAsWholeNumbers()
    {
        // A 22 that should have been 0.22 is an easy and catastrophic slip.
        Assert.Throws<InvalidTaxScheduleException>(() => new WithholdingRateSchedule(
            "percent confusion",
            new[]
            {
                new TaxBracket(0m, 10_000m, 0m, 0m),
                new TaxBracket(10_000m, null, 0m, 22m),
            }));
    }

    // ═══════════════════════════════════════════════════════════════
    // FEDERAL - WORKSHEET 1A
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Federal_SingleFiler_MatchesHandWorkedWorksheet()
    {
        // Worksheet 1A, single filer, $3,000 biweekly, plain W-4 (no steps 2/3/4):
        //   1c: 3,000 * 26                        = 78,000
        //   1g: standard adjustment, Single       =  8,600
        //   1i: adjusted annual wage              = 69,400
        //   2:  69,400 falls in 57,900-113,200 @ 22%, base 5,800
        //       5,800 + (69,400 - 57,900) * 0.22  =  8,330
        //   2i: 8,330 / 26                        =    320.3846... -> 320.38
        var withheld = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly, new FederalW4Info { FilingStatus = FilingStatus.Single }, Year);

        Assert.Equal(320.38m, withheld);
    }

    [Fact]
    public void Federal_MarriedFilingJointly_MatchesHandWorkedWorksheet()
    {
        //   1c: 3,000 * 26                        = 78,000
        //   1g: standard adjustment, MFJ          = 12,900
        //   1i:                                   = 65,100
        //   2:  65,100 in 44,100-120,100 @ 12%, base 2,480
        //       2,480 + (65,100 - 44,100) * 0.12  =  5,000
        //   2i: 5,000 / 26                        =    192.3076... -> 192.31
        var withheld = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly,
            new FederalW4Info { FilingStatus = FilingStatus.MarriedFilingJointly }, Year);

        Assert.Equal(192.31m, withheld);
    }

    [Fact]
    public void Federal_MarriedFilingSeparately_UsesTheSingleSchedule()
    {
        // Pub 15-T titles the table "Single or Married Filing Separately".
        var single = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly, new FederalW4Info { FilingStatus = FilingStatus.Single }, Year);
        var separately = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly, new FederalW4Info { FilingStatus = FilingStatus.MarriedFilingSeparately }, Year);

        Assert.Equal(single, separately);
    }

    [Fact]
    public void Federal_DependentCredits_ReduceWithholdingPerPeriod()
    {
        // Step 3 of $4,000 annually spreads to 4,000 / 26 = 153.846... per period.
        //   320.3846 - 153.8461 = 166.5384 -> 166.54
        var withheld = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly,
            new FederalW4Info { FilingStatus = FilingStatus.Single, DependentsAndOtherCredits = 4_000m },
            Year);

        Assert.Equal(166.54m, withheld);
    }

    [Fact]
    public void Federal_CreditsCannotProduceNegativeWithholding()
    {
        // An employer never refunds through withholding, so Step 3 floors at zero.
        var withheld = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly,
            new FederalW4Info { FilingStatus = FilingStatus.Single, DependentsAndOtherCredits = 500_000m },
            Year);

        Assert.Equal(0m, withheld);
    }

    [Fact]
    public void Federal_ExtraWithholding_IsAddedEvenWhenTheTableYieldsZero()
    {
        // Step 4(c) is applied after the zero floor - an employee who asks for extra gets it.
        var withheld = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly,
            new FederalW4Info
            {
                FilingStatus = FilingStatus.Single,
                DependentsAndOtherCredits = 500_000m,
                ExtraWithholdingPerPeriod = 75m
            },
            Year);

        Assert.Equal(75m, withheld);
    }

    [Fact]
    public void Federal_LowWageEarner_WithholdsNothing()
    {
        // 500 * 26 = 13,000, less the 8,600 adjustment = 4,400, which is inside the
        // Single 0% bracket (0 - 7,500).
        var withheld = FederalWithholdingCalculator.Calculate(
            500m, Biweekly, new FederalW4Info { FilingStatus = FilingStatus.Single }, Year);

        Assert.Equal(0m, withheld);
    }

    [Fact]
    public void Federal_Step2Checkbox_WithholdsMoreThanTheStandardSchedule()
    {
        var standard = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly,
            new FederalW4Info { FilingStatus = FilingStatus.MarriedFilingJointly }, Year);

        var higher = FederalWithholdingCalculator.Calculate(
            3_000m, Biweekly,
            new FederalW4Info
            {
                FilingStatus = FilingStatus.MarriedFilingJointly,
                MultipleJobsChecked = true
            },
            Year);

        // Step 2 exists precisely because a single-job table under-withholds a two-earner
        // household. It must never withhold less.
        Assert.True(higher > standard, $"step-2-checked {higher} should exceed standard {standard}");

        //   1c: 78,000, 1g: 0 (built into the table), 1i: 78,000
        //   2:  78,000 in 66,500-121,800 @ 22%, base 5,800
        //       5,800 + (78,000 - 66,500) * 0.22 = 8,330
        //   2i: 8,330 / 26 = 320.3846... -> 320.38
        Assert.Equal(320.38m, higher);
    }

    [Fact]
    public void Federal_TopBracket_IsReachableAndCorrect()
    {
        // Guards the 37% bracket that a truncated source would have silently omitted.
        //   1c: 40,000 * 26 = 1,040,000; 1g: 8,600; 1i: 1,031,400
        //   2:  above 648,100 @ 37%, base 192,979.25
        //       192,979.25 + (1,031,400 - 648,100) * 0.37 = 192,979.25 + 141,821 = 334,800.25
        //   2i: 334,800.25 / 26 = 12,876.9327... -> 12,876.93
        var withheld = FederalWithholdingCalculator.Calculate(
            40_000m, Biweekly, new FederalW4Info { FilingStatus = FilingStatus.Single }, Year);

        Assert.Equal(12_876.93m, withheld);
    }

    [Fact]
    public void Federal_UnverifiedYear_Throws()
    {
        Assert.Throws<TaxRulesNotAvailableException>(() =>
            FederalWithholdingCalculator.Calculate(
                3_000m, Biweekly, new FederalW4Info { FilingStatus = FilingStatus.Single }, 2031));
    }

    // ═══════════════════════════════════════════════════════════════
    // ILLINOIS - IL-700-T
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Illinois_NoAllowances_IsFlatRateOnFullWages()
    {
        // 3,000 * 4.95% = 148.50
        var withheld = IllinoisWithholdingCalculator.Calculate(
            3_000m, Biweekly, new IllinoisW4Info(), Year);

        Assert.Equal(148.50m, withheld);
    }

    [Fact]
    public void Illinois_BasicAllowances_ReduceWagesBeforeTheRate()
    {
        // 2 basic allowances: 2 * 2,925 = 5,850 annually; 5,850 / 26 = 225.00 per period.
        // (3,000 - 225) * 4.95% = 2,775 * 0.0495 = 137.3625 -> 137.36
        var withheld = IllinoisWithholdingCalculator.Calculate(
            3_000m, Biweekly, new IllinoisW4Info(BasicAllowances: 2), Year);

        Assert.Equal(137.36m, withheld);
    }

    [Fact]
    public void Illinois_AdditionalAllowances_UseTheThousandDollarAmount()
    {
        // 1 basic (2,925) + 1 additional (1,000) = 3,925 annually; / 26 = 150.9615...
        // (3,000 - 150.9615) * 0.0495 = 2,849.0384 * 0.0495 = 141.0274 -> 141.03
        var withheld = IllinoisWithholdingCalculator.Calculate(
            3_000m, Biweekly, new IllinoisW4Info(BasicAllowances: 1, AdditionalAllowances: 1), Year);

        Assert.Equal(141.03m, withheld);
    }

    [Fact]
    public void Illinois_AllowancesExceedingWages_WithholdNothing()
    {
        var withheld = IllinoisWithholdingCalculator.Calculate(
            100m, Biweekly, new IllinoisW4Info(BasicAllowances: 20), Year);

        Assert.Equal(0m, withheld);
    }

    [Fact]
    public void Illinois_PublishedFiguresAreCorrect()
    {
        // Verified 2026-07-21 against IDOR Booklet IL-700-T.
        var rules = IllinoisWithholdingCalculator.GetRules(2026);

        Assert.Equal(0.0495m, rules.Rate);
        Assert.Equal(2_925m, rules.BasicAllowanceAmount);
        Assert.Equal(1_000m, rules.AdditionalAllowanceAmount);
    }

    [Fact]
    public void Illinois_UnverifiedYear_Throws()
    {
        Assert.Throws<TaxRulesNotAvailableException>(
            () => IllinoisWithholdingCalculator.GetRules(2031));
    }
}
