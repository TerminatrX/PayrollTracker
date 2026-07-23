using System.Text;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using PayrollManager.Domain.Services.Security;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Pay stub statement assembly (current + YTD), SSN protection, and PDF generation.
/// </summary>
public class PayStubStatementTests
{
    private static async Task<(AppDbContext Db, PayrollService Payroll)> ServicesAsync(
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{name}_{Guid.NewGuid()}").Options);

        var settings = new CompanySettingsService(db);
        await settings.SaveSettingsAsync(new CompanySettings
        {
            CompanyName = "Acme IL LLC",
            CompanyAddress = "1 State St, Chicago, IL",
            TaxId = "36-1234567",
            SocialSecurityPercent = 6.2m,
            MedicarePercent = 1.45m,
            PayPeriodsPerYear = 26,
            DefaultHoursPerPeriod = 80
        });

        return (db, new PayrollService(db, settings));
    }

    private static PayStubStatementService StatementService(AppDbContext db) =>
        new(db, new CompanySettingsService(db), new AggregationService(db));

    private static async Task<Employee> AddEmployeeAsync(AppDbContext db, Employee e)
    {
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    private static Employee SampleEmployee() => new()
    {
        FirstName = "Grace", LastName = "Hopper", IsActive = true,
        IsHourly = false, AnnualSalary = 78_000m, PreTax401kPercent = 5m,
        HealthInsurancePerPeriod = 120m, OtherDeductionsPerPeriod = 30m,
        StreetAddress = "500 W Madison St", City = "Chicago", State = "IL", PostalCode = "60661",
        SsnLast4 = "6789", SsnEncrypted = "not-real-cipher-for-test"
    };

    /// <summary>Posts a run and its stub for an employee, returning the saved stub id.</summary>
    private static async Task<int> PostStubAsync(AppDbContext db, PayrollService payroll, Employee employee, DateTime payDate)
    {
        var run = new PayRun
        {
            PeriodStart = payDate.AddDays(-14),
            PeriodEnd = payDate.AddDays(-1),
            PayDate = payDate,
            Status = PayRunStatus.Posted
        };
        db.PayRuns.Add(run);
        await db.SaveChangesAsync();

        var stub = await payroll.GeneratePayStubAsync(employee, run, new PayStubInput());
        db.PayStubs.Add(stub);
        await db.SaveChangesAsync();
        return stub.Id;
    }

    // ═══════════════════════════════════════════════════════════════
    // YTD-through-pay-date
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Ytd_IncludesStubsThroughPayDate_ExcludesLaterAndVoided()
    {
        var (db, payroll) = await ServicesAsync();
        using var _ = db;
        var employee = await AddEmployeeAsync(db, SampleEmployee());

        var jan = await PostStubAsync(db, payroll, employee, new DateTime(2026, 1, 15));
        await PostStubAsync(db, payroll, employee, new DateTime(2026, 2, 15));

        // A voided March run must not count.
        var marchRun = new PayRun { PeriodStart = new DateTime(2026, 3, 1), PeriodEnd = new DateTime(2026, 3, 14), PayDate = new DateTime(2026, 3, 15), Status = PayRunStatus.Voided };
        db.PayRuns.Add(marchRun);
        await db.SaveChangesAsync();
        var marchStub = await payroll.GeneratePayStubAsync(employee, marchRun, new PayStubInput());
        db.PayStubs.Add(marchStub);
        await db.SaveChangesAsync();

        var agg = new AggregationService(db);

        // Through Jan 15: only the January stub.
        var janYtd = await agg.GetEmployeeYtdThroughAsync(employee.Id, new DateTime(2026, 1, 15));
        Assert.Equal(1, janYtd.PayStubCount);

        // Through Feb 15: Jan + Feb, not the voided March.
        var febYtd = await agg.GetEmployeeYtdThroughAsync(employee.Id, new DateTime(2026, 2, 15));
        Assert.Equal(2, febYtd.PayStubCount);

        // The Jan stub's stored YtdGross must equal the through-Jan-15 computed gross.
        var janStub = db.PayStubs.First(s => s.Id == jan);
        Assert.Equal(janStub.YtdGross, janYtd.GrossPay);

        // Per-tax YTD is populated (the core gap this feature closes).
        Assert.True(febYtd.FederalTax > 0);
        Assert.True(febYtd.SocialSecurity > 0);
        Assert.True(febYtd.Medicare > 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Statement assembly
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildStatement_PopulatesCurrentAndYtd()
    {
        var (db, payroll) = await ServicesAsync();
        using var _ = db;
        var employee = await AddEmployeeAsync(db, SampleEmployee());
        var stubId = await PostStubAsync(db, payroll, employee, new DateTime(2026, 1, 15));

        var statement = await StatementService(db).BuildAsync(stubId);

        Assert.Equal("Grace Hopper", statement.Employee.FullName);
        Assert.Equal("XXX-XX-6789", statement.MaskedSsn);
        Assert.Equal("Bi-Weekly", statement.PayScheduleLabel);
        Assert.Equal(statement.PayStub.TaxFederal, statement.Ytd.FederalTax); // first stub: YTD == current
        Assert.Equal(statement.PayStub.YtdGross, statement.Ytd.GrossPay);
    }

    [Fact]
    public async Task BuildStatement_RejectsNonPostedRun()
    {
        var (db, payroll) = await ServicesAsync();
        using var _ = db;
        var employee = await AddEmployeeAsync(db, SampleEmployee());

        var draftRun = new PayRun { PeriodStart = new DateTime(2026, 1, 1), PeriodEnd = new DateTime(2026, 1, 14), PayDate = new DateTime(2026, 1, 15), Status = PayRunStatus.Draft };
        db.PayRuns.Add(draftRun);
        await db.SaveChangesAsync();
        var stub = await payroll.GeneratePayStubAsync(employee, draftRun, new PayStubInput());
        db.PayStubs.Add(stub);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PayStubNotPostedException>(() => StatementService(db).BuildAsync(stub.Id));
    }

    [Fact]
    public async Task BuildStatement_NotFound_Throws()
    {
        var (db, _) = await ServicesAsync();
        using var _d = db;
        await Assert.ThrowsAsync<PayStubNotFoundException>(() => StatementService(db).BuildAsync(9999));
    }

    // ═══════════════════════════════════════════════════════════════
    // PDF generation (QuestPDF / SkiaSharp smoke test)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratePayStubPdf_ProducesAValidPdf()
    {
        // This is the QuestPDF/SkiaSharp de-risk: if the native rendering stack cannot load,
        // this throws. A valid PDF starts with the "%PDF" magic bytes.
        var (db, payroll) = await ServicesAsync();
        using var _ = db;
        var employee = await AddEmployeeAsync(db, SampleEmployee());
        var stubId = await PostStubAsync(db, payroll, employee, new DateTime(2026, 1, 15));

        var statement = await StatementService(db).BuildAsync(stubId);
        var export = new ExportService(db, new CompanySettingsService(db));

        var pdf = export.GeneratePayStubPdfBytes(statement);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1000, $"PDF unexpectedly small ({pdf.Length} bytes)");
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // ═══════════════════════════════════════════════════════════════
    // SSN protection (Windows DPAPI)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SsnProtector_RoundTripsAndExtractsLast4()
    {
        var protector = new DpapiSsnProtector();

        var result = protector.Protect("123-45-6789");
        Assert.Equal("6789", result.Last4);
        Assert.False(string.IsNullOrEmpty(result.Encrypted));
        Assert.DoesNotContain("6789", result.Encrypted); // ciphertext, not plaintext

        Assert.Equal("123456789", protector.Unprotect(result.Encrypted));
    }

    [Theory]
    [InlineData("123456789", true)]
    [InlineData("123-45-6789", true)]
    [InlineData("12345678", false)]   // too short
    [InlineData("1234567890", false)] // too long
    [InlineData("abc-de-fghi", false)]
    public void SsnProtector_ValidatesFormat(string input, bool valid)
    {
        Assert.Equal(valid, new DpapiSsnProtector().IsValidSsn(input));
    }
}
