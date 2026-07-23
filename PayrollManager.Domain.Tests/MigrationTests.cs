using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Migration tests run against real SQLite, not the in-memory provider - the in-memory
/// provider ignores schema entirely and so cannot catch a broken migration.
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"payroll_migtest_{Guid.NewGuid():N}.db");

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void AllMigrations_ApplyCleanlyFromEmpty()
    {
        using var db = CreateContext();
        db.Database.Migrate();

        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public void ExistingEmployeeRows_SurviveTheW4Migration()
    {
        // Bring the schema up to the migration immediately BEFORE the W-4 columns were added.
        using (var db = CreateContext())
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            migrator.Migrate("20260120090000_AddEmployeeDefaultHoursPerPeriod");

            // Insert a row the old way - raw SQL, because the entity now has columns this
            // schema version does not yet know about.
            db.Database.ExecuteSqlRaw(@"
                INSERT INTO Employees
                    (FirstName, LastName, IsActive, IsHourly, AnnualSalary, HourlyRate,
                     PreTax401kPercent, HealthInsurancePerPeriod, OtherDeductionsPerPeriod,
                     DefaultHoursPerPeriod)
                VALUES ('Legacy', 'Employee', 1, 1, 0, 25.0, 0, 0, 0, 80);");
        }

        // Apply the remaining migrations, including the W-4 columns.
        using (var db = CreateContext())
        {
            db.Database.Migrate();
        }

        // The pre-existing row must still be readable. This is what fails if FilingStatus is
        // backfilled with "" instead of a valid enum name.
        using (var db = CreateContext())
        {
            var employee = db.Employees.Single();

            Assert.Equal("Legacy", employee.FirstName);
            Assert.Equal(25.0m, employee.HourlyRate);

            // Backfilled defaults for a row that predates the W-4 columns.
            Assert.Equal(FilingStatus.Single, employee.FilingStatus);
            Assert.False(employee.W4OnFile);      // no real W-4 has been collected for them
            Assert.False(employee.W4MultipleJobsChecked);
            Assert.Equal(0m, employee.W4ExtraWithholding);
            Assert.Equal(0, employee.IlBasicAllowances);
            Assert.Null(employee.HireDate);
            Assert.Null(employee.TerminationDate);
        }
    }

    [Fact]
    public void ExistingPayRuns_AreBackfilledAsPostedLegacyRuns()
    {
        // Pre-lifecycle pay runs already produced pay stubs, so they represent committed
        // payroll: they must come back as Posted (immutable), not Draft, and must be stamped
        // with the engine that actually computed them - flat-rate withholding, FICA on gross.
        using (var db = CreateContext())
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            migrator.Migrate("20260722032526_AddW4AndEmploymentDateFields");

            db.Database.ExecuteSqlRaw(@"
                INSERT INTO PayRuns (PeriodStart, PeriodEnd, PayDate)
                VALUES ('2026-01-01 00:00:00', '2026-01-14 00:00:00', '2026-01-15 00:00:00');");
        }

        using (var db = CreateContext())
        {
            db.Database.Migrate();
        }

        using (var db = CreateContext())
        {
            var run = db.PayRuns.Single();

            Assert.Equal(PayRunStatus.Posted, run.Status);
            Assert.True(run.IsImmutable);
            Assert.Equal("1.0-legacy-flat-rate", run.CalculationEngineVersion);
            Assert.NotNull(run.PostedAtUtc);

            // And the immutability guard applies to it immediately.
            run.PayDate = run.PayDate.AddDays(1);
            Assert.Throws<ImmutablePayrollRecordException>(() => db.SaveChanges());
        }
    }

    [Fact]
    public void FutaCreditDefault_FavorsTheNetRate()
    {
        // Backfilling false would compute FUTA at the 6.0% gross rate instead of 0.6%,
        // overstating employer unemployment cost roughly tenfold.
        using var db = CreateContext();
        db.Database.Migrate();

        var settings = new CompanySettingsService(db).GetSettings();

        Assert.True(settings.ReceivesFullFutaCredit);

        // No defensible default exists for these - they come from the IDES rate notice.
        Assert.Equal(0m, settings.SuiRatePercent);
        Assert.Equal(0m, settings.SuiWageBase);
    }

    [Fact]
    public void HireDate_RoundTripsToTheDatabase()
    {
        // Defect #9: HireDate was collected in the UI but never persisted, because it existed
        // only on the ViewModel. Now that it is a real column, prove it survives a round trip.
        var hired = new DateTime(2023, 4, 17);

        using (var db = CreateContext())
        {
            db.Database.Migrate();
            db.Employees.Add(new Employee
            {
                FirstName = "Hire", LastName = "Date", IsHourly = true, HourlyRate = 30m,
                HireDate = hired, FilingStatus = FilingStatus.HeadOfHousehold,
                W4OnFile = true, IlBasicAllowances = 2
            });
            db.SaveChanges();
        }

        using (var db = CreateContext())
        {
            var employee = db.Employees.Single();

            Assert.Equal(hired, employee.HireDate);
            Assert.Equal(FilingStatus.HeadOfHousehold, employee.FilingStatus);
            Assert.True(employee.W4OnFile);
            Assert.Equal(2, employee.IlBasicAllowances);
        }
    }

    public void Dispose()
    {
        // SQLite keeps a pooled connection open; release it before deleting the file.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
