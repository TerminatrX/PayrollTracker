using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System.Globalization;
using System.Text;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Unit tests for ExportService CSV export functionality.
/// Tests that CSV exports include headers and totals row, and can be parsed back correctly.
/// </summary>
public class ExportServiceCsvTests
{
    [Fact]
    public async Task ExportReportToCsvAsync_Includes_Header_Row_And_Totals_Row()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ExportCsv_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);
        var companySettingsService = new CompanySettingsService(dbContext);
        var exportService = new ExportService(dbContext, companySettingsService);

        // Create test data
        var employee1 = new Employee
        {
            FirstName = "John",
            LastName = "Doe",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 25.00m
        };
        var employee2 = new Employee
        {
            FirstName = "Jane",
            LastName = "Smith",
            IsActive = true,
            IsHourly = false,
            AnnualSalary = 100000m
        };
        dbContext.Employees.AddRange(employee1, employee2);
        await dbContext.SaveChangesAsync();

        var payRun = new PayRun
        {
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 14),
            PayDate = new DateTime(2024, 1, 15)
        };
        dbContext.PayRuns.Add(payRun);
        await dbContext.SaveChangesAsync();

        var stub1 = new PayStub
        {
            EmployeeId = employee1.Id,
            PayRunId = payRun.Id,
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
            EmployeeId = employee2.Id,
            PayRunId = payRun.Id,
            GrossPay = 3846.15m,
            TaxFederal = 384.62m,
            TaxState = 192.31m,
            TaxSocialSecurity = 238.46m,
            TaxMedicare = 55.77m,
            PreTax401kDeduction = 153.85m,
            PostTaxDeductions = 100m,
            NetPay = 2721.14m
        };
        dbContext.PayStubs.AddRange(stub1, stub2);
        await dbContext.SaveChangesAsync();

        // Create employee and company totals
        var employeeTotals = new List<EmployeeTotals>
        {
            new EmployeeTotals
            {
                EmployeeId = employee1.Id,
                EmployeeName = "John Doe",
                GrossPay = 2000m,
                FederalTax = 200m,
                StateTax = 100m,
                SocialSecurity = 124m,
                Medicare = 29m,
                TotalTaxes = 453m,
                PreTax401k = 80m,
                PostTaxDeductions = 50m,
                TotalDeductions = 130m,
                NetPay = 1417m,
                PayStubCount = 1
            },
            new EmployeeTotals
            {
                EmployeeId = employee2.Id,
                EmployeeName = "Jane Smith",
                GrossPay = 3846.15m,
                FederalTax = 384.62m,
                StateTax = 192.31m,
                SocialSecurity = 238.46m,
                Medicare = 55.77m,
                TotalTaxes = 871.16m,
                PreTax401k = 153.85m,
                PostTaxDeductions = 100m,
                TotalDeductions = 253.85m,
                NetPay = 2721.14m,
                PayStubCount = 1
            }
        };

        var companyTotals = new CompanyTotals
        {
            Year = 2024,
            EmployeeCount = 2,
            GrossPay = 5846.15m,
            FederalTax = 584.62m,
            StateTax = 292.31m,
            SocialSecurity = 362.46m,
            Medicare = 84.77m,
            TotalTaxes = 1324.16m,
            PreTax401k = 233.85m,
            PostTaxDeductions = 150m,
            TotalDeductions = 383.85m,
            NetPay = 4138.14m,
            PayStubCount = 2
        };

        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 31);
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_report_{Guid.NewGuid()}.csv");

        try
        {
            // Act - Export to CSV
            var filePath = await exportService.ExportReportToCsvAsync(
                employeeTotals,
                companyTotals,
                startDate,
                endDate,
                tempPath);

            // Assert - File exists
            Assert.True(File.Exists(filePath));

            // Read the CSV content
            var csvContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // Find the EMPLOYEE TOTALS section
            var employeeTotalsIndex = lines.FindIndex(l => l.Contains("EMPLOYEE TOTALS"));
            Assert.True(employeeTotalsIndex >= 0, "EMPLOYEE TOTALS section should exist");

            // Verify header row exists
            var headerRow = lines[employeeTotalsIndex + 1];
            Assert.Contains("Employee ID", headerRow);
            Assert.Contains("Employee Name", headerRow);
            Assert.Contains("Gross Pay", headerRow);
            Assert.Contains("Federal Tax", headerRow);
            Assert.Contains("State Tax", headerRow);
            Assert.Contains("Social Security", headerRow);
            Assert.Contains("Medicare", headerRow);
            Assert.Contains("Total Taxes", headerRow);
            Assert.Contains("Pre-Tax 401k", headerRow);
            Assert.Contains("Post-Tax Deductions", headerRow);
            Assert.Contains("Total Deductions", headerRow);
            Assert.Contains("Net Pay", headerRow);
            Assert.Contains("Pay Stubs", headerRow);

            // Verify employee rows exist
            var employee1Row = lines.FirstOrDefault(l => l.Contains("John Doe"));
            var employee2Row = lines.FirstOrDefault(l => l.Contains("Jane Smith"));
            Assert.NotNull(employee1Row);
            Assert.NotNull(employee2Row);

            // Verify Totals row exists at the end
            var totalsRow = lines.LastOrDefault(l => l.StartsWith("Totals,"));
            Assert.NotNull(totalsRow);
            Assert.Contains("COMPANY TOTALS", totalsRow);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task ExportReportToCsvAsync_Totals_Row_Matches_Company_Totals()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ExportCsv_Totals_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);
        var companySettingsService = new CompanySettingsService(dbContext);
        var exportService = new ExportService(dbContext, companySettingsService);

        var employeeTotals = new List<EmployeeTotals>
        {
            new EmployeeTotals
            {
                EmployeeId = 1,
                EmployeeName = "Test Employee",
                GrossPay = 5000m,
                FederalTax = 500m,
                StateTax = 250m,
                SocialSecurity = 310m,
                Medicare = 72.50m,
                TotalTaxes = 1132.50m,
                PreTax401k = 200m,
                PostTaxDeductions = 100m,
                TotalDeductions = 300m,
                NetPay = 3567.50m,
                PayStubCount = 2
            }
        };

        var companyTotals = new CompanyTotals
        {
            Year = 2024,
            EmployeeCount = 1,
            GrossPay = 5000m,
            FederalTax = 500m,
            StateTax = 250m,
            SocialSecurity = 310m,
            Medicare = 72.50m,
            TotalTaxes = 1132.50m,
            PreTax401k = 200m,
            PostTaxDeductions = 100m,
            TotalDeductions = 300m,
            NetPay = 3567.50m,
            PayStubCount = 2
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_totals_{Guid.NewGuid()}.csv");

        try
        {
            // Act
            var filePath = await exportService.ExportReportToCsvAsync(
                employeeTotals,
                companyTotals,
                new DateTime(2024, 1, 1),
                new DateTime(2024, 1, 31),
                tempPath);

            // Read and parse CSV
            var csvContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // Find the Totals row
            var totalsRow = lines.LastOrDefault(l => l.StartsWith("Totals,"));
            Assert.NotNull(totalsRow);

            // Parse the totals row
            var columns = ParseCsvLine(totalsRow);

            // Assert - Verify totals row values match company totals
            Assert.Equal("Totals", columns[0]);
            Assert.Equal("COMPANY TOTALS", columns[1]);
            Assert.Equal(companyTotals.GrossPay, decimal.Parse(columns[2], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.FederalTax, decimal.Parse(columns[3], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.StateTax, decimal.Parse(columns[4], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.SocialSecurity, decimal.Parse(columns[5], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.Medicare, decimal.Parse(columns[6], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.TotalTaxes, decimal.Parse(columns[7], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.PreTax401k, decimal.Parse(columns[8], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.PostTaxDeductions, decimal.Parse(columns[9], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.TotalDeductions, decimal.Parse(columns[10], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.NetPay, decimal.Parse(columns[11], CultureInfo.InvariantCulture), 2);
            Assert.Equal(companyTotals.PayStubCount, int.Parse(columns[12]));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task ExportReportToCsvAsync_Can_Be_Parsed_And_Produces_Same_Totals()
    {
        // Arrange - Create in-memory database with test data
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ExportCsv_Parse_{Guid.NewGuid()}")
            .Options;

        using var dbContext = new AppDbContext(options);
        var companySettingsService = new CompanySettingsService(dbContext);
        var exportService = new ExportService(dbContext, companySettingsService);

        // Create employees
        var employee1 = new Employee
        {
            FirstName = "Alice",
            LastName = "Johnson",
            IsActive = true,
            IsHourly = true,
            HourlyRate = 30.00m
        };
        var employee2 = new Employee
        {
            FirstName = "Bob",
            LastName = "Williams",
            IsActive = true,
            IsHourly = false,
            AnnualSalary = 120000m
        };
        dbContext.Employees.AddRange(employee1, employee2);
        await dbContext.SaveChangesAsync();

        // Create pay runs
        var payRun1 = new PayRun
        {
            PeriodStart = new DateTime(2024, 3, 1),
            PeriodEnd = new DateTime(2024, 3, 14),
            PayDate = new DateTime(2024, 3, 15)
        };
        var payRun2 = new PayRun
        {
            PeriodStart = new DateTime(2024, 3, 15),
            PeriodEnd = new DateTime(2024, 3, 28),
            PayDate = new DateTime(2024, 3, 29)
        };
        dbContext.PayRuns.AddRange(payRun1, payRun2);
        await dbContext.SaveChangesAsync();

        // Create pay stubs with specific values
        var stub1 = new PayStub
        {
            EmployeeId = employee1.Id,
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
            EmployeeId = employee1.Id,
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
        var stub3 = new PayStub
        {
            EmployeeId = employee2.Id,
            PayRunId = payRun1.Id,
            GrossPay = 4615.38m, // ~$120k / 26 periods
            TaxFederal = 461.54m,
            TaxState = 230.77m,
            TaxSocialSecurity = 286.15m,
            TaxMedicare = 66.92m,
            PreTax401kDeduction = 184.62m,
            PostTaxDeductions = 150m,
            NetPay = 3265.38m
        };
        dbContext.PayStubs.AddRange(stub1, stub2, stub3);
        await dbContext.SaveChangesAsync();

        // Create employee and company totals (matching what ReportsViewModel would compute)
        var employeeTotals = new List<EmployeeTotals>
        {
            new EmployeeTotals
            {
                EmployeeId = employee1.Id,
                EmployeeName = "Alice Johnson",
                GrossPay = stub1.GrossPay + stub2.GrossPay, // 4800
                FederalTax = stub1.TaxFederal + stub2.TaxFederal, // 480
                StateTax = stub1.TaxState + stub2.TaxState, // 240
                SocialSecurity = stub1.TaxSocialSecurity + stub2.TaxSocialSecurity, // 297.60
                Medicare = stub1.TaxMedicare + stub2.TaxMedicare, // 69.60
                TotalTaxes = stub1.TotalTaxes + stub2.TotalTaxes, // 543.20 * 2 = 1086.40
                PreTax401k = stub1.PreTax401kDeduction + stub2.PreTax401kDeduction, // 192
                PostTaxDeductions = stub1.PostTaxDeductions + stub2.PostTaxDeductions, // 120
                TotalDeductions = (stub1.PreTax401kDeduction + stub1.PostTaxDeductions) + 
                                 (stub2.PreTax401kDeduction + stub2.PostTaxDeductions), // 312
                NetPay = stub1.NetPay + stub2.NetPay, // 3400.80
                PayStubCount = 2
            },
            new EmployeeTotals
            {
                EmployeeId = employee2.Id,
                EmployeeName = "Bob Williams",
                GrossPay = stub3.GrossPay, // 4615.38
                FederalTax = stub3.TaxFederal, // 461.54
                StateTax = stub3.TaxState, // 230.77
                SocialSecurity = stub3.TaxSocialSecurity, // 286.15
                Medicare = stub3.TaxMedicare, // 66.92
                TotalTaxes = stub3.TotalTaxes, // 1045.38
                PreTax401k = stub3.PreTax401kDeduction, // 184.62
                PostTaxDeductions = stub3.PostTaxDeductions, // 150
                TotalDeductions = stub3.PreTax401kDeduction + stub3.PostTaxDeductions, // 334.62
                NetPay = stub3.NetPay, // 3265.38
                PayStubCount = 1
            }
        };

        var companyTotals = new CompanyTotals
        {
            Year = 2024,
            EmployeeCount = 2,
            GrossPay = stub1.GrossPay + stub2.GrossPay + stub3.GrossPay, // 9415.38
            FederalTax = stub1.TaxFederal + stub2.TaxFederal + stub3.TaxFederal, // 941.54
            StateTax = stub1.TaxState + stub2.TaxState + stub3.TaxState, // 470.77
            SocialSecurity = stub1.TaxSocialSecurity + stub2.TaxSocialSecurity + stub3.TaxSocialSecurity, // 583.75
            Medicare = stub1.TaxMedicare + stub2.TaxMedicare + stub3.TaxMedicare, // 136.52
            TotalTaxes = stub1.TotalTaxes + stub2.TotalTaxes + stub3.TotalTaxes, // 2131.78
            PreTax401k = stub1.PreTax401kDeduction + stub2.PreTax401kDeduction + stub3.PreTax401kDeduction, // 376.62
            PostTaxDeductions = stub1.PostTaxDeductions + stub2.PostTaxDeductions + stub3.PostTaxDeductions, // 270
            TotalDeductions = (stub1.PreTax401kDeduction + stub1.PostTaxDeductions) +
                              (stub2.PreTax401kDeduction + stub2.PostTaxDeductions) +
                              (stub3.PreTax401kDeduction + stub3.PostTaxDeductions), // 646.62
            NetPay = stub1.NetPay + stub2.NetPay + stub3.NetPay, // 6666.16
            PayStubCount = 3
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_parse_{Guid.NewGuid()}.csv");

        try
        {
            // Act - Export to CSV
            var filePath = await exportService.ExportReportToCsvAsync(
                employeeTotals,
                companyTotals,
                new DateTime(2024, 3, 1),
                new DateTime(2024, 3, 31),
                tempPath);

            // Read and parse CSV
            var csvContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // Find EMPLOYEE TOTALS section
            var employeeTotalsIndex = lines.FindIndex(l => l.Contains("EMPLOYEE TOTALS"));
            Assert.True(employeeTotalsIndex >= 0);

            // Parse employee rows (skip header)
            var employeeRows = new List<Dictionary<string, string>>();
            for (int i = employeeTotalsIndex + 2; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.StartsWith("Totals,"))
                    break;

                var columns = ParseCsvLine(line);
                if (columns.Count >= 13) // Ensure we have all columns
                {
                    employeeRows.Add(new Dictionary<string, string>
                    {
                        ["EmployeeId"] = columns[0],
                        ["EmployeeName"] = columns[1],
                        ["GrossPay"] = columns[2],
                        ["FederalTax"] = columns[3],
                        ["StateTax"] = columns[4],
                        ["SocialSecurity"] = columns[5],
                        ["Medicare"] = columns[6],
                        ["TotalTaxes"] = columns[7],
                        ["PreTax401k"] = columns[8],
                        ["PostTaxDeductions"] = columns[9],
                        ["TotalDeductions"] = columns[10],
                        ["NetPay"] = columns[11],
                        ["PayStubs"] = columns[12]
                    });
                }
            }

            // Parse Totals row
            var totalsRow = lines.LastOrDefault(l => l.StartsWith("Totals,"));
            Assert.NotNull(totalsRow);
            var totalsColumns = ParseCsvLine(totalsRow);

            // Assert - Verify parsed totals match original company totals
            var parsedGross = decimal.Parse(totalsColumns[2], CultureInfo.InvariantCulture);
            var parsedFederal = decimal.Parse(totalsColumns[3], CultureInfo.InvariantCulture);
            var parsedState = decimal.Parse(totalsColumns[4], CultureInfo.InvariantCulture);
            var parsedSS = decimal.Parse(totalsColumns[5], CultureInfo.InvariantCulture);
            var parsedMedicare = decimal.Parse(totalsColumns[6], CultureInfo.InvariantCulture);
            var parsedTotalTaxes = decimal.Parse(totalsColumns[7], CultureInfo.InvariantCulture);
            var parsedPreTax401k = decimal.Parse(totalsColumns[8], CultureInfo.InvariantCulture);
            var parsedPostTax = decimal.Parse(totalsColumns[9], CultureInfo.InvariantCulture);
            var parsedTotalDeductions = decimal.Parse(totalsColumns[10], CultureInfo.InvariantCulture);
            var parsedNet = decimal.Parse(totalsColumns[11], CultureInfo.InvariantCulture);
            var parsedPayStubCount = int.Parse(totalsColumns[12]);

            Assert.Equal(companyTotals.GrossPay, parsedGross, 2);
            Assert.Equal(companyTotals.FederalTax, parsedFederal, 2);
            Assert.Equal(companyTotals.StateTax, parsedState, 2);
            Assert.Equal(companyTotals.SocialSecurity, parsedSS, 2);
            Assert.Equal(companyTotals.Medicare, parsedMedicare, 2);
            Assert.Equal(companyTotals.TotalTaxes, parsedTotalTaxes, 2);
            Assert.Equal(companyTotals.PreTax401k, parsedPreTax401k, 2);
            Assert.Equal(companyTotals.PostTaxDeductions, parsedPostTax, 2);
            Assert.Equal(companyTotals.TotalDeductions, parsedTotalDeductions, 2);
            Assert.Equal(companyTotals.NetPay, parsedNet, 2);
            Assert.Equal(companyTotals.PayStubCount, parsedPayStubCount);

            // Verify employee rows can be parsed
            Assert.Equal(2, employeeRows.Count);
            var aliceRow = employeeRows.FirstOrDefault(r => r["EmployeeName"] == "Alice Johnson");
            var bobRow = employeeRows.FirstOrDefault(r => r["EmployeeName"] == "Bob Williams");
            Assert.NotNull(aliceRow);
            Assert.NotNull(bobRow);

            // Verify employee totals can be recalculated from parsed rows
            var recalculatedGross = employeeRows.Sum(r => decimal.Parse(r["GrossPay"], CultureInfo.InvariantCulture));
            var recalculatedNet = employeeRows.Sum(r => decimal.Parse(r["NetPay"], CultureInfo.InvariantCulture));
            var recalculatedTaxes = employeeRows.Sum(r => decimal.Parse(r["TotalTaxes"], CultureInfo.InvariantCulture));
            var recalculatedDeductions = employeeRows.Sum(r => decimal.Parse(r["TotalDeductions"], CultureInfo.InvariantCulture));

            Assert.Equal(companyTotals.GrossPay, recalculatedGross, 2);
            Assert.Equal(companyTotals.NetPay, recalculatedNet, 2);
            Assert.Equal(companyTotals.TotalTaxes, recalculatedTaxes, 2);
            Assert.Equal(companyTotals.TotalDeductions, recalculatedDeductions, 2);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Simple CSV line parser that handles quoted fields.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentField.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    // Toggle quote state
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // End of field
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        // Add last field
        result.Add(currentField.ToString());

        return result;
    }
}
