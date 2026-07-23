using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// FLSA overtime splitting. The central rule: overtime accrues per 7-day workweek, and hours
/// may not be averaged across workweeks.
/// </summary>
public class FlsaOvertimeTests
{
    [Fact]
    public void TwoStandardWorkweeks_OweNoOvertime()
    {
        var (regular, overtime) = FlsaOvertime.SplitByWorkweek(new[] { 40m, 40m });

        Assert.Equal(80m, regular);
        Assert.Equal(0m, overtime);
    }

    [Fact]
    public void OvertimeAccruesPerWorkweek_NotAcrossThePeriod()
    {
        // 50 + 30 = 80 total hours. Averaging across the period would show zero overtime,
        // but the employee is owed 10 overtime hours for the 50-hour week. Underpaying here
        // is a wage-and-hour violation, not a rounding difference.
        var (regular, overtime) = FlsaOvertime.SplitByWorkweek(new[] { 50m, 30m });

        Assert.Equal(70m, regular);
        Assert.Equal(10m, overtime);
    }

    [Fact]
    public void EvenSplit_OfAStandardBiweeklyPeriod_ProducesNoOvertime()
    {
        var (regular, overtime) = FlsaOvertime.SplitEvenlyAcrossWorkweeks(80m, workweeksInPeriod: 2);

        Assert.Equal(80m, regular);
        Assert.Equal(0m, overtime);
    }

    [Fact]
    public void EvenSplit_AboveThreshold_SplitsThePremiumAcrossWeeks()
    {
        // 90 over two weeks = 45/week = 5 overtime hours each week.
        var (regular, overtime) = FlsaOvertime.SplitEvenlyAcrossWorkweeks(90m, workweeksInPeriod: 2);

        Assert.Equal(80m, regular);
        Assert.Equal(10m, overtime);
    }

    [Fact]
    public void EvenSplit_WeeklyPeriod_UsesTheFortyHourThreshold()
    {
        var (regular, overtime) = FlsaOvertime.SplitEvenlyAcrossWorkweeks(46m, workweeksInPeriod: 1);

        Assert.Equal(40m, regular);
        Assert.Equal(6m, overtime);
    }

    [Fact]
    public void PartTimeHours_AreAllRegular()
    {
        var (regular, overtime) = FlsaOvertime.SplitEvenlyAcrossWorkweeks(30m, workweeksInPeriod: 2);

        Assert.Equal(30m, regular);
        Assert.Equal(0m, overtime);
    }

    [Theory]
    [InlineData(52, 1)]
    [InlineData(26, 2)]
    [InlineData(24, 2)]
    [InlineData(12, 4)]
    public void WorkweeksInPeriod_MapsPayFrequency(int payPeriodsPerYear, int expectedWorkweeks)
    {
        Assert.Equal(expectedWorkweeks, FlsaOvertime.WorkweeksInPeriod(payPeriodsPerYear));
    }

    [Fact]
    public void NegativeHours_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FlsaOvertime.SplitByWorkweek(new[] { 40m, -5m }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FlsaOvertime.SplitEvenlyAcrossWorkweeks(-1m, 2));
    }

    [Fact]
    public void ZeroWorkweeks_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FlsaOvertime.SplitEvenlyAcrossWorkweeks(80m, workweeksInPeriod: 0));
    }
}
