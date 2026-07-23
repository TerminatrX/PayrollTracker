using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using PayrollManager.Domain.Services.Tax;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Characterization tests for the payroll calculation engine (PayrollService).
///
/// Every expected value below is derived by hand from the stated inputs, so these tests
/// specify intended behavior rather than merely echoing current output.
///
/// Tests tagged [Trait("Pinned", "bug")] document behavior that is currently WRONG. They
/// exist so the defect is visible and so a fix produces an obvious, reviewable test change.
/// Do NOT treat a tagged test passing as evidence of correctness - each names the defect it
/// pins and the value it should produce once fixed.
/// </summary>
public class PayrollCalculationTests
{
    // Standard settings used across tests: round tax percentages keep the arithmetic checkable.
    private const decimal FederalPct = 10m;
    private const decimal StatePct = 5m;
    private const decimal SocialSecurityPct = 6.2m;
    private const decimal MedicarePct = 1.45m;

    private static async Task<(AppDbContext Db, PayrollService Payroll)> CreateServicesAsync(
        [System.Runtime.CompilerServices.CallerMemberName] string dbName = "")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"{dbName}_{Guid.NewGuid()}")
            .Options;

        var dbContext = new AppDbContext(options);
        var settingsService = new CompanySettingsService(dbContext);

        await settingsService.SaveSettingsAsync(new CompanySettings
        {
            CompanyName = "Test Company",
            SocialSecurityPercent = SocialSecurityPct,
            MedicarePercent = MedicarePct,
            PayPeriodsPerYear = 26,
            DefaultHoursPerPeriod = 80
        });

        return (dbContext, new PayrollService(dbContext, settingsService));
    }

    private static async Task<Employee> AddEmployeeAsync(AppDbContext db, Employee employee)
    {
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    private static async Task<PayRun> AddPayRunAsync(AppDbContext db, DateTime payDate)
    {
        var payRun = new PayRun
        {
            PeriodStart = payDate.AddDays(-14),
            PeriodEnd = payDate.AddDays(-1),
            PayDate = payDate
        };
        db.PayRuns.Add(payRun);
        await db.SaveChangesAsync();
        return payRun;
    }

    // ═══════════════════════════════════════════════════════════════
    // BASELINE: correct behavior that must not regress
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Salaried_NoDeductions_ProducesExpectedGrossTaxesAndNet()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        // 78,000 / 26 periods = 3,000.00 gross per period
        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Sal", LastName = "Aried", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        Assert.Equal(3000m, stub.GrossPay);

        // Federal, Pub 15-T Worksheet 1A (default W-4: Single, no steps 2/3/4):
        //   3,000 * 26 = 78,000; less 8,600 = 69,400 adjusted annual wage
        //   69,400 sits in 57,900-113,200 @ 22%, base 5,800 -> 8,330 annual; / 26 = 320.38
        Assert.Equal(320.38m, stub.TaxFederal);
        Assert.Equal(148.50m, stub.TaxState);           // Illinois flat 4.95%, no allowances
        Assert.Equal(186m, stub.TaxSocialSecurity);     // 3000 * 6.2%
        Assert.Equal(43.50m, stub.TaxMedicare);         // 3000 * 1.45%
        Assert.Equal(698.38m, stub.TotalTaxes);         // 320.38 + 148.50 + 186 + 43.50
        Assert.Equal(2301.62m, stub.NetPay);            // 3000 - 698.38
        Assert.Equal(0m, stub.HoursWorked);             // salaried: hours not tracked
    }

    [Fact]
    public async Task Hourly_WithOvertime_AppliesTimeAndAHalf()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Hour", LastName = "Ly", IsActive = true,
            IsHourly = true, HourlyRate = 25m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput
        {
            RegularHours = 80m,
            OvertimeHours = 10m
        });

        // 80 * 25 = 2000 regular; 10 * (25 * 1.5) = 375 overtime
        Assert.Equal(2000m, stub.RegularEarnings);
        Assert.Equal(375m, stub.OvertimeEarnings);
        Assert.Equal(2375m, stub.GrossPay);
        Assert.Equal(90m, stub.HoursWorked);
    }

    [Fact]
    public async Task PreTax401k_ReducesIncomeTax_ButNotFica()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        // 401(k) deferrals are exempt from federal/state income tax but remain FICA-taxable.
        // This is correct today and MUST NOT change when the §125 FICA defect is fixed.
        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Four", LastName = "OhOneKay", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m, PreTax401kPercent = 10m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        Assert.Equal(3000m, stub.GrossPay);
        Assert.Equal(300m, stub.PreTax401kDeduction);   // 3000 * 10%

        // Income tax computed on 2,700:
        //   2,700 * 26 = 70,200; less 8,600 = 61,600
        //   5,800 + (61,600 - 57,900) * 0.22 = 6,614 annual; / 26 = 254.38
        Assert.Equal(254.38m, stub.TaxFederal);         // <- reduced by the deferral
        Assert.Equal(133.65m, stub.TaxState);           // 2700 * 4.95%        <- reduced
        Assert.Equal(186m, stub.TaxSocialSecurity);     // 3000 * 6.2%         <- NOT reduced
        Assert.Equal(43.50m, stub.TaxMedicare);         // 3000 * 1.45%        <- NOT reduced
    }

    [Fact]
    public async Task YtdTotals_AccumulateAcrossPayRuns()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Wai", LastName = "Teedee", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m
        });

        var firstRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));
        var firstStub = await payroll.GeneratePayStubAsync(employee, firstRun, new PayStubInput());
        db.PayStubs.Add(firstStub);
        await db.SaveChangesAsync();

        var secondRun = await AddPayRunAsync(db, new DateTime(2026, 1, 29));
        var secondStub = await payroll.GeneratePayStubAsync(employee, secondRun, new PayStubInput());

        Assert.Equal(3000m, firstStub.YtdGross);
        Assert.Equal(6000m, secondStub.YtdGross);       // 3000 + 3000
        Assert.Equal(1396.76m, secondStub.YtdTaxes);    // 698.38 * 2
        Assert.Equal(4603.24m, secondStub.YtdNet);      // 2301.62 * 2
    }

    [Fact]
    public async Task Preview_And_Generate_ProduceIdenticalNumbers()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Pre", LastName = "View", IsActive = true,
            IsHourly = true, HourlyRate = 31.25m,
            PreTax401kPercent = 6m, HealthInsurancePerPeriod = 90m, OtherDeductionsPerPeriod = 25m
        });
        var payDate = new DateTime(2026, 3, 15);
        var payRun = await AddPayRunAsync(db, payDate);
        var input = new PayStubInput { RegularHours = 80m, OvertimeHours = 4m, BonusAmount = 500m };

        var preview = await payroll.PreviewPayStubAsync(
            employee,
            new PayRunDraft { PeriodStart = payRun.PeriodStart, PeriodEnd = payRun.PeriodEnd, PayDate = payDate },
            input);
        var stub = await payroll.GeneratePayStubAsync(employee, payRun, input);

        // The preview the user approves must match what gets posted, to the cent.
        Assert.Equal(preview.GrossPay, stub.GrossPay);
        Assert.Equal(preview.NetPay, stub.NetPay);
        Assert.Equal(preview.TotalTaxes, stub.TotalTaxes);
        Assert.Equal(preview.TaxFederal, stub.TaxFederal);
        Assert.Equal(preview.TaxSocialSecurity, stub.TaxSocialSecurity);
        Assert.Equal(preview.PreTax401kDeduction, stub.PreTax401kDeduction);
    }

    // ═══════════════════════════════════════════════════════════════
    // ANNUAL LIMITS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SocialSecurity_StopsAtWageBase()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Wage", LastName = "Base", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m
        });

        // Prior YTD gross of 182,900 leaves 1,600 under the 2026 wage base of 184,500.
        // This stub carries NO tax lines, exercising the legacy fallback that treats gross as
        // the Social Security wage basis for stubs predating the tax-line migration.
        var priorRun = await AddPayRunAsync(db, new DateTime(2026, 11, 15));
        db.PayStubs.Add(new PayStub
        {
            EmployeeId = employee.Id, PayRunId = priorRun.Id, PayRun = priorRun,
            GrossPay = 182_900m, NetPay = 100_000m
        });
        await db.SaveChangesAsync();

        var payRun = await AddPayRunAsync(db, new DateTime(2026, 11, 29));
        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        // Only 1,600 of the 3,000 gross is still subject to Social Security.
        Assert.Equal(99.20m, stub.TaxSocialSecurity);   // 1600 * 6.2%
        Assert.Equal(43.50m, stub.TaxMedicare);         // Medicare has no wage base: 3000 * 1.45%
    }

    // ═══════════════════════════════════════════════════════════════
    // YEAR-INDEXED STATUTORY LIMITS (was DEFECT #3: hardcoded 2024 values)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SocialSecurityWageBase_UsesTheYearOfThePayDate()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Twenty", LastName = "TwentySix", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m
        });

        // Prior SS wages of 183,000 against the 2026 base of 184,500 leaves 1,500 of headroom.
        // Under the old hardcoded 2024 base (168,600) this employee would be fully exempt,
        // silently under-withholding Social Security.
        var priorRun = await AddPayRunAsync(db, new DateTime(2026, 11, 13));
        var priorStub = new PayStub
        {
            EmployeeId = employee.Id, PayRunId = priorRun.Id, PayRun = priorRun,
            GrossPay = 183_000m, NetPay = 120_000m
        };
        priorStub.TaxLines.Add(new TaxLine
        {
            Type = TaxType.SocialSecurity, Amount = 11_346m,
            Rate = 6.2m, TaxableAmount = 183_000m, Description = "Social Security"
        });
        db.PayStubs.Add(priorStub);
        await db.SaveChangesAsync();

        var payRun = await AddPayRunAsync(db, new DateTime(2026, 11, 27));
        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        Assert.Equal(93m, stub.TaxSocialSecurity);   // 1500 * 6.2%
    }

    [Fact]
    public async Task ElectiveDeferralLimit_UsesTheYearOfThePayDate()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Deferral", LastName = "Limit", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m, PreTax401kPercent = 10m
        });

        // 24,400 contributed against the 2026 §402(g) limit of 24,500 leaves 100.
        // Under the old hardcoded 23,000 limit this would clamp to 0.
        var priorRun = await AddPayRunAsync(db, new DateTime(2026, 6, 12));
        db.PayStubs.Add(new PayStub
        {
            EmployeeId = employee.Id, PayRunId = priorRun.Id, PayRun = priorRun,
            GrossPay = 60_000m, PreTax401kDeduction = 24_400m, NetPay = 30_000m
        });
        await db.SaveChangesAsync();

        var payRun = await AddPayRunAsync(db, new DateTime(2026, 6, 26));
        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        Assert.Equal(100m, stub.PreTax401kDeduction);
    }

    [Fact]
    public async Task UnverifiedTaxYear_FailsLoudly_RatherThanGuessing()
    {
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Future", LastName = "Year", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2031, 1, 15));

        // Reusing a prior year's wage base would mis-withhold for every employee with no
        // visible symptom, so an unloaded year must throw.
        var ex = await Assert.ThrowsAsync<TaxRulesNotAvailableException>(
            () => payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput()));

        Assert.Equal(2031, ex.Year);
    }

    [Fact]
    public void FederalTaxRules_MatchPublishedFigures()
    {
        // Guards against a careless edit to the statutory table. Figures verified 2026-07-21
        // against IRS Topic 751, SSA CBB, and IRS Notice 2025-67 - see FederalTaxRules.cs.
        var provider = new StaticFederalTaxRuleProvider();

        Assert.Equal(168_600m, provider.GetFederalRules(2024).SocialSecurityWageBase);
        Assert.Equal(176_100m, provider.GetFederalRules(2025).SocialSecurityWageBase);
        Assert.Equal(184_500m, provider.GetFederalRules(2026).SocialSecurityWageBase);

        Assert.Equal(23_000m, provider.GetFederalRules(2024).ElectiveDeferralLimit);
        Assert.Equal(23_500m, provider.GetFederalRules(2025).ElectiveDeferralLimit);
        Assert.Equal(24_500m, provider.GetFederalRules(2026).ElectiveDeferralLimit);

        var y2026 = provider.GetFederalRules(2026);
        Assert.Equal(0.062m, y2026.SocialSecurityRate);
        Assert.Equal(0.0145m, y2026.MedicareRate);
        Assert.Equal(200_000m, y2026.AdditionalMedicareThreshold);
        Assert.Equal(0.009m, y2026.AdditionalMedicareRate);
        Assert.Equal(0.006m, y2026.FutaNetRate);   // 6.0% gross less the 5.4% state credit
    }

    // ═══════════════════════════════════════════════════════════════
    // PINNED DEFECTS - these document WRONG behavior
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Section125Premiums_ReduceFicaWages()
    {
        // Was DEFECT #1 - FICA was computed on gross, over-withholding for every employee
        // with health insurance. §125 cafeteria-plan premiums are exempt from FIT AND FICA.
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "See", LastName = "OneTwentyFive", IsActive = true,
            IsHourly = false, AnnualSalary = 78_000m, HealthInsurancePerPeriod = 200m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        // Income tax on 2,800: 2,800 * 26 = 72,800; less 8,600 = 64,200
        //   5,800 + (64,200 - 57,900) * 0.22 = 7,186 annual; / 26 = 276.38
        Assert.Equal(276.38m, stub.TaxFederal);
        Assert.Equal(138.60m, stub.TaxState);          // 2800 * 4.95%
        Assert.Equal(173.60m, stub.TaxSocialSecurity); // (3000 - 200) * 6.2%
        Assert.Equal(40.60m, stub.TaxMedicare);        // (3000 - 200) * 1.45%

        // The Social Security line must record the reduced wage basis, not gross - the YTD
        // wage-base tracker reads TaxableAmount back off these lines.
        var ssLine = Assert.Single(stub.TaxLines, t => t.Type == TaxType.SocialSecurity);
        Assert.Equal(2800m, ssLine.TaxableAmount);
    }

    [Fact]
    public async Task FicaWages_AreNeverNegative_WhenDeductionsExceedGross()
    {
        // A zero-hours period with pre-tax deductions must not produce negative FICA wages,
        // which would emit negative Social Security / Medicare withholding.
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Neg", LastName = "Ative", IsActive = true,
            IsHourly = true, HourlyRate = 25m, HealthInsurancePerPeriod = 100m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        Assert.Equal(0m, stub.TaxSocialSecurity);
        Assert.Equal(0m, stub.TaxMedicare);
    }

    [Fact]
    public async Task StandardBiweeklyHours_ProduceNoOvertime()
    {
        // Was DEFECT #2. An 80-hour biweekly period is two ordinary 40-hour workweeks and
        // owes NO overtime. The previous behavior split the period total at 40, booking 40
        // overtime hours and overpaying by 500.00 every period.
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Over", LastName = "Time", IsActive = true,
            IsHourly = true, HourlyRate = 25m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubFromPeriodHoursAsync(employee, payRun, 80m);

        Assert.Equal(2000m, stub.RegularEarnings);   // 80 * 25
        Assert.Equal(0m, stub.OvertimeEarnings);
        Assert.Equal(2000m, stub.GrossPay);
    }

    [Fact]
    public async Task PeriodHoursAboveTwoFullWorkweeks_ProduceOvertime()
    {
        // 90 hours over two workweeks is 45 per week, so 5 overtime hours per week = 10 total.
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Real", LastName = "Overtime", IsActive = true,
            IsHourly = true, HourlyRate = 25m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubFromPeriodHoursAsync(employee, payRun, 90m);

        Assert.Equal(2000m, stub.RegularEarnings);   // 80 * 25
        Assert.Equal(375m, stub.OvertimeEarnings);   // 10 * 37.50
        Assert.Equal(2375m, stub.GrossPay);
    }

    [Fact]
    public async Task AllAmounts_AreRoundedToWholeCents()
    {
        // Was DEFECT #7 (no rounding choke point) - fixed by Money.Round applied per line.
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Frac", LastName = "Tional", IsActive = true,
            IsHourly = true, HourlyRate = 25m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput
        {
            RegularHours = 80m,
            OvertimeHours = 10m
        });

        // 2375 * 1.45% = 34.4375, rounded half-up to the cent.
        Assert.Equal(34.44m, stub.TaxMedicare);

        // Federal: 2,375 * 26 = 61,750; less 8,600 = 53,150
        //   1,240 + (53,150 - 19,900) * 0.12 = 5,230 annual; / 26 = 201.15
        // Illinois: 2,375 * 4.95% = 117.5625 -> 117.56
        // Social Security: 2,375 * 6.2% = 147.25
        Assert.Equal(500.40m, stub.TotalTaxes);   // 201.15 + 117.56 + 147.25 + 34.44
        Assert.Equal(1874.60m, stub.NetPay);      // 2375 - 500.40

        foreach (var amount in new[]
        {
            stub.GrossPay, stub.NetPay, stub.TaxFederal, stub.TaxState,
            stub.TaxSocialSecurity, stub.TaxMedicare, stub.PreTax401kDeduction,
            stub.PostTaxDeductions, stub.YtdGross, stub.YtdNet, stub.YtdTaxes
        })
        {
            Assert.True(Money.IsWholeCents(amount), $"{amount} is not a whole number of cents");
        }
    }

    [Fact]
    public async Task StubTotals_TieOutToTheSumOfTheirLines()
    {
        // Verification item 7: a stub's printed totals must equal the lines beneath them.
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Tie", LastName = "Out", IsActive = true,
            IsHourly = true, HourlyRate = 33.33m,
            PreTax401kPercent = 7m, HealthInsurancePerPeriod = 137.77m,
            OtherDeductionsPerPeriod = 41.11m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 5, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput
        {
            RegularHours = 77.25m,
            OvertimeHours = 3.5m,
            BonusAmount = 333.33m,
            CommissionAmount = 111.11m
        });

        Assert.Equal(stub.GrossPay, stub.EarningLines.Sum(e => e.Amount));
        Assert.Equal(stub.TotalTaxes, stub.TaxLines.Sum(t => t.Amount));
        Assert.Equal(stub.PostTaxDeductions, stub.DeductionLines.Where(d => !d.IsPreTax).Sum(d => d.Amount));

        var preTaxTotal = stub.DeductionLines.Where(d => d.IsPreTax).Sum(d => d.Amount);
        Assert.Equal(stub.GrossPay - preTaxTotal - stub.TotalTaxes - stub.PostTaxDeductions, stub.NetPay);

        foreach (var line in stub.EarningLines)
            Assert.True(Money.IsWholeCents(line.Amount), $"earning line {line.Description} = {line.Amount}");
        foreach (var line in stub.TaxLines)
            Assert.True(Money.IsWholeCents(line.Amount), $"tax line {line.Description} = {line.Amount}");
        foreach (var line in stub.DeductionLines)
            Assert.True(Money.IsWholeCents(line.Amount), $"deduction line {line.Description} = {line.Amount}");
    }

    [Fact]
    [Trait("Pinned", "bug")]
    public async Task ZeroHoursWithDeductions_WithholdsNothing_ButStillNetsNegative()
    {
        // The negative-WITHHOLDING half of this defect is fixed: all four taxes now floor at
        // zero, so a zero-hours stub can no longer offset other employees' liability when
        // these rows are summed into a payroll tax report.
        //
        // STILL PINNED: net pay is negative. Deductions exceeding earnings is a real payroll
        // situation (unpaid leave), but it must not post silently - it needs to block the pay
        // run and require an explicit decision about arrears. Tracked for the pay-run
        // lifecycle/validation work (A5).
        var (db, payroll) = await CreateServicesAsync();
        using var _ = db;

        var employee = await AddEmployeeAsync(db, new Employee
        {
            FirstName = "Zee", LastName = "Roh", IsActive = true,
            IsHourly = true, HourlyRate = 25m,
            HealthInsurancePerPeriod = 100m, OtherDeductionsPerPeriod = 50m
        });
        var payRun = await AddPayRunAsync(db, new DateTime(2026, 1, 15));

        var stub = await payroll.GeneratePayStubAsync(employee, payRun, new PayStubInput());

        Assert.Equal(0m, stub.GrossPay);

        // Fixed: no negative withholding of any kind.
        Assert.Equal(0m, stub.TaxFederal);
        Assert.Equal(0m, stub.TaxState);
        Assert.Equal(0m, stub.TaxSocialSecurity);
        Assert.Equal(0m, stub.TaxMedicare);
        Assert.Equal(0m, stub.TotalTaxes);

        // Still wrong to post unguarded: -100 pre-tax health, -50 post-tax other.
        Assert.Equal(-150m, stub.NetPay);
    }
}
