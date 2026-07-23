using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Pay-run lifecycle and posting integrity.
///
/// These run against real SQLite because the in-memory provider does not support
/// transactions - it would silently no-op the exact behavior under test.
/// </summary>
public class PayRunServiceTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"payroll_runtest_{Guid.NewGuid():N}.db");

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        var db = new AppDbContext(options);
        db.Database.Migrate();
        return db;
    }

    private static (PayRunService Runs, PayrollService Payroll) CreateServices(AppDbContext db)
    {
        var settingsService = new CompanySettingsService(db);
        settingsService.SaveSettingsAsync(new CompanySettings
        {
            CompanyName = "Test Co",
            SocialSecurityPercent = 6.2m,
            MedicarePercent = 1.45m,
            PayPeriodsPerYear = 26,
            DefaultHoursPerPeriod = 80
        }).GetAwaiter().GetResult();

        var payroll = new PayrollService(db, settingsService);
        return (new PayRunService(db, payroll), payroll);
    }

    private static Employee AddEmployee(AppDbContext db, string first, bool hourly = true, bool w4 = true)
    {
        var employee = new Employee
        {
            FirstName = first,
            LastName = "Tester",
            IsActive = true,
            IsHourly = hourly,
            HourlyRate = hourly ? 25m : 0m,
            AnnualSalary = hourly ? 0m : 78_000m,
            W4OnFile = w4
        };
        db.Employees.Add(employee);
        db.SaveChanges();
        return employee;
    }

    private static PayRunDraft Draft(DateTime payDate) => new()
    {
        PeriodStart = payDate.AddDays(-14),
        PeriodEnd = payDate.AddDays(-1),
        PayDate = payDate
    };

    private static PayRunEmployeeInput Input(int employeeId, decimal regular = 80m) =>
        new() { EmployeeId = employeeId, RegularHours = regular };

    // ═══════════════════════════════════════════════════════════════
    // POSTING
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Post_CommitsRunAndAllStubsTogether()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var a = AddEmployee(db, "Ann");
        var b = AddEmployee(db, "Bob");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(a.Id), Input(b.Id) };

        var preview = await runs.PreviewAsync(draft, inputs);
        Assert.True(preview.CanPost);
        Assert.Equal(2, preview.Totals.EmployeeCount);
        Assert.Equal(4000m, preview.Totals.GrossPay);   // 2 x 80h x 25

        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash, "tester");

        Assert.Equal(PayRunStatus.Posted, posted.Status);
        Assert.NotNull(posted.PostedAtUtc);
        Assert.Equal(PayRunService.EngineVersion, posted.CalculationEngineVersion);
        Assert.Equal(2, db.PayStubs.Count(s => s.PayRunId == posted.Id));

        var audit = db.AuditLog.Single(a => a.Action == AuditAction.PayRunPosted);
        Assert.Equal(posted.Id, audit.EntityId);
        Assert.Equal("tester", audit.PerformedBy);
    }

    [Fact]
    public async Task Post_RollsBackEntirely_WhenAnEmployeeDisappearsMidRun()
    {
        // The previous implementation saved the PayRun first and `continue`d past a missing
        // employee, leaving an orphaned run and a PARTIAL payroll. Nothing may survive now.
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var a = AddEmployee(db, "Ann");
        var b = AddEmployee(db, "Bob");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(a.Id), Input(b.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);

        // Delete one employee after the preview was approved.
        db.Employees.Remove(db.Employees.Find(b.Id)!);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PayRunPostingException>(
            () => runs.PostAsync(draft, inputs, preview.CalculationHash));

        Assert.Empty(db.PayRuns);
        Assert.Empty(db.PayStubs);
    }

    [Fact]
    public async Task Post_IsRefused_WhenFiguresChangedSinceReview()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);

        // Someone raises the rate between review and posting.
        db.Employees.Find(employee.Id)!.HourlyRate = 40m;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<PayRunPostingException>(
            () => runs.PostAsync(draft, inputs, preview.CalculationHash));

        Assert.Contains("changed since it was reviewed", ex.Message);
        Assert.Empty(db.PayRuns);
    }

    [Fact]
    public async Task Post_IsBlocked_ForAnOverlappingPeriod()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var first = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(first, inputs);
        await runs.PostAsync(first, inputs, preview.CalculationHash);

        // Same period again - the classic duplicate-payroll mistake.
        var duplicate = await runs.PreviewAsync(first, inputs);

        Assert.False(duplicate.CanPost);
        Assert.Contains(duplicate.Warnings,
            w => w.Code == PayRunWarningCode.OverlappingPayPeriod && w.BlocksPosting);

        await Assert.ThrowsAsync<PayRunPostingException>(
            () => runs.PostAsync(first, inputs, duplicate.CalculationHash));

        Assert.Single(db.PayRuns);
    }

    [Fact]
    public async Task Post_IsBlocked_ByNegativeNetPay()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);

        var employee = AddEmployee(db, "Zero");
        employee.HealthInsurancePerPeriod = 100m;
        employee.OtherDeductionsPerPeriod = 50m;
        db.SaveChanges();

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id, regular: 0m) };

        var preview = await runs.PreviewAsync(draft, inputs);

        Assert.False(preview.CanPost);
        Assert.Contains(preview.Warnings,
            w => w.Code == PayRunWarningCode.NegativeNetPay && w.BlocksPosting);
    }

    [Fact]
    public async Task Post_IsBlocked_ForAnEmployeeTerminatedBeforeThePeriod()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);

        var employee = AddEmployee(db, "Gone");
        employee.TerminationDate = new DateTime(2025, 12, 1);
        db.SaveChanges();

        var draft = Draft(new DateTime(2026, 1, 15));
        var preview = await runs.PreviewAsync(draft, new[] { Input(employee.Id) });

        Assert.False(preview.CanPost);
        Assert.Contains(preview.Warnings,
            w => w.Code == PayRunWarningCode.EmployeeTerminated && w.BlocksPosting);
    }

    [Fact]
    public async Task MissingW4_WarnsButDoesNotBlock()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "NoForm", w4: false);

        var preview = await runs.PreviewAsync(
            Draft(new DateTime(2026, 1, 15)), new[] { Input(employee.Id) });

        Assert.Contains(preview.Warnings, w => w.Code == PayRunWarningCode.NoW4OnFile);
        Assert.True(preview.CanPost);
    }

    [Fact]
    public async Task DuplicateEmployee_BlocksPosting()
    {
        // The same person twice in one run would receive two stubs for one period, and the
        // second would not even see the first in its YTD figures.
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Twice");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id), Input(employee.Id) };

        var preview = await runs.PreviewAsync(draft, inputs);

        Assert.False(preview.CanPost);
        await Assert.ThrowsAsync<PayRunPostingException>(
            () => runs.PostAsync(draft, inputs, preview.CalculationHash));

        Assert.Empty(db.PayRuns);
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYER TAXES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EmployerTaxes_AreComputedAndKeptSeparateFromWithholding()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);

        // Configure SUI the way an employer would from their IDES rate notice.
        var settingsService = new CompanySettingsService(db);
        var settings = await settingsService.GetSettingsAsync();
        settings.SuiRatePercent = 3.0m;
        settings.SuiWageBase = 13_590m;
        await settingsService.SaveSettingsAsync(settings);

        var employee = AddEmployee(db, "Ann");
        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };

        var preview = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash);

        var stub = db.PayStubs.Single(s => s.PayRunId == posted.Id);

        // Gross 2,000 (80h x 25), no §125 deductions, so FICA wages are 2,000.
        Assert.Equal(124m, stub.EmployerSocialSecurity);   // 2000 * 6.2%
        Assert.Equal(29m, stub.EmployerMedicare);          // 2000 * 1.45%
        Assert.Equal(12m, stub.EmployerFuta);              // 2000 * 0.6%
        Assert.Equal(60m, stub.EmployerSui);               // 2000 * 3.0%
        Assert.Equal(225m, stub.TotalEmployerTaxes);
        Assert.Equal(2225m, stub.TotalEmployerCost);       // gross + employer taxes

        // Critically: employer taxes are NOT part of what was withheld from the employee.
        Assert.Equal(stub.TaxFederal + stub.TaxState + stub.TaxSocialSecurity + stub.TaxMedicare,
                     stub.TotalTaxes);
        Assert.Equal(stub.TotalTaxes, stub.TaxLines.Sum(t => t.Amount));

        // And net pay is unaffected by employer cost.
        Assert.Equal(stub.GrossPay - stub.TotalTaxes - stub.PostTaxDeductions, stub.NetPay);
    }

    [Fact]
    public async Task PayRunTotals_ReportEmployerCostSeparately()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var a = AddEmployee(db, "Ann");
        var b = AddEmployee(db, "Bob");

        var preview = await runs.PreviewAsync(
            Draft(new DateTime(2026, 1, 15)), new[] { Input(a.Id), Input(b.Id) });

        Assert.Equal(4000m, preview.Totals.GrossPay);

        // Employer taxes are a cost on TOP of gross, never netted into employee figures.
        Assert.True(preview.Totals.EmployerTaxes > 0);
        Assert.Equal(preview.Totals.GrossPay + preview.Totals.EmployerTaxes,
                     preview.Totals.TotalEmployerCost);
        Assert.NotEqual(preview.Totals.EmployeeTaxes, preview.Totals.EmployerTaxes);
    }

    [Fact]
    public async Task UnconfiguredSui_WarnsOnceForTheWholeRun()
    {
        // Default settings have no SUI configured.
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var a = AddEmployee(db, "Ann");
        var b = AddEmployee(db, "Bob");

        var preview = await runs.PreviewAsync(
            Draft(new DateTime(2026, 1, 15)), new[] { Input(a.Id), Input(b.Id) });

        var suiWarnings = preview.Warnings.Where(w => w.Message.Contains("IDES rate notice")).ToList();

        Assert.Single(suiWarnings);          // deduplicated, not repeated per employee
        Assert.True(preview.CanPost);        // a warning, not a blocker
    }

    // ═══════════════════════════════════════════════════════════════
    // IMMUTABILITY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostedRun_CannotBeEdited()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash);

        posted.PayDate = posted.PayDate.AddDays(7);

        Assert.Throws<ImmutablePayrollRecordException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task PostedRun_CannotBeDeleted()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash);

        db.PayRuns.Remove(posted);

        var ex = Assert.Throws<ImmutablePayrollRecordException>(() => db.SaveChanges());
        Assert.Contains("Void it instead", ex.Message);
    }

    [Fact]
    public async Task PostedPayStub_CannotBeEdited()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        await runs.PostAsync(draft, inputs, preview.CalculationHash);

        var stub = db.PayStubs.First();
        stub.NetPay = 999_999m;

        Assert.Throws<ImmutablePayrollRecordException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task PostedPayStub_TaxLine_CannotBeEdited()
    {
        // The stub itself was already protected; its child lines were not. Editing a posted
        // TaxLine directly must be rejected too - those rows are read back to reconstruct
        // taxable wages for later YTD wage-base limits.
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        await runs.PostAsync(draft, inputs, preview.CalculationHash);

        var taxLine = db.TaxLines.First();
        taxLine.TaxableAmount = 0m;

        Assert.Throws<ImmutablePayrollRecordException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task PostedPayStub_EarningAndDeductionLines_CannotBeEditedOrDeleted()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");
        employee.HealthInsurancePerPeriod = 100m;   // ensure a deduction line exists
        db.SaveChanges();

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        await runs.PostAsync(draft, inputs, preview.CalculationHash);

        // Editing an earning line is rejected.
        using (var db2 = CreateContext())
        {
            var earning = db2.EarningLines.First();
            earning.Amount = 1m;
            Assert.Throws<ImmutablePayrollRecordException>(() => db2.SaveChanges());
        }

        // Deleting a deduction line is rejected.
        using (var db3 = CreateContext())
        {
            var deduction = db3.DeductionLines.First();
            db3.DeductionLines.Remove(deduction);
            Assert.Throws<ImmutablePayrollRecordException>(() => db3.SaveChanges());
        }
    }

    [Fact]
    public async Task AuditLog_IsAppendOnly()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        await runs.PostAsync(draft, inputs, preview.CalculationHash);

        var entry = db.AuditLog.First();
        entry.NewValue = "tampered";

        Assert.Throws<ImmutablePayrollRecordException>(() => db.SaveChanges());
    }

    // ═══════════════════════════════════════════════════════════════
    // VOIDING
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Void_RetainsTheRunAndItsStubs()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash);

        await runs.VoidAsync(posted.Id, "Wrong pay period selected", "tester");

        var reloaded = db.PayRuns.Single();
        Assert.Equal(PayRunStatus.Voided, reloaded.Status);
        Assert.NotNull(reloaded.VoidedAtUtc);
        Assert.Equal("Wrong pay period selected", reloaded.VoidReason);

        // The stubs survive - voiding reverses, it does not erase.
        Assert.Single(db.PayStubs);
        Assert.Contains(db.AuditLog, a => a.Action == AuditAction.PayRunVoided);
    }

    [Fact]
    public async Task Void_RequiresAReason()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash);

        await Assert.ThrowsAsync<ArgumentException>(() => runs.VoidAsync(posted.Id, "   "));
    }

    [Fact]
    public async Task VoidedPeriod_NoLongerBlocksANewRun()
    {
        // Voiding frees the period, which is what makes "void and re-run" a usable correction.
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var first = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, first.CalculationHash);

        await runs.VoidAsync(posted.Id, "Rebuilding with corrected hours");

        var replacement = await runs.PreviewAsync(draft, inputs);

        Assert.DoesNotContain(replacement.Warnings, w => w.Code == PayRunWarningCode.OverlappingPayPeriod);
        Assert.True(replacement.CanPost);
    }

    [Fact]
    public async Task VoidedRun_DoesNotInflateYtdOnALaterRun()
    {
        // A voided run's stubs stay in the database. They must NOT count toward YTD on a later
        // run, or the replacement would see doubled YTD gross and have its Social Security /
        // FUTA / SUI / 401(k) wage bases partly consumed by wages that were reversed.
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Sal");   // hourly $25, 80h => $2,000 gross

        // First run, then voided.
        var firstDraft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var firstPreview = await runs.PreviewAsync(firstDraft, inputs);
        var firstRun = await runs.PostAsync(firstDraft, inputs, firstPreview.CalculationHash);
        await runs.VoidAsync(firstRun.Id, "Wrong hours");

        // A later run for the same employee, same year.
        var secondDraft = Draft(new DateTime(2026, 1, 29));
        var secondPreview = await runs.PreviewAsync(secondDraft, inputs);
        var secondRun = await runs.PostAsync(secondDraft, inputs, secondPreview.CalculationHash);

        var secondStub = db.PayStubs.Single(s => s.PayRunId == secondRun.Id);

        // YTD reflects only the second (posted) run's $2,000 - not $4,000 including the void.
        Assert.Equal(2000m, secondStub.YtdGross);
        Assert.Equal(secondStub.NetPay, secondStub.YtdNet);

        // And Social Security is charged on the full $2,000 again, proving the wage base was
        // not consumed by the voided run (2000 * 6.2% = 124.00).
        Assert.Equal(124m, secondStub.TaxSocialSecurity);
    }

    [Fact]
    public async Task VoidedRun_CannotBeVoidedAgain()
    {
        using var db = CreateContext();
        var (runs, _) = CreateServices(db);
        var employee = AddEmployee(db, "Ann");

        var draft = Draft(new DateTime(2026, 1, 15));
        var inputs = new[] { Input(employee.Id) };
        var preview = await runs.PreviewAsync(draft, inputs);
        var posted = await runs.PostAsync(draft, inputs, preview.CalculationHash);

        await runs.VoidAsync(posted.Id, "First void");

        await Assert.ThrowsAsync<PayRunPostingException>(
            () => runs.VoidAsync(posted.Id, "Second void"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
