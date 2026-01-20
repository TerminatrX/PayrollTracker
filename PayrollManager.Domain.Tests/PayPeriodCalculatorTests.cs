using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Unit tests for PayPeriodCalculator service.
/// </summary>
public class PayPeriodCalculatorTests
{
    [Fact]
    public void CalculateNextPeriod_BiWeekly_FirstPayRun_StartsFromToday()
    {
        // Arrange
        var today = new DateTime(2024, 1, 15, 0, 0, 0); // Monday, January 15, 2024
        // Next period should start the next Monday (Jan 22) since we're calculating from today
        var expectedStart = new DateTime(2024, 1, 22); // Next Monday
        var expectedEnd = new DateTime(2024, 2, 4); // 14 days later (22-4 = 14 days)
        var expectedPayDate = new DateTime(2024, 2, 5); // Day after period end

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriodFromDate(today, PayFrequency.BiWeekly);

        // Assert
        Assert.Equal(expectedStart.Date, result.PeriodStart.Date);
        Assert.Equal(expectedEnd.Date, result.PeriodEnd.Date);
        Assert.Equal(expectedPayDate.Date, result.PayDate.Date);
        Assert.Equal(14, (result.PeriodEnd - result.PeriodStart).Days + 1); // Inclusive: 14 days
        Assert.Equal(DayOfWeek.Monday, result.PeriodStart.DayOfWeek);
    }

    [Fact]
    public void CalculateNextPeriod_BiWeekly_FromLastPayRun()
    {
        // Arrange
        var lastPayRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 14),
            PayDate = new DateTime(2024, 1, 15)
        };

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, PayFrequency.BiWeekly);

        // Assert
        // Next period should start the day after last period end
        Assert.True(result.PeriodStart > lastPayRun.PeriodEnd);
        Assert.Equal(14, (result.PeriodEnd - result.PeriodStart).Days + 1);
        Assert.True(result.PayDate > result.PeriodEnd);
    }

    [Fact]
    public void CalculateNextPeriod_BiWeekly_ConsecutivePeriods()
    {
        // Arrange
        var firstPeriod = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1), // Monday
            PeriodEnd = new DateTime(2024, 1, 14), // Sunday
            PayDate = new DateTime(2024, 1, 15)
        };

        // Act
        var secondPeriod = PayPeriodCalculator.CalculateNextPeriod(firstPeriod, PayFrequency.BiWeekly);
        var thirdPeriod = PayPeriodCalculator.CalculateNextPeriod(
            new PayRun
            {
                PeriodStart = secondPeriod.PeriodStart,
                PeriodEnd = secondPeriod.PeriodEnd,
                PayDate = secondPeriod.PayDate
            },
            PayFrequency.BiWeekly);

        // Assert
        // Second period should start the day after first period ends
        Assert.Equal(firstPeriod.PeriodEnd.AddDays(1), secondPeriod.PeriodStart);
        
        // Third period should start the day after second period ends
        Assert.Equal(secondPeriod.PeriodEnd.AddDays(1), thirdPeriod.PeriodStart);
        
        // All periods should be 14 days
        Assert.Equal(14, (secondPeriod.PeriodEnd - secondPeriod.PeriodStart).Days + 1);
        Assert.Equal(14, (thirdPeriod.PeriodEnd - thirdPeriod.PeriodStart).Days + 1);
    }

    [Fact]
    public void CalculateNextPeriod_Monthly_FirstPayRun()
    {
        // Arrange
        var today = new DateTime(2024, 1, 10); // January 10, 2024

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriodFromDate(today, PayFrequency.Monthly);

        // Assert
        // Should start on February 1st (next month)
        Assert.Equal(new DateTime(2024, 2, 1), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 2, 29), result.PeriodEnd); // 2024 is a leap year
        Assert.Equal(new DateTime(2024, 3, 1), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_Monthly_FromLastPayRun()
    {
        // Arrange
        var lastPayRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 31),
            PayDate = new DateTime(2024, 2, 1)
        };

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, PayFrequency.Monthly);

        // Assert
        Assert.Equal(new DateTime(2024, 2, 1), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 2, 29), result.PeriodEnd); // 2024 is a leap year
        Assert.Equal(new DateTime(2024, 3, 1), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_Monthly_ConsecutiveMonths()
    {
        // Arrange
        var january = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 31),
            PayDate = new DateTime(2024, 2, 1)
        };

        // Act
        var february = PayPeriodCalculator.CalculateNextPeriod(january, PayFrequency.Monthly);
        var march = PayPeriodCalculator.CalculateNextPeriod(
            new PayRun
            {
                PeriodStart = february.PeriodStart,
                PeriodEnd = february.PeriodEnd,
                PayDate = february.PayDate
            },
            PayFrequency.Monthly);

        // Assert
        Assert.Equal(new DateTime(2024, 2, 1), february.PeriodStart);
        Assert.Equal(new DateTime(2024, 2, 29), february.PeriodEnd);
        
        Assert.Equal(new DateTime(2024, 3, 1), march.PeriodStart);
        Assert.Equal(new DateTime(2024, 3, 31), march.PeriodEnd);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_FirstHalf_FirstPayRun()
    {
        // Arrange
        var today = new DateTime(2024, 1, 5); // January 5, 2024 (first half)

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriodFromDate(today, PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 1), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 1, 15), result.PeriodEnd);
        Assert.Equal(new DateTime(2024, 1, 16), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_SecondHalf_FirstPayRun()
    {
        // Arrange
        var today = new DateTime(2024, 1, 20); // January 20, 2024 (second half)

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriodFromDate(today, PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 16), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 1, 31), result.PeriodEnd);
        Assert.Equal(new DateTime(2024, 2, 1), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_FromLastPayRun_FirstHalf()
    {
        // Arrange
        var lastPayRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 15),
            PayDate = new DateTime(2024, 1, 16)
        };

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 16), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 1, 31), result.PeriodEnd);
        Assert.Equal(new DateTime(2024, 2, 1), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_FromLastPayRun_SecondHalf()
    {
        // Arrange
        var lastPayRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 16),
            PeriodEnd = new DateTime(2024, 1, 31),
            PayDate = new DateTime(2024, 2, 1)
        };

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2024, 2, 1), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 2, 15), result.PeriodEnd);
        Assert.Equal(new DateTime(2024, 2, 16), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_ConsecutivePeriods()
    {
        // Arrange
        var firstHalf = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 15),
            PayDate = new DateTime(2024, 1, 16)
        };

        // Act
        var secondHalf = PayPeriodCalculator.CalculateNextPeriod(firstHalf, PayFrequency.SemiMonthly);
        var nextFirstHalf = PayPeriodCalculator.CalculateNextPeriod(
            new PayRun
            {
                PeriodStart = secondHalf.PeriodStart,
                PeriodEnd = secondHalf.PeriodEnd,
                PayDate = secondHalf.PayDate
            },
            PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 16), secondHalf.PeriodStart);
        Assert.Equal(new DateTime(2024, 1, 31), secondHalf.PeriodEnd);
        
        Assert.Equal(new DateTime(2024, 2, 1), nextFirstHalf.PeriodStart);
        Assert.Equal(new DateTime(2024, 2, 15), nextFirstHalf.PeriodEnd);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_FebruaryLeapYear()
    {
        // Arrange
        var lastPayRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 2, 1),
            PeriodEnd = new DateTime(2024, 2, 15),
            PayDate = new DateTime(2024, 2, 16)
        };

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2024, 2, 16), result.PeriodStart);
        Assert.Equal(new DateTime(2024, 2, 29), result.PeriodEnd); // 2024 is a leap year
        Assert.Equal(new DateTime(2024, 3, 1), result.PayDate);
    }

    [Fact]
    public void CalculateNextPeriod_SemiMonthly_FebruaryNonLeapYear()
    {
        // Arrange
        var lastPayRun = new PayRun
        {
            PeriodStart = new DateTime(2023, 2, 1),
            PeriodEnd = new DateTime(2023, 2, 15),
            PayDate = new DateTime(2023, 2, 16)
        };

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(lastPayRun, PayFrequency.SemiMonthly);

        // Assert
        Assert.Equal(new DateTime(2023, 2, 16), result.PeriodStart);
        Assert.Equal(new DateTime(2023, 2, 28), result.PeriodEnd); // 2023 is not a leap year
        Assert.Equal(new DateTime(2023, 3, 1), result.PayDate);
    }

    [Fact]
    public void GetPayFrequency_ConvertsPayPeriodsPerYearCorrectly()
    {
        // Assert
        Assert.Equal(PayFrequency.BiWeekly, PayPeriodCalculator.GetPayFrequency(26));
        Assert.Equal(PayFrequency.Monthly, PayPeriodCalculator.GetPayFrequency(12));
        Assert.Equal(PayFrequency.SemiMonthly, PayPeriodCalculator.GetPayFrequency(24));
        Assert.Equal(PayFrequency.BiWeekly, PayPeriodCalculator.GetPayFrequency(0)); // Default
        Assert.Equal(PayFrequency.BiWeekly, PayPeriodCalculator.GetPayFrequency(52)); // Unknown, defaults
    }

    [Fact]
    public void CalculateNextPeriod_NullLastPayRun_UsesToday()
    {
        // Arrange
        var today = DateTime.Today;

        // Act
        var result = PayPeriodCalculator.CalculateNextPeriod(null, PayFrequency.BiWeekly);

        // Assert
        // Should calculate from today
        Assert.True(result.PeriodStart >= today);
        Assert.True(result.PeriodEnd > result.PeriodStart);
        Assert.True(result.PayDate > result.PeriodEnd);
    }

    [Fact]
    public void CalculateNextPeriod_BiWeekly_StartsOnMonday()
    {
        // Arrange - Test different days of the week
        var monday = new DateTime(2024, 1, 15); // Monday
        var wednesday = new DateTime(2024, 1, 17); // Wednesday
        var friday = new DateTime(2024, 1, 19); // Friday
        var sunday = new DateTime(2024, 1, 21); // Sunday

        // Act
        var resultMonday = PayPeriodCalculator.CalculateNextPeriodFromDate(monday, PayFrequency.BiWeekly);
        var resultWednesday = PayPeriodCalculator.CalculateNextPeriodFromDate(wednesday, PayFrequency.BiWeekly);
        var resultFriday = PayPeriodCalculator.CalculateNextPeriodFromDate(friday, PayFrequency.BiWeekly);
        var resultSunday = PayPeriodCalculator.CalculateNextPeriodFromDate(sunday, PayFrequency.BiWeekly);

        // Assert - All should start on a Monday
        Assert.Equal(DayOfWeek.Monday, resultMonday.PeriodStart.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, resultWednesday.PeriodStart.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, resultFriday.PeriodStart.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, resultSunday.PeriodStart.DayOfWeek);
    }
}
