using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services.Tax;

/// <summary>
/// IRS Publication 15-T Percentage Method tables for Automated Payroll Systems (Worksheet 1A).
///
/// Source: https://www.irs.gov/publications/p15t - transcribed and verified 2026-07-21.
/// Every schedule is checked by <see cref="WithholdingRateSchedule"/>'s chain invariant at
/// construction, so a transcription error fails fast at startup rather than silently
/// mis-withholding.
///
/// NOTE ON PUBLISHED ROUNDING: several thresholds are printed as whole dollars but are really
/// half-dollars (e.g. Single/Step-2-checked shows $108,938 for $108,937.50). The exact values
/// are used here - the chain invariant is what surfaced the discrepancy.
///
/// TO ADD A YEAR: transcribe from the published tables, never from memory. If the invariant
/// rejects the table, the transcription is wrong - do not relax the check.
/// </summary>
public static class FederalWithholdingTables
{
    /// <summary>
    /// Worksheet 1A line 1g: amount subtracted when the Step 2 checkbox is NOT checked.
    /// </summary>
    public static decimal GetStandardDeductionAdjustment(int year, FilingStatus filingStatus)
    {
        if (year is not (2024 or 2025 or 2026))
        {
            throw new TaxRulesNotAvailableException(year, new[] { 2024, 2025, 2026 });
        }

        return filingStatus == FilingStatus.MarriedFilingJointly ? 12_900m : 8_600m;
    }

    /// <summary>
    /// Returns the percentage-method schedule for a year, filing status, and whether the
    /// employee checked the Step 2 (multiple jobs) box on Form W-4.
    /// </summary>
    public static WithholdingRateSchedule GetSchedule(int year, FilingStatus filingStatus, bool step2Checked)
    {
        if (year != 2026)
        {
            // Deliberately narrow: only years with transcribed-and-verified tables are served.
            throw new TaxRulesNotAvailableException(year, new[] { 2026 });
        }

        return (filingStatus, step2Checked) switch
        {
            (FilingStatus.MarriedFilingJointly, false) => Standard2026MarriedFilingJointly,
            (FilingStatus.HeadOfHousehold, false) => Standard2026HeadOfHousehold,
            (_, false) => Standard2026Single,

            (FilingStatus.MarriedFilingJointly, true) => Higher2026MarriedFilingJointly,
            (FilingStatus.HeadOfHousehold, true) => Higher2026HeadOfHousehold,
            (_, true) => Higher2026Single
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // STANDARD schedules - Step 2 checkbox NOT checked
    // ═══════════════════════════════════════════════════════════════

    private static readonly WithholdingRateSchedule Standard2026MarriedFilingJointly = new(
        "2026 Standard - Married Filing Jointly",
        new[]
        {
            new TaxBracket(0m,        19_300m,  0m,           0.00m),
            new TaxBracket(19_300m,   44_100m,  0m,           0.10m),
            new TaxBracket(44_100m,  120_100m,  2_480m,       0.12m),
            new TaxBracket(120_100m, 230_700m,  11_600m,      0.22m),
            new TaxBracket(230_700m, 422_850m,  35_932m,      0.24m),
            new TaxBracket(422_850m, 531_750m,  82_048m,      0.32m),
            new TaxBracket(531_750m, 788_000m,  116_896m,     0.35m),
            new TaxBracket(788_000m, null,      206_583.50m,  0.37m),
        });

    private static readonly WithholdingRateSchedule Standard2026Single = new(
        "2026 Standard - Single or Married Filing Separately",
        new[]
        {
            new TaxBracket(0m,         7_500m,  0m,           0.00m),
            new TaxBracket(7_500m,    19_900m,  0m,           0.10m),
            new TaxBracket(19_900m,   57_900m,  1_240m,       0.12m),
            new TaxBracket(57_900m,  113_200m,  5_800m,       0.22m),
            new TaxBracket(113_200m, 209_275m,  17_966m,      0.24m),
            new TaxBracket(209_275m, 263_725m,  41_024m,      0.32m),
            new TaxBracket(263_725m, 648_100m,  58_448m,      0.35m),
            new TaxBracket(648_100m, null,      192_979.25m,  0.37m),
        });

    private static readonly WithholdingRateSchedule Standard2026HeadOfHousehold = new(
        "2026 Standard - Head of Household",
        new[]
        {
            new TaxBracket(0m,        15_550m,  0m,           0.00m),
            new TaxBracket(15_550m,   33_250m,  0m,           0.10m),
            new TaxBracket(33_250m,   83_000m,  1_770m,       0.12m),
            new TaxBracket(83_000m,  121_250m,  7_740m,       0.22m),
            new TaxBracket(121_250m, 217_300m,  16_155m,      0.24m),
            new TaxBracket(217_300m, 271_750m,  39_207m,      0.32m),
            new TaxBracket(271_750m, 656_150m,  56_631m,      0.35m),
            new TaxBracket(656_150m, null,      191_171m,     0.37m),
        });

    // ═══════════════════════════════════════════════════════════════
    // HIGHER schedules - Step 2 checkbox IS checked
    // ═══════════════════════════════════════════════════════════════

    private static readonly WithholdingRateSchedule Higher2026MarriedFilingJointly = new(
        "2026 Step-2-Checked - Married Filing Jointly",
        new[]
        {
            new TaxBracket(0m,        16_100m,  0m,           0.00m),
            new TaxBracket(16_100m,   28_500m,  0m,           0.10m),
            new TaxBracket(28_500m,   66_500m,  1_240m,       0.12m),
            new TaxBracket(66_500m,  121_800m,  5_800m,       0.22m),
            new TaxBracket(121_800m, 217_875m,  17_966m,      0.24m),
            new TaxBracket(217_875m, 272_325m,  41_024m,      0.32m),
            new TaxBracket(272_325m, 400_450m,  58_448m,      0.35m),
            new TaxBracket(400_450m, null,      103_291.75m,  0.37m),
        });

    private static readonly WithholdingRateSchedule Higher2026Single = new(
        "2026 Step-2-Checked - Single or Married Filing Separately",
        new[]
        {
            new TaxBracket(0m,           8_050m,     0m,          0.00m),
            new TaxBracket(8_050m,      14_250m,     0m,          0.10m),
            new TaxBracket(14_250m,     33_250m,     620m,        0.12m),
            new TaxBracket(33_250m,     60_900m,     2_900m,      0.22m),
            // Published as $108,938 / $136,163; the true thresholds are half-dollars.
            new TaxBracket(60_900m,    108_937.50m,  8_983m,      0.24m),
            new TaxBracket(108_937.50m, 136_162.50m, 20_512m,     0.32m),
            new TaxBracket(136_162.50m, 328_350m,    29_224m,     0.35m),
            new TaxBracket(328_350m,    null,        96_489.625m, 0.37m),
        });

    private static readonly WithholdingRateSchedule Higher2026HeadOfHousehold = new(
        "2026 Step-2-Checked - Head of Household",
        new[]
        {
            new TaxBracket(0m,        12_075m,  0m,           0.00m),
            new TaxBracket(12_075m,   20_925m,  0m,           0.10m),
            new TaxBracket(20_925m,   45_800m,  885m,         0.12m),
            new TaxBracket(45_800m,   64_925m,  3_870m,       0.22m),
            new TaxBracket(64_925m,  112_950m,  8_077.50m,    0.24m),
            new TaxBracket(112_950m, 140_175m,  19_603.50m,   0.32m),
            new TaxBracket(140_175m, 332_375m,  28_315.50m,   0.35m),
            new TaxBracket(332_375m, null,      95_585.50m,   0.37m),
        });
}
