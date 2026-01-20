using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Pay frequency types supported by the system.
/// </summary>
public enum PayFrequency
{
    BiWeekly = 26,      // 26 periods per year (every 2 weeks)
    Monthly = 12,       // 12 periods per year (once per month)
    SemiMonthly = 24    // 24 periods per year (twice per month, typically 1st and 15th)
}

/// <summary>
/// Result of pay period calculation containing the next period dates.
/// </summary>
public class PayPeriodResult
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime PayDate { get; set; }
}

/// <summary>
/// Service for calculating pay period dates based on pay frequency and last pay run.
/// </summary>
public class PayPeriodCalculator
{
    /// <summary>
    /// Calculates the next pay period based on the last pay run and company settings.
    /// </summary>
    /// <param name="lastPayRun">The last pay run, or null if this is the first pay run.</param>
    /// <param name="payFrequency">The pay frequency from company settings.</param>
    /// <returns>The next pay period dates (PeriodStart, PeriodEnd, PayDate).</returns>
    public static PayPeriodResult CalculateNextPeriod(PayRun? lastPayRun, PayFrequency payFrequency)
    {
        if (lastPayRun == null)
        {
            // First pay run - calculate from today
            return CalculateNextPeriodFromDate(DateTime.Today, payFrequency);
        }

        // Calculate next period based on last pay run's period end
        return CalculateNextPeriodFromDate(lastPayRun.PeriodEnd, payFrequency);
    }

    /// <summary>
    /// Calculates the next pay period from a reference date.
    /// </summary>
    /// <param name="referenceDate">The date to calculate from (typically last period end or today).</param>
    /// <param name="payFrequency">The pay frequency.</param>
    /// <returns>The next pay period dates.</returns>
    public static PayPeriodResult CalculateNextPeriodFromDate(DateTime referenceDate, PayFrequency payFrequency)
    {
        return payFrequency switch
        {
            PayFrequency.BiWeekly => CalculateBiWeekly(referenceDate),
            PayFrequency.Monthly => CalculateMonthly(referenceDate),
            PayFrequency.SemiMonthly => CalculateSemiMonthly(referenceDate),
            _ => CalculateBiWeekly(referenceDate) // Default to biweekly
        };
    }

    /// <summary>
    /// Converts PayPeriodsPerYear to PayFrequency enum.
    /// </summary>
    public static PayFrequency GetPayFrequency(int payPeriodsPerYear)
    {
        return payPeriodsPerYear switch
        {
            26 => PayFrequency.BiWeekly,
            12 => PayFrequency.Monthly,
            24 => PayFrequency.SemiMonthly,
            _ => PayFrequency.BiWeekly // Default to biweekly
        };
    }

    /// <summary>
    /// Calculates biweekly pay period (every 2 weeks, 26 periods per year).
    /// Period typically runs Monday to Sunday, pay date is the day after period ends.
    /// </summary>
    private static PayPeriodResult CalculateBiWeekly(DateTime referenceDate)
    {
        // Start from the day after the reference date
        var startDate = referenceDate.AddDays(1);

        // Find the next Monday
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)startDate.DayOfWeek + 7) % 7;
        
        // If it's already Monday, we want the next Monday (7 days away)
        if (daysUntilMonday == 0)
        {
            daysUntilMonday = 7;
        }

        var periodStart = startDate.AddDays(daysUntilMonday).Date;
        var periodEnd = periodStart.AddDays(13).Date; // 14-day period (0-13 = 14 days inclusive)
        var payDate = periodEnd.AddDays(1).Date; // Pay date is the day after period ends

        return new PayPeriodResult
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = payDate
        };
    }

    /// <summary>
    /// Calculates monthly pay period (once per month, 12 periods per year).
    /// Period runs from the 1st to the last day of the month, pay date is typically the 1st of next month.
    /// </summary>
    private static PayPeriodResult CalculateMonthly(DateTime referenceDate)
    {
        // Start from the day after the reference date
        var startDate = referenceDate.AddDays(1);

        // If we're past the 1st of the current month, move to next month
        DateTime periodStart;
        if (startDate.Day == 1)
        {
            periodStart = startDate;
        }
        else
        {
            // Move to the 1st of next month
            periodStart = new DateTime(startDate.Year, startDate.Month, 1).AddMonths(1);
        }

        // Period end is the last day of the month
        var periodEnd = new DateTime(periodStart.Year, periodStart.Month, 
            DateTime.DaysInMonth(periodStart.Year, periodStart.Month));

        // Pay date is typically the 1st of the following month
        var payDate = periodEnd.AddDays(1);

        return new PayPeriodResult
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = payDate
        };
    }

    /// <summary>
    /// Calculates semi-monthly pay period (twice per month, 24 periods per year).
    /// Periods typically run 1st-15th and 16th-last day of month.
    /// Pay dates are typically the 15th and last day of the month.
    /// </summary>
    private static PayPeriodResult CalculateSemiMonthly(DateTime referenceDate)
    {
        // Start from the day after the reference date
        var startDate = referenceDate.AddDays(1);

        DateTime periodStart;
        DateTime periodEnd;
        DateTime payDate;

        if (startDate.Day <= 15)
        {
            // First half of month: 1st to 15th
            periodStart = new DateTime(startDate.Year, startDate.Month, 1);
            periodEnd = new DateTime(startDate.Year, startDate.Month, 15);
            payDate = periodEnd.AddDays(1); // Pay date is 16th
        }
        else
        {
            // Second half of month: 16th to last day
            periodStart = new DateTime(startDate.Year, startDate.Month, 16);
            var lastDay = DateTime.DaysInMonth(startDate.Year, startDate.Month);
            periodEnd = new DateTime(startDate.Year, startDate.Month, lastDay);
            
            // Pay date is 1st of next month
            payDate = periodEnd.AddDays(1);
        }

        // If we've already passed the period, move to next period
        if (startDate > periodEnd)
        {
            if (startDate.Day <= 15)
            {
                // We're past the 15th, move to second half
                periodStart = new DateTime(startDate.Year, startDate.Month, 16);
                var lastDay = DateTime.DaysInMonth(startDate.Year, startDate.Month);
                periodEnd = new DateTime(startDate.Year, startDate.Month, lastDay);
                payDate = periodEnd.AddDays(1);
            }
            else
            {
                // We're past month end, move to first half of next month
                var nextMonth = startDate.AddMonths(1);
                periodStart = new DateTime(nextMonth.Year, nextMonth.Month, 1);
                periodEnd = new DateTime(nextMonth.Year, nextMonth.Month, 15);
                payDate = periodEnd.AddDays(1);
            }
        }

        return new PayPeriodResult
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = payDate
        };
    }
}
