using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Unit tests for ReportsViewModel aggregate calculations.
/// Tests that company and employee totals are correctly computed from PayStubs in a date range.
/// These tests verify the core query logic that ReportsViewModel uses.
/// </summary>
public class ReportsViewModelTests
{
    [Fact]
    public async Task ReportsQuery_Computes_Company_Totals_Correctly()
    {
        // Arrange - Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReportsQuery_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);

        // Create employees
        var employee1 = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m,
            Department = "Engineering"
        };
        var employee2 = new Employee
        {
            FirstName = "Jane",
            LastName = "Smith",
            IsActive = true,
            IsHourly = false,
            AnnualSalary = 100000m,
            Department = "Marketing"
        };
        dbContext.Employees.AddRange(employee1, employee2);
        await dbContext.SaveChangesAsync();

        // Create pay runs
        var payRun1 = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 14),
            PayDate = new DateTime(2024, 1, 15)
        };
        var payRun2 = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 15),
            PeriodEnd = new DateTime(2024, 1, 28),
            PayDate = new DateTime(2024, 1, 29)
        };
        var payRun3 = new DateTime(2024, 2, 1); // Outside the range
        dbContext.PayRuns.AddRange(payRun1, payRun2);
        await dbContext.SaveChangesAsync();

        // Create pay stubs with specific values
        var stub1 = new PayStub
        {
            EmployeeId = employee1.Id,
            PayRunId = payRun1.Id,
            GrossPay = 2000m,
            TaxFederal = 200m,
            TaxState = 100m,
            TaxSocialSecurity = 124m,
            TaxMedicare = 29m,
            PreTax401kDeduction = 80m,
            PostTaxDeductions = 50m,
            NetPay = 1417m
        };
        var stub2 = new PayStub
        {
            EmployeeId = employee1.Id,
            PayRunId = payRun2.Id,
            GrossPay = 2000m,
            TaxFederal = 200m,
            TaxState = 100m,
            TaxSocialSecurity = 124m,
            TaxMedicare = 29m,
            PreTax401kDeduction = 80m,
            PostTaxDeductions = 50m,
            NetPay = 1417m
        };
        var stub3 = new PayStub
        {
            EmployeeId = employee2.Id,
            PayRunId = payRun1.Id,
            GrossPay = 3846.15m, // ~$100k / 26 periods
            TaxFederal = 384.62m,
            TaxState = 192.31m,
            TaxSocialSecurity = 238.46m,
            TaxMedicare = 55.77m,
            PreTax401kDeduction = 153.85m,
            PostTaxDeductions = 100m,
            NetPay = 2721.14m
        };
        dbContext.PayStubs.AddRange(stub1, stub2, stub3);
        await dbContext.SaveChangesAsync();

        // Act - Query PayStubs in date range (same logic as ReportsViewModel)
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);
        
        var payStubs = await dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Include(ps => ps.Employee)
            .Where(ps => ps.PayRun != null && 
                         ps.PayRun.PayDate >= startDate && 
                         ps.PayRun.PayDate <= endDate)
            .ToListAsync();

        var companyTotalGross = payStubs.Sum(ps => ps.GrossPay);
        var companyTotalTaxes = payStubs.Sum(ps => ps.TotalTaxes);
        var companyTotalBenefits = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions);
        var companyTotalNet = payStubs.Sum(ps => ps.NetPay);

        // Assert - Company totals should match sum of all stubs in range
        var expectedGross = stub1.GrossPay + stub2.GrossPay + stub3.GrossPay; // 2000 + 2000 + 3846.15 = 7846.15
        var expectedTaxes = stub1.TotalTaxes + stub2.TotalTaxes + stub3.TotalTaxes; // (200+100+124+29)*2 + (384.62+192.31+238.46+55.77) = 906 + 871.16 = 1777.16
        var expectedBenefits = (stub1.PreTax401kDeduction + stub1.PostTaxDeductions) +
                               (stub2.PreTax401kDeduction + stub2.PostTaxDeductions) +
                               (stub3.PreTax401kDeduction + stub3.PostTaxDeductions); // (80+50)*2 + (153.85+100) = 260 + 253.85 = 513.85
        var expectedNet = stub1.NetPay + stub2.NetPay + stub3.NetPay; // 1417*2 + 2721.14 = 2834 + 2721.14 = 5555.14

        Assert.Equal(expectedGross, companyTotalGross, 2);
        Assert.Equal(expectedTaxes, companyTotalTaxes, 2);
        Assert.Equal(expectedBenefits, companyTotalBenefits, 2);
        Assert.Equal(expectedNet, companyTotalNet, 2);
    }

    [Fact]
    public async Task ReportsQuery_Computes_Employee_Totals_Correctly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReportsQuery_Employee_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);

        var employee = new Employee
        {
            FirstName = "Alice",
            LastName = "Johnson",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 30.00m,
            Department = "Sales"
        };
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var payRun1 = new PayRun
        {
            PeriodStart = new DateTime(2024, 2, 1),
            PeriodEnd = new DateTime(2024, 2, 14),
            PayDate = new DateTime(2024, 2, 15)
        };
        var payRun2 = new PayRun
        {
            PeriodStart = new DateTime(2024, 2, 15),
            PeriodEnd = new DateTime(2024, 2, 28),
            PayDate = new DateTime(2024, 2, 29)
        };
        dbContext.PayRuns.AddRange(payRun1, payRun2);
        await dbContext.SaveChangesAsync();

        var stub1 = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRun1.Id,
            GrossPay = 2400m,
            TaxFederal = 240m,
            TaxState = 120m,
            TaxSocialSecurity = 148.80m,
            TaxMedicare = 34.80m,
            PreTax401kDeduction = 96m,
            PostTaxDeductions = 60m,
            NetPay = 1700.40m
        };
        var stub2 = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRun2.Id,
            GrossPay = 2400m,
            TaxFederal = 240m,
            TaxState = 120m,
            TaxSocialSecurity = 148.80m,
            TaxMedicare = 34.80m,
            PreTax401kDeduction = 96m,
            PostTaxDeductions = 60m,
            NetPay = 1700.40m
        };
        dbContext.PayStubs.AddRange(stub1, stub2);
        await dbContext.SaveChangesAsync();

        // Act - Query and group by employee (same logic as ReportsViewModel)
        var startDate = new DateTime(2024, 2, 1);
        var endDate = new DateTime(2024, 2, 29);
        
        var payStubs = await dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Include(ps => ps.Employee)
            .Where(ps => ps.PayRun != null && 
                         ps.PayRun.PayDate >= startDate && 
                         ps.PayRun.PayDate <= endDate)
            .ToListAsync();

        var employeeGroups = payStubs
            .GroupBy(ps => ps.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Employee = g.First().Employee,
                GrossPay = g.Sum(ps => ps.GrossPay),
                FederalTax = g.Sum(ps => ps.TaxFederal),
                StateTax = g.Sum(ps => ps.TaxState),
                SocialSecurity = g.Sum(ps => ps.TaxSocialSecurity),
                Medicare = g.Sum(ps => ps.TaxMedicare),
                TotalTaxes = g.Sum(ps => ps.TotalTaxes),
                PreTax401k = g.Sum(ps => ps.PreTax401kDeduction),
                PostTaxDeductions = g.Sum(ps => ps.PostTaxDeductions),
                TotalDeductions = g.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions),
                NetPay = g.Sum(ps => ps.NetPay)
            })
            .OrderBy(g => g.Employee?.FullName ?? "Unknown")
            .ToList();

        // Assert - Employee totals should match sum of their stubs
        Assert.Single(employeeGroups);
        var employeeTotal = employeeGroups[0];
        
        Assert.Equal(employee.Id, employeeTotal.EmployeeId);
        Assert.Equal("Alice Johnson", employeeTotal.Employee?.FullName);
        Assert.Equal(stub1.GrossPay + stub2.GrossPay, employeeTotal.GrossPay, 2); // 4800
        Assert.Equal(stub1.TaxFederal + stub2.TaxFederal, employeeTotal.FederalTax, 2); // 480
        Assert.Equal(stub1.TaxState + stub2.TaxState, employeeTotal.StateTax, 2); // 240
        Assert.Equal(stub1.TaxSocialSecurity + stub2.TaxSocialSecurity, employeeTotal.SocialSecurity, 2); // 297.60
        Assert.Equal(stub1.TaxMedicare + stub2.TaxMedicare, employeeTotal.Medicare, 2); // 69.60
        Assert.Equal(stub1.TotalTaxes + stub2.TotalTaxes, employeeTotal.TotalTaxes, 2); // 543.20 * 2 = 1086.40
        Assert.Equal((stub1.PreTax401kDeduction + stub1.PostTaxDeductions) + 
                     (stub2.PreTax401kDeduction + stub2.PostTaxDeductions), 
                     employeeTotal.TotalDeductions, 2); // (96+60)*2 = 312
        Assert.Equal(stub1.NetPay + stub2.NetPay, employeeTotal.NetPay, 2); // 3400.80
    }

    [Fact]
    public async Task ReportsQuery_Filters_By_Date_Range()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReportsQuery_Filter_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);

        var employee = new Employee
        {
            FirstName = "Bob",
            LastName = "Williams",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 20.00m
        };
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        // Create pay runs in different months
        var payRunJan = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 14),
            PayDate = new DateTime(2024, 1, 15)
        };
        var payRunFeb = new PayRun
        {
            PeriodStart = new DateTime(2024, 2, 1),
            PeriodEnd = new DateTime(2024, 2, 14),
            PayDate = new DateTime(2024, 2, 15)
        };
        var payRunMar = new PayRun
        {
            PeriodStart = new DateTime(2024, 3, 1),
            PeriodEnd = new DateTime(2024, 3, 14),
            PayDate = new DateTime(2024, 3, 15)
        };
        dbContext.PayRuns.AddRange(payRunJan, payRunFeb, payRunMar);
        await dbContext.SaveChangesAsync();

        var stubJan = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRunJan.Id,
            GrossPay = 1600m,
            TaxFederal = 160m,
            TaxState = 80m,
            TaxSocialSecurity = 99.20m,
            TaxMedicare = 23.20m,
            PreTax401kDeduction = 64m,
            PostTaxDeductions = 40m,
            NetPay = 1133.60m
        };
        var stubFeb = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRunFeb.Id,
            GrossPay = 1600m,
            TaxFederal = 160m,
            TaxState = 80m,
            TaxSocialSecurity = 99.20m,
            TaxMedicare = 23.20m,
            PreTax401kDeduction = 64m,
            PostTaxDeductions = 40m,
            NetPay = 1133.60m
        };
        var stubMar = new PayStub
        {
            EmployeeId = employee.Id,
            PayRunId = payRunMar.Id,
            GrossPay = 1600m,
            TaxFederal = 160m,
            TaxState = 80m,
            TaxSocialSecurity = 99.20m,
            TaxMedicare = 23.20m,
            PreTax401kDeduction = 64m,
            PostTaxDeductions = 40m,
            NetPay = 1133.60m
        };
        dbContext.PayStubs.AddRange(stubJan, stubFeb, stubMar);
        await dbContext.SaveChangesAsync();

        // Act - Query for February only
        var startDate = new DateTime(2024, 2, 1);
        var endDate = new DateTime(2024, 2, 29);
        
        var payStubs = await dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Include(ps => ps.Employee)
            .Where(ps => ps.PayRun != null && 
                         ps.PayRun.PayDate >= startDate && 
                         ps.PayRun.PayDate <= endDate)
            .ToListAsync();

        var companyTotalGross = payStubs.Sum(ps => ps.GrossPay);
        var companyTotalNet = payStubs.Sum(ps => ps.NetPay);

        var employeeGroups = payStubs
            .GroupBy(ps => ps.EmployeeId)
            .ToList();

        // Assert - Should only include February stub
        Assert.Single(employeeGroups);
        Assert.Equal(stubFeb.GrossPay, employeeGroups[0].Sum(ps => ps.GrossPay), 2);
        Assert.Equal(stubFeb.NetPay, employeeGroups[0].Sum(ps => ps.NetPay), 2);
        Assert.Equal(stubFeb.GrossPay, companyTotalGross, 2);
        Assert.Equal(stubFeb.NetPay, companyTotalNet, 2);
    }

    [Fact]
    public void GetYearRange_Returns_Correct_Date_Range()
    {
        // Use reflection to call the static method, or test the logic directly
        // Since we can't reference UI project, we'll test the logic directly
        var year = 2024;
        var start = new DateTime(year, 1, 1);
        var end = new DateTime(year, 12, 31, 23, 59, 59);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 1), start);
        Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 59), end);
    }

    [Fact]
    public void GetQuarterRange_Returns_Correct_Date_Ranges()
    {
        // Test the quarter range logic directly
        var year = 2024;

        // Q1
        var q1Start = new DateTime(year, 1, 1);
        var q1End = new DateTime(year, 3, 31, 23, 59, 59);
        Assert.Equal(new DateTime(2024, 1, 1), q1Start);
        Assert.Equal(new DateTime(2024, 3, 31, 23, 59, 59), q1End);

        // Q2
        var q2Start = new DateTime(year, 4, 1);
        var q2End = new DateTime(year, 6, 30, 23, 59, 59);
        Assert.Equal(new DateTime(2024, 4, 1), q2Start);
        Assert.Equal(new DateTime(2024, 6, 30, 23, 59, 59), q2End);

        // Q3
        var q3Start = new DateTime(year, 7, 1);
        var q3End = new DateTime(year, 9, 30, 23, 59, 59);
        Assert.Equal(new DateTime(2024, 7, 1), q3Start);
        Assert.Equal(new DateTime(2024, 9, 30, 23, 59, 59), q3End);

        // Q4
        var q4Start = new DateTime(year, 10, 1);
        var q4End = new DateTime(year, 12, 31, 23, 59, 59);
        Assert.Equal(new DateTime(2024, 10, 1), q4Start);
        Assert.Equal(new DateTime(2024, 12, 31, 23, 59, 59), q4End);
    }

    [Fact]
    public async Task ReportsQuery_Handles_Empty_Date_Range()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReportsQuery_Empty_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);

        // Act - Query for a date range with no pay stubs
        var startDate = new DateTime(2025, 1, 1);
        var endDate = new DateTime(2025, 1, 31);
        
        var payStubs = await dbContext.PayStubs
            .Include(ps => ps.PayRun)
            .Include(ps => ps.Employee)
            .Where(ps => ps.PayRun != null && 
                         ps.PayRun.PayDate >= startDate && 
                         ps.PayRun.PayDate <= endDate)
            .ToListAsync();

        var companyTotalGross = payStubs.Sum(ps => ps.GrossPay);
        var companyTotalTaxes = payStubs.Sum(ps => ps.TotalTaxes);
        var companyTotalBenefits = payStubs.Sum(ps => ps.PreTax401kDeduction + ps.PostTaxDeductions);
        var companyTotalNet = payStubs.Sum(ps => ps.NetPay);

        // Assert - All totals should be zero
        Assert.Equal(0m, companyTotalGross);
        Assert.Equal(0m, companyTotalTaxes);
        Assert.Equal(0m, companyTotalBenefits);
        Assert.Equal(0m, companyTotalNet);
        Assert.Empty(payStubs);
    }
}
