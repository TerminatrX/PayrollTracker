namespace PayrollManager.Domain.Services;

/// <summary>
/// The single rounding choke point for all monetary amounts.
///
/// Policy: round half away from zero ("half-up" for positive amounts) to 2 decimal places.
/// This is the conventional choice for US payroll and matches IRS worksheet instructions,
/// which round each computed amount to the nearest cent. Banker's rounding
/// (<see cref="MidpointRounding.ToEven"/>, the .NET default) is deliberately NOT used - it
/// would make withheld amounts disagree with hand-checked worksheets at exact half-cents.
///
/// Rounding discipline: round each individual line as it is produced, then derive every
/// total by summing already-rounded lines. Never round a total independently of its parts -
/// that is what allows a stub's totals to disagree with the lines printed beneath them.
/// </summary>
public static class Money
{
    public const int Decimals = 2;

    /// <summary>
    /// Rounds a monetary amount to the nearest cent, half away from zero.
    /// </summary>
    public static decimal Round(decimal amount) =>
        Math.Round(amount, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// True if the amount is already expressed in whole cents.
    /// Intended for test assertions and posting-time validation.
    /// </summary>
    public static bool IsWholeCents(decimal amount) => Round(amount) == amount;
}
