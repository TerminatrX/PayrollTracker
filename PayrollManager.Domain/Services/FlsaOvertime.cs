namespace PayrollManager.Domain.Services;

/// <summary>
/// Fair Labor Standards Act overtime splitting.
///
/// The rule that matters: overtime is owed for hours worked over 40 in a single WORKWEEK -
/// a fixed, recurring 168-hour period. It is NOT computed over the pay period. Splitting an
/// 80-hour biweekly period at 40 treats half of an ordinary two-week schedule as overtime.
///
/// Employers may not average hours across two workweeks either: 50 hours one week and 30 the
/// next is 10 hours of overtime, not zero.
/// </summary>
public static class FlsaOvertime
{
    /// <summary>Hours over this many in a single workweek are overtime.</summary>
    public const decimal WeeklyThreshold = 40m;

    /// <summary>Standard FLSA overtime premium multiplier.</summary>
    public const decimal Multiplier = 1.5m;

    /// <summary>
    /// Splits actual per-workweek hours into regular and overtime. This is the correct entry
    /// point: it needs one entry per workweek in the pay period.
    /// </summary>
    public static (decimal Regular, decimal Overtime) SplitByWorkweek(IEnumerable<decimal> hoursPerWorkweek)
    {
        ArgumentNullException.ThrowIfNull(hoursPerWorkweek);

        decimal regular = 0m, overtime = 0m;

        foreach (var hours in hoursPerWorkweek)
        {
            if (hours < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hoursPerWorkweek), hours, "Hours worked in a workweek cannot be negative.");
            }

            regular += Math.Min(hours, WeeklyThreshold);
            overtime += Math.Max(0m, hours - WeeklyThreshold);
        }

        return (regular, overtime);
    }

    /// <summary>
    /// Splits a pay-period hours total by assuming it was spread evenly across the period's
    /// workweeks.
    ///
    /// This is an APPROXIMATION and is only correct when hours really were even. It is
    /// suitable for defaulting a data-entry form - it produces the right answer for a standard
    /// schedule - but an employee whose hours varied across weeks must have actual per-workweek
    /// hours entered and split with <see cref="SplitByWorkweek"/>, or they will be underpaid.
    /// </summary>
    public static (decimal Regular, decimal Overtime) SplitEvenlyAcrossWorkweeks(
        decimal totalPeriodHours, int workweeksInPeriod)
    {
        if (workweeksInPeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workweeksInPeriod), workweeksInPeriod, "A pay period must contain at least one workweek.");
        }

        if (totalPeriodHours < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalPeriodHours), totalPeriodHours, "Hours worked cannot be negative.");
        }

        var perWeek = totalPeriodHours / workweeksInPeriod;
        return SplitByWorkweek(Enumerable.Repeat(perWeek, workweeksInPeriod));
    }

    /// <summary>
    /// Workweeks in one pay period, for defaulting purposes.
    ///
    /// Semi-monthly and monthly periods do not align to workweeks at all - FLSA still requires
    /// tracking by workweek regardless of how often people are paid - so those values are only
    /// usable as a data-entry default, never as a compliance answer.
    /// </summary>
    public static int WorkweeksInPeriod(int payPeriodsPerYear) => payPeriodsPerYear switch
    {
        52 => 1,   // weekly
        26 => 2,   // biweekly
        24 => 2,   // semi-monthly (approximate: ~2.17 workweeks)
        12 => 4,   // monthly (approximate: ~4.33 workweeks)
        _ => Math.Max(1, (int)Math.Round(52m / Math.Max(1, payPeriodsPerYear)))
    };
}
