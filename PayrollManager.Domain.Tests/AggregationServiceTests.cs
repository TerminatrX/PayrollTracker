using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Aggregation totals must count only POSTED runs. Voided runs keep their stubs in the
/// database, so a missing status filter would inflate every dashboard and report total.
/// </summary>
public class AggregationServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"agg_{Guid.NewGuid()}")
            .Options);

    private static Employee AddEmployee(AppDbContext db)
    {
        var e = new Employee { FirstName = "Ann", LastName = "Worker", IsActive = true, IsHourly = true, HourlyRate = 25m };
        db.Employees.Add(e);
        db.SaveChanges();
        return e;
    }

    private static void AddRunWithStub(AppDbContext db, int employeeId, DateTime payDate, PayRunStatus status, decimal gross, decimal net)
    {
        var run = new PayRun
        {
            PeriodStart = payDate.AddDays(-14),
            PeriodEnd = payDate.AddDays(-1),
            PayDate = payDate,
            Status = status,
        };
        db.PayRuns.Add(run);
        db.SaveChanges();

        db.PayStubs.Add(new PayStub
        {
            EmployeeId = employeeId,
            PayRunId = run.Id,
            GrossPay = gross,
            TaxFederal = 0m,
            TaxState = 0m,
            TaxSocialSecurity = 0m,
            TaxMedicare = 0m,
            NetPay = net,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task CompanyYtdTotals_ExcludeVoidedRuns()
    {
        using var db = CreateDb();
        var employee = AddEmployee(db);

        AddRunWithStub(db, employee.Id, new DateTime(2026, 1, 15), PayRunStatus.Posted, gross: 2000m, net: 1600m);
        AddRunWithStub(db, employee.Id, new DateTime(2026, 1, 29), PayRunStatus.Voided, gross: 9999m, net: 8888m);

        var totals = await new AggregationService(db).GetCompanyYtdTotalsAsync(2026);

        // Only the posted run counts - the voided 9,999 is excluded.
        Assert.Equal(2000m, totals.GrossPay);
        Assert.Equal(1600m, totals.NetPay);
        Assert.Equal(1, totals.PayStubCount);
        Assert.Equal(1, totals.EmployeeCount);
    }

    [Fact]
    public async Task EmployeeYtdTotals_ExcludeVoidedRuns()
    {
        using var db = CreateDb();
        var employee = AddEmployee(db);

        AddRunWithStub(db, employee.Id, new DateTime(2026, 2, 15), PayRunStatus.Posted, gross: 2000m, net: 1600m);
        AddRunWithStub(db, employee.Id, new DateTime(2026, 3, 15), PayRunStatus.Voided, gross: 5000m, net: 4000m);

        var totals = await new AggregationService(db).GetEmployeeYtdTotalsAsync(employee.Id, 2026);

        Assert.Equal(2000m, totals.GrossPay);
        Assert.Equal(1, totals.PayStubCount);
    }

    [Fact]
    public async Task AllEmployeeTotals_ExcludeVoidedRuns()
    {
        using var db = CreateDb();
        var employee = AddEmployee(db);

        AddRunWithStub(db, employee.Id, new DateTime(2026, 1, 15), PayRunStatus.Posted, gross: 2000m, net: 1600m);
        AddRunWithStub(db, employee.Id, new DateTime(2026, 1, 29), PayRunStatus.Voided, gross: 3000m, net: 2400m);

        var totals = await new AggregationService(db)
            .GetAllEmployeeTotalsAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        var row = Assert.Single(totals);
        Assert.Equal(2000m, row.GrossPay);
        Assert.Equal(1, row.PayStubCount);
    }
}
