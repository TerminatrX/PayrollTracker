// NOTE: This test file requires PayrollManager.UI project reference
// which is not compatible with net8.0 test projects.
// These tests are temporarily disabled until the UI project reference issue is resolved.

#if false
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using PayrollManager.UI.ViewModels;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Tests for PayRunWizardViewModel to ensure IsActive property is respected.
/// </summary>
public class PayRunWizardViewModelTests
{
    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private PayrollService CreatePayrollService(AppDbContext dbContext)
    {
        // Create default company settings
        var companySettings = new CompanySettings
        {
            CompanyName = "Test Company",
            CompanyAddress = "123 Test St",
            TaxId = "12-3456789",
            PayPeriodsPerYear = 26,
            DefaultHoursPerPeriod = 80,
            FederalTaxPercent = 0.15m,
            StateTaxPercent = 0.05m,
            SocialSecurityPercent = 0.062m,
            MedicarePercent = 0.0145m
        };

        dbContext.CompanySettings.Add(companySettings);
        dbContext.SaveChanges();

        return new PayrollService(dbContext);
    }

    [Fact]
    public async Task LoadEmployeesAsync_ExcludesInactiveEmployees_ByDefault()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var payrollService = CreatePayrollService(dbContext);

        // Create active and inactive employees
        var activeEmployee = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m,
            AnnualSalary = 0,
            PreTax401kPercent = 0.05m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 0m
        };

        var inactiveEmployee = new Employee
        {
            FirstName = "Jane",
            LastName = "Smith",
            IsActive = false,
            IsHourly = true,
            HourlyRate = 30.00m,
            AnnualSalary = 0,
            PreTax401kPercent = 0.05m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 0m
        };

        dbContext.Employees.Add(activeEmployee);
        dbContext.Employees.Add(inactiveEmployee);
        await dbContext.SaveChangesAsync();

        var viewModel = new PayRunWizardViewModel(dbContext, payrollService);

        // Act
        await viewModel.LoadEmployeesAsync();

        // Assert
        Assert.Single(viewModel.EmployeeRows);
        Assert.Equal(activeEmployee.Id, viewModel.EmployeeRows[0].EmployeeId);
        Assert.Equal("John Doe", viewModel.EmployeeRows[0].FullName);
        Assert.True(viewModel.EmployeeRows[0].IsActive);
    }

    [Fact]
    public async Task LoadEmployeesAsync_IncludesInactiveEmployees_WhenToggleEnabled()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var payrollService = CreatePayrollService(dbContext);

        // Create active and inactive employees
        var activeEmployee = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m,
            AnnualSalary = 0,
            PreTax401kPercent = 0.05m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 0m
        };

        var inactiveEmployee = new Employee
        {
            FirstName = "Jane",
            LastName = "Smith",
            IsActive = false,
            IsHourly = true,
            HourlyRate = 30.00m,
            AnnualSalary = 0,
            PreTax401kPercent = 0.05m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 0m
        };

        dbContext.Employees.Add(activeEmployee);
        dbContext.Employees.Add(inactiveEmployee);
        await dbContext.SaveChangesAsync();

        var viewModel = new PayRunWizardViewModel(dbContext, payrollService);

        // Act
        viewModel.IncludeInactive = true;
        await viewModel.LoadEmployeesAsync();

        // Assert
        Assert.Equal(2, viewModel.EmployeeRows.Count);
        Assert.Contains(viewModel.EmployeeRows, r => r.EmployeeId == activeEmployee.Id && r.IsActive);
        Assert.Contains(viewModel.EmployeeRows, r => r.EmployeeId == inactiveEmployee.Id && !r.IsActive);
    }

    [Fact]
    public async Task DeactivatedEmployee_DoesNotAppearInNewPayRun_EmployeeList()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var payrollService = CreatePayrollService(dbContext);

        // Create an active employee
        var employee = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m,
            AnnualSalary = 0,
            PreTax401kPercent = 0.05m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 0m
        };

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var viewModel = new PayRunWizardViewModel(dbContext, payrollService);

        // Verify employee appears initially
        await viewModel.LoadEmployeesAsync();
        Assert.Single(viewModel.EmployeeRows);
        Assert.Equal(employee.Id, viewModel.EmployeeRows[0].EmployeeId);

        // Act: Deactivate the employee
        employee.IsActive = false;
        await dbContext.SaveChangesAsync();

        // Reload employees (simulating a new PayRun)
        await viewModel.LoadEmployeesAsync();

        // Assert: Deactivated employee should not appear in the list
        Assert.Empty(viewModel.EmployeeRows);
    }

    [Fact]
    public async Task DeactivatedEmployee_AppearsInNewPayRun_WhenIncludeInactiveIsTrue()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var payrollService = CreatePayrollService(dbContext);

        // Create an active employee
        var employee = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m,
            AnnualSalary = 0,
            PreTax401kPercent = 0.05m,
            HealthInsurancePerPeriod = 100m,
            OtherDeductionsPerPeriod = 0m
        };

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var viewModel = new PayRunWizardViewModel(dbContext, payrollService);

        // Verify employee appears initially
        await viewModel.LoadEmployeesAsync();
        Assert.Single(viewModel.EmployeeRows);

        // Act: Deactivate the employee
        employee.IsActive = false;
        await dbContext.SaveChangesAsync();

        // Enable include inactive toggle
        viewModel.IncludeInactive = true;

        // Reload employees (simulating a new PayRun with historical corrections)
        await viewModel.LoadEmployeesAsync();

        // Assert: Deactivated employee should appear when IncludeInactive is true
        Assert.Single(viewModel.EmployeeRows);
        Assert.Equal(employee.Id, viewModel.EmployeeRows[0].EmployeeId);
        Assert.False(viewModel.EmployeeRows[0].IsActive);
    }
}
#endif
