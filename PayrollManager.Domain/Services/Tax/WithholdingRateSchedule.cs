namespace PayrollManager.Domain.Services.Tax;

/// <summary>
/// One row of a percentage-method withholding table.
/// </summary>
/// <param name="LowerBound">Adjusted annual wage at which this bracket starts (inclusive).</param>
/// <param name="UpperBound">Adjusted annual wage at which it ends (exclusive); null = no upper bound.</param>
/// <param name="BaseAmount">Tentative withholding on all wages below <paramref name="LowerBound"/>.</param>
/// <param name="MarginalRate">Rate applied to the excess over <paramref name="LowerBound"/> (0.22 = 22%).</param>
public sealed record TaxBracket(
    decimal LowerBound,
    decimal? UpperBound,
    decimal BaseAmount,
    decimal MarginalRate);

/// <summary>
/// A percentage-method withholding rate schedule (one filing status, one table variant).
///
/// The constructor enforces a structural invariant that catches transcription errors, which
/// are the dominant failure mode when copying published tax tables:
///
///     base[i] == base[i-1] + (lower[i] - lower[i-1]) * rate[i-1]
///
/// A correct published table always satisfies this, because the base amount of each bracket
/// is by definition the tax accumulated through all lower brackets. Any typo in a threshold,
/// a base amount, or a rate breaks the chain.
///
/// This is not theoretical - while transcribing IRS Pub 15-T (2026) this invariant caught a
/// published table displaying $108,938 for a threshold that is really $108,937.50, and it
/// reconstructed bracket boundaries omitted from a truncated source.
/// </summary>
public sealed class WithholdingRateSchedule
{
    public string Name { get; }
    public IReadOnlyList<TaxBracket> Brackets { get; }

    public WithholdingRateSchedule(string name, IReadOnlyList<TaxBracket> brackets)
    {
        if (brackets is null || brackets.Count == 0)
        {
            throw new ArgumentException($"Schedule '{name}' has no brackets.", nameof(brackets));
        }

        Name = name;
        Brackets = brackets;
        Validate();
    }

    private void Validate()
    {
        if (Brackets[0].LowerBound != 0m)
        {
            throw new InvalidTaxScheduleException(Name, "the first bracket must start at 0.");
        }

        if (Brackets[^1].UpperBound is not null)
        {
            throw new InvalidTaxScheduleException(Name, "the final bracket must be open-ended.");
        }

        for (var i = 0; i < Brackets.Count; i++)
        {
            var bracket = Brackets[i];

            if (bracket.MarginalRate < 0m || bracket.MarginalRate > 1m)
            {
                throw new InvalidTaxScheduleException(
                    Name, $"bracket {i} has rate {bracket.MarginalRate}, which is not a fraction between 0 and 1. " +
                          "Rates must be expressed as 0.22 for 22%, not as 22.");
            }

            if (i == 0)
            {
                continue;
            }

            var previous = Brackets[i - 1];

            if (previous.UpperBound != bracket.LowerBound)
            {
                throw new InvalidTaxScheduleException(
                    Name, $"bracket {i} starts at {bracket.LowerBound:N2} but bracket {i - 1} " +
                          $"ends at {previous.UpperBound:N2} - brackets must be contiguous with no gap or overlap.");
            }

            if (bracket.MarginalRate < previous.MarginalRate)
            {
                throw new InvalidTaxScheduleException(
                    Name, $"bracket {i} has a lower rate ({bracket.MarginalRate:P2}) than bracket " +
                          $"{i - 1} ({previous.MarginalRate:P2}) - a withholding schedule must be progressive.");
            }

            // The chain invariant: the tax accumulated through the previous bracket must equal
            // this bracket's stated base amount.
            var expectedBase = previous.BaseAmount +
                               (bracket.LowerBound - previous.LowerBound) * previous.MarginalRate;

            if (Math.Abs(expectedBase - bracket.BaseAmount) > 0.005m)
            {
                throw new InvalidTaxScheduleException(
                    Name, $"bracket {i} states a base amount of {bracket.BaseAmount:N2} but the brackets " +
                          $"below it accumulate to {expectedBase:N2}. Re-check the published table: a " +
                          "threshold, base amount, or rate has been transcribed incorrectly, or a " +
                          "threshold shown as a whole dollar is really a half-dollar.");
            }
        }
    }

    /// <summary>
    /// Computes tentative annual withholding for an adjusted annual wage amount.
    /// Wages at or below zero produce zero withholding.
    /// </summary>
    public decimal ComputeAnnualWithholding(decimal adjustedAnnualWage)
    {
        if (adjustedAnnualWage <= 0m)
        {
            return 0m;
        }

        foreach (var bracket in Brackets)
        {
            if (bracket.UpperBound is null || adjustedAnnualWage < bracket.UpperBound)
            {
                return bracket.BaseAmount +
                       (adjustedAnnualWage - bracket.LowerBound) * bracket.MarginalRate;
            }
        }

        // Unreachable: the final bracket is open-ended and validated as such.
        throw new InvalidOperationException($"Schedule '{Name}' produced no bracket for {adjustedAnnualWage}.");
    }
}

public sealed class InvalidTaxScheduleException : InvalidOperationException
{
    public InvalidTaxScheduleException(string scheduleName, string problem)
        : base($"Withholding schedule '{scheduleName}' is invalid: {problem}")
    {
    }
}
