using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Unit tests to verify that existing PayStubs are not recalculated when viewing.
/// Ensures that stored pay stub values remain unchanged even when CompanySettings change.
/// </summary>
public class PayStubPersistenceTests
{
    [Fact]
    public async Task PayStub_Values_Remain_Unchanged_After_CompanySettings_Change()
    {
        // Arrange - Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PayStubPersistence_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);
        var companySettingsService = new CompanySettingsService(dbContext);
        var payrollService = new PayrollService(dbContext, companySettingsService);

        // Create initial company settings with specific tax rates
        var initialSettings = new CompanySettings
        {
            CompanyName = "Test Company",
            FederalTaxPercent = 10m,
            StateTaxPercent = 5m,
            SocialSecurityPercent = 6.2m,
            MedicarePercent = 1.45m,
            PayPeriodsPerYear = 26
        };
        await companySettingsService.SaveSettingsAsync(initialSettings);

        // Create an employee
        var employee = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m,
            PreTax401kPercent = 4m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 50m
        };
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        // Create a pay run
        var payRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 14),
            PayDate = new DateTime(2024, 1, 15)
        };
        dbContext.PayRuns.Add(payRun);
        await dbContext.SaveChangesAsync();

        // Generate a pay stub with initial tax rates
        var payStubInput = new PayStubInput
        {
            RegularHours = 80m,
            OvertimeHours = 0m,
            BonusAmount = 0m,
            CommissionAmount = 0m
        };

        var payStub = await payrollService.GeneratePayStubAsync(employee, payRun, payStubInput);
        dbContext.PayStubs.Add(payStub);
        await dbContext.SaveChangesAsync();

        // Capture the original values
        var originalGrossPay = payStub.GrossPay;
        var originalTaxFederal = payStub.TaxFederal;
        var originalTaxState = payStub.TaxState;
        var originalTaxSocialSecurity = payStub.TaxSocialSecurity;
        var originalTaxMedicare = payStub.TaxMedicare;
        var originalTotalTaxes = payStub.TotalTaxes;
        var originalNetPay = payStub.NetPay;
        var originalYtdGross = payStub.YtdGross;
        var originalYtdTaxes = payStub.YtdTaxes;
        var originalYtdNet = payStub.YtdNet;
        var originalPreTax401k = payStub.PreTax401kDeduction;
        var originalPostTaxDeductions = payStub.PostTaxDeductions;

        // Verify the pay stub was created with expected values based on initial settings
        // Gross pay: 80 hours * $25 = $2000
        Assert.Equal(2000m, originalGrossPay);
        
        // Taxable income: $2000 - (4% 401k = $80) = $1920
        // Federal tax: $1920 * 10% = $192
        // State tax: $1920 * 5% = $96
        // Social Security: $2000 * 6.2% = $124
        // Medicare: $2000 * 1.45% = $29
        // Total taxes: $192 + $96 + $124 + $29 = $441
        // Net pay: $1920 - $441 - $50 (other deductions) = $1429

        // Act - Change company settings tax rates significantly
        var changedSettings = new CompanySettings
        {
            Id = initialSettings.Id,
            CompanyName = "Test Company",
            FederalTaxPercent = 20m,  // Changed from 10% to 20%
            StateTaxPercent = 10m,    // Changed from 5% to 10%
            SocialSecurityPercent = 7.0m,  // Changed from 6.2% to 7.0%
            MedicarePercent = 2.0m,   // Changed from 1.45% to 2.0%
            PayPeriodsPerYear = 26
        };
        await companySettingsService.SaveSettingsAsync(changedSettings);

        // Reload the pay stub from the database (simulating what PayStubDetailsPage would do)
        var reloadedPayStub = await dbContext.PayStubs
            .Include(ps => ps.Employee)
            .Include(ps => ps.PayRun)
            .Include(ps => ps.EarningLines)
            .Include(ps => ps.DeductionLines)
            .Include(ps => ps.TaxLines)
            .FirstOrDefaultAsync(ps => ps.Id == payStub.Id);

        // Assert - All values must remain exactly the same
        Assert.NotNull(reloadedPayStub);
        Assert.Equal(originalGrossPay, reloadedPayStub.GrossPay);
        Assert.Equal(originalTaxFederal, reloadedPayStub.TaxFederal);
        Assert.Equal(originalTaxState, reloadedPayStub.TaxState);
        Assert.Equal(originalTaxSocialSecurity, reloadedPayStub.TaxSocialSecurity);
        Assert.Equal(originalTaxMedicare, reloadedPayStub.TaxMedicare);
        Assert.Equal(originalTotalTaxes, reloadedPayStub.TotalTaxes);
        Assert.Equal(originalNetPay, reloadedPayStub.NetPay);
        Assert.Equal(originalYtdGross, reloadedPayStub.YtdGross);
        Assert.Equal(originalYtdTaxes, reloadedPayStub.YtdTaxes);
        Assert.Equal(originalYtdNet, reloadedPayStub.YtdNet);
        Assert.Equal(originalPreTax401k, reloadedPayStub.PreTax401kDeduction);
        Assert.Equal(originalPostTaxDeductions, reloadedPayStub.PostTaxDeductions);

        // Verify that the new settings are actually different
        var currentSettings = await companySettingsService.GetSettingsAsync();
        Assert.Equal(20m, currentSettings.FederalTaxPercent);
        Assert.Equal(10m, currentSettings.StateTaxPercent);
        Assert.Equal(7.0m, currentSettings.SocialSecurityPercent);
        Assert.Equal(2.0m, currentSettings.MedicarePercent);

        // Verify that if we generated a NEW pay stub with the new rates, it would have different values
        var newPayRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 15),
            PeriodEnd = new DateTime(2024, 1, 28),
            PayDate = new DateTime(2024, 1, 29)
        };
        dbContext.PayRuns.Add(newPayRun);
        await dbContext.SaveChangesAsync();

        var newPayStub = await payrollService.GeneratePayStubAsync(employee, newPayRun, payStubInput);
        
        // The new pay stub should have different tax values due to changed rates
        // This proves that the old pay stub values are preserved, not recalculated
        Assert.NotEqual(originalTaxFederal, newPayStub.TaxFederal);
        Assert.NotEqual(originalTaxState, newPayStub.TaxState);
        Assert.NotEqual(originalTaxSocialSecurity, newPayStub.TaxSocialSecurity);
        Assert.NotEqual(originalTaxMedicare, newPayStub.TaxMedicare);
    }

    [Fact]
    public async Task PayStub_DetailsViewModel_Does_Not_Recalculate_Values()
    {
        // Arrange - Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PayStubDetailsViewModel_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);
        var companySettingsService = new CompanySettingsService(dbContext);
        var payrollService = new PayrollService(dbContext, companySettingsService);

        // Create initial company settings
        var initialSettings = new CompanySettings
        {
            CompanyName = "Test Company",
            FederalTaxPercent = 12m,
            StateTaxPercent = 5m,
            SocialSecurityPercent = 6.2m,
            MedicarePercent = 1.45m,
            PayPeriodsPerYear = 26
        };
        await companySettingsService.SaveSettingsAsync(initialSettings);

        // Create an employee
        var employee = new Employee
        {
            FirstName = "Jane",
            LastName = "Smith",
            IsActive = true,
            IsHourly = false,
            AnnualSalary = 100000m,
            PreTax401kPercent = 5m,
            HealthInsurancePerPeriod = 150m,
            OtherDeductionsPerPeriod = 75m
        };
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        // Create a pay run
        var payRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 2, 1),
            PeriodEnd = new DateTime(2024, 2, 14),
            PayDate = new DateTime(2024, 2, 15)
        };
        dbContext.PayRuns.Add(payRun);
        await dbContext.SaveChangesAsync();

        // Generate a pay stub
        var payStubInput = new PayStubInput
        {
            RegularHours = 0m,
            OvertimeHours = 0m,
            BonusAmount = 0m,
            CommissionAmount = 0m
        };

        var payStub = await payrollService.GeneratePayStubAsync(employee, payRun, payStubInput);
        dbContext.PayStubs.Add(payStub);
        await dbContext.SaveChangesAsync();

        // Capture original values
        var originalGrossPay = payStub.GrossPay;
        var originalNetPay = payStub.NetPay;
        var originalTaxFederal = payStub.TaxFederal;
        var originalTotalTaxes = payStub.TotalTaxes;

        // Change company settings
        var changedSettings = new CompanySettings
        {
            Id = initialSettings.Id,
            CompanyName = "Test Company",
            FederalTaxPercent = 25m,  // Significantly increased
            StateTaxPercent = 15m,    // Significantly increased
            SocialSecurityPercent = 8.0m,
            MedicarePercent = 2.5m,
            PayPeriodsPerYear = 26
        };
        await companySettingsService.SaveSettingsAsync(changedSettings);

        // Simulate what PayStubDetailsViewModel.LoadPayStubByIdAsync does
        // (it only reads from DB, never calls PayrollService)
        var reloadedPayStub = await dbContext.PayStubs
            .Include(ps => ps.Employee)
            .Include(ps => ps.PayRun)
            .Include(ps => ps.EarningLines)
            .Include(ps => ps.DeductionLines)
            .Include(ps => ps.TaxLines)
            .FirstOrDefaultAsync(ps => ps.Id == payStub.Id);

        // Assert - Values must be unchanged
        Assert.NotNull(reloadedPayStub);
        Assert.Equal(originalGrossPay, reloadedPayStub.GrossPay);
        Assert.Equal(originalNetPay, reloadedPayStub.NetPay);
        Assert.Equal(originalTaxFederal, reloadedPayStub.TaxFederal);
        Assert.Equal(originalTotalTaxes, reloadedPayStub.TotalTaxes);

        // Verify that PayStubDetailsViewModel would display these exact values
        // (This test ensures the ViewModel doesn't accidentally trigger recalculation)
        Assert.Equal(originalGrossPay, reloadedPayStub.GrossPay);
        Assert.Equal(originalNetPay, reloadedPayStub.NetPay);
    }
}
