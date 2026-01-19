using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Service for exporting payroll data to CSV and PDF formats.
/// </summary>
public class ExportService
{
    private readonly AppDbContext _dbContext;

    public ExportService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Export pay stub to CSV
    /// </summary>
    public async Task<string> ExportPayStubToCsvAsync(int payStubId, string? outputPath = null)
    {
        var payStub = await _dbContext.PayStubs
            .Include(ps => ps.Employee)
            .Include(ps => ps.PayRun)
            .Include(ps => ps.EarningLines)
            .Include(ps => ps.DeductionLines)
            .Include(ps => ps.TaxLines)
            .FirstOrDefaultAsync(ps => ps.Id == payStubId);

        if (payStub == null)
            throw new ArgumentException($"Pay stub {payStubId} not found");

        var sb = new StringBuilder();
        sb.AppendLine("PAY STUB EXPORT");
        sb.AppendLine($"Employee,{payStub.Employee?.FullName ?? "Unknown"}");
        sb.AppendLine($"Employee ID,{payStub.EmployeeId}");
        sb.AppendLine($"Pay Period,{payStub.PayRun?.PeriodStart:yyyy-MM-dd} to {payStub.PayRun?.PeriodEnd:yyyy-MM-dd}");
        sb.AppendLine($"Pay Date,{payStub.PayRun?.PayDate:yyyy-MM-dd}");
        sb.AppendLine();

        // Earnings
        sb.AppendLine("EARNINGS");
        sb.AppendLine("Type,Description,Hours,Rate,Amount");
        foreach (var line in payStub.EarningLines)
        {
            sb.AppendLine($"{line.Type},{EscapeCsv(line.Description)},{line.Hours:F2},{line.Rate:F2},{line.Amount:F2}");
        }
        sb.AppendLine($",,,,Total Gross Pay,{payStub.GrossPay:F2}");
        sb.AppendLine();

        // Deductions
        sb.AppendLine("DEDUCTIONS");
        sb.AppendLine("Type,Description,Is Pre-Tax,Amount");
        foreach (var line in payStub.DeductionLines)
        {
            sb.AppendLine($"{line.Type},{EscapeCsv(line.Description)},{line.IsPreTax},{line.Amount:F2}");
        }
        sb.AppendLine($"Total Deductions,,,{payStub.PreTax401kDeduction + payStub.PostTaxDeductions:F2}");
        sb.AppendLine();

        // Taxes
        sb.AppendLine("TAXES");
        sb.AppendLine("Type,Description,Rate,Taxable Amount,Amount");
        foreach (var line in payStub.TaxLines)
        {
            sb.AppendLine($"{line.Type},{EscapeCsv(line.Description)},{line.Rate:F2}%,{line.TaxableAmount:F2},{line.Amount:F2}");
        }
        sb.AppendLine($"Total Taxes,,,,{payStub.TotalTaxes:F2}");
        sb.AppendLine();

        // Summary
        sb.AppendLine("SUMMARY");
        sb.AppendLine($"Gross Pay,{payStub.GrossPay:F2}");
        sb.AppendLine($"Pre-Tax Deductions,{payStub.PreTax401kDeduction:F2}");
        sb.AppendLine($"Taxable Income,{payStub.GrossPay - payStub.PreTax401kDeduction:F2}");
        sb.AppendLine($"Total Taxes,{payStub.TotalTaxes:F2}");
        sb.AppendLine($"Post-Tax Deductions,{payStub.PostTaxDeductions:F2}");
        sb.AppendLine($"Net Pay,{payStub.NetPay:F2}");
        sb.AppendLine();

        // YTD
        sb.AppendLine("YEAR-TO-DATE");
        sb.AppendLine($"YTD Gross,{payStub.YtdGross:F2}");
        sb.AppendLine($"YTD Taxes,{payStub.YtdTaxes:F2}");
        sb.AppendLine($"YTD Net,{payStub.YtdNet:F2}");

        var content = sb.ToString();
        
        if (string.IsNullOrEmpty(outputPath))
        {
            var fileName = $"paystub_{payStub.Employee?.LastName ?? "unknown"}_{payStub.PayRun?.PayDate:yyyyMMdd}.csv";
            outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        }

        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8);
        return outputPath;
    }

    /// <summary>
    /// Export pay stub to PDF using QuestPDF
    /// </summary>
    public async Task<string> ExportPayStubToPdfAsync(int payStubId, string? outputPath = null)
    {
        var payStub = await _dbContext.PayStubs
            .Include(ps => ps.Employee)
            .Include(ps => ps.PayRun)
            .Include(ps => ps.EarningLines)
            .Include(ps => ps.DeductionLines)
            .Include(ps => ps.TaxLines)
            .FirstOrDefaultAsync(ps => ps.Id == payStubId);

        if (payStub == null)
            throw new ArgumentException($"Pay stub {payStubId} not found");

        var companySettings = await _dbContext.CompanySettings.FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(outputPath))
        {
            var fileName = $"paystub_{payStub.Employee?.LastName ?? "unknown"}_{payStub.PayRun?.PayDate:yyyyMMdd}.pdf";
            outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Row(row =>
                    {
                        row.RelativeItem().Column(column =>
                        {
                            column.Item().Text(companySettings?.CompanyName ?? "Company Name")
                                .FontSize(18).Bold();
                            column.Item().Text(companySettings?.CompanyAddress ?? "")
                                .FontSize(10);
                        });
                        row.ConstantItem(100).AlignRight().Text("PAY STUB")
                            .FontSize(16).Bold();
                    });

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        // Employee Info
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"Employee: {payStub.Employee?.FullName ?? "Unknown"}");
                            row.ConstantItem(150).Text($"ID: {payStub.EmployeeId:D5}");
                        });
                        column.Item().Text($"Pay Period: {payStub.PayRun?.PeriodStart:MMM dd, yyyy} - {payStub.PayRun?.PeriodEnd:MMM dd, yyyy}");
                        column.Item().Text($"Pay Date: {payStub.PayRun?.PayDate:MMM dd, yyyy}");
                        column.Item().PaddingTop(10);

                        // Earnings
                        column.Item().Text("EARNINGS").FontSize(12).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Type").Bold();
                                header.Cell().Text("Description").Bold();
                                header.Cell().Text("Hours").Bold();
                                header.Cell().AlignRight().Text("Rate").Bold();
                                header.Cell().AlignRight().Text("Amount").Bold();
                            });

                            foreach (var line in payStub.EarningLines)
                            {
                                table.Cell().Text(line.Type.ToString());
                                table.Cell().Text(line.Description);
                                table.Cell().Text(line.Hours.ToString("F2"));
                                table.Cell().AlignRight().Text($"${line.Rate:F2}");
                                table.Cell().AlignRight().Text($"${line.Amount:F2}");
                            }

                            table.Cell().ColumnSpan(4).AlignRight().Text("Total Gross Pay:").Bold();
                            table.Cell().AlignRight().Text($"${payStub.GrossPay:F2}").Bold();
                        });

                        column.Item().PaddingTop(10);

                        // Deductions
                        column.Item().Text("DEDUCTIONS").FontSize(12).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(80);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Type").Bold();
                                header.Cell().Text("Description").Bold();
                                header.Cell().Text("Pre-Tax").Bold();
                                header.Cell().AlignRight().Text("Amount").Bold();
                            });

                            foreach (var line in payStub.DeductionLines)
                            {
                                table.Cell().Text(line.Type.ToString());
                                table.Cell().Text(line.Description);
                                table.Cell().Text(line.IsPreTax ? "Yes" : "No");
                                table.Cell().AlignRight().Text($"${line.Amount:F2}");
                            }
                        });

                        column.Item().PaddingTop(10);

                        // Taxes
                        column.Item().Text("TAXES").FontSize(12).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Type").Bold();
                                header.Cell().Text("Description").Bold();
                                header.Cell().Text("Rate").Bold();
                                header.Cell().AlignRight().Text("Taxable").Bold();
                                header.Cell().AlignRight().Text("Amount").Bold();
                            });

                            foreach (var line in payStub.TaxLines)
                            {
                                table.Cell().Text(line.Type.ToString());
                                table.Cell().Text(line.Description);
                                table.Cell().Text($"{line.Rate:F2}%");
                                table.Cell().AlignRight().Text($"${line.TaxableAmount:F2}");
                                table.Cell().AlignRight().Text($"${line.Amount:F2}");
                            }
                        });

                        column.Item().PaddingTop(10);

                        // Summary
                        column.Item().Text("SUMMARY").FontSize(12).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(100);
                            });

                            table.Cell().Text("Gross Pay");
                            table.Cell().AlignRight().Text($"${payStub.GrossPay:F2}");

                            table.Cell().Text("Pre-Tax Deductions");
                            table.Cell().AlignRight().Text($"${payStub.PreTax401kDeduction:F2}");

                            table.Cell().Text("Taxable Income");
                            table.Cell().AlignRight().Text($"${payStub.GrossPay - payStub.PreTax401kDeduction:F2}");

                            table.Cell().Text("Total Taxes");
                            table.Cell().AlignRight().Text($"${payStub.TotalTaxes:F2}");

                            table.Cell().Text("Post-Tax Deductions");
                            table.Cell().AlignRight().Text($"${payStub.PostTaxDeductions:F2}");

                            table.Cell().Text("Net Pay").Bold();
                            table.Cell().AlignRight().Text($"${payStub.NetPay:F2}").Bold();
                        });

                        column.Item().PaddingTop(10);

                        // YTD
                        column.Item().Text("YEAR-TO-DATE").FontSize(12).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(100);
                            });

                            table.Cell().Text("YTD Gross");
                            table.Cell().AlignRight().Text($"${payStub.YtdGross:F2}");

                            table.Cell().Text("YTD Taxes");
                            table.Cell().AlignRight().Text($"${payStub.YtdTaxes:F2}");

                            table.Cell().Text("YTD Net");
                            table.Cell().AlignRight().Text($"${payStub.YtdNet:F2}");
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .DefaultTextStyle(TextStyle.Default.FontSize(8))
                    .Text(x =>
                    {
                        x.Span("Generated on ");
                        x.Span(DateTime.Now.ToString("MMM dd, yyyy HH:mm"));
                    });
            });
        });

        document.GeneratePdf(outputPath);
        return outputPath;
    }

    /// <summary>
    /// Export payroll report to CSV
    /// </summary>
    public async Task<string> ExportReportToCsvAsync(
        List<EmployeeTotals> employeeTotals,
        CompanyTotals? companyTotals,
        DateTime startDate,
        DateTime endDate,
        string? outputPath = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PAYROLL REPORT");
        sb.AppendLine($"Period,{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
        sb.AppendLine($"Generated,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        if (companyTotals != null)
        {
            sb.AppendLine("COMPANY TOTALS");
            sb.AppendLine($"Employee Count,{companyTotals.EmployeeCount}");
            sb.AppendLine($"Gross Pay,{companyTotals.GrossPay:F2}");
            sb.AppendLine($"Federal Tax,{companyTotals.FederalTax:F2}");
            sb.AppendLine($"State Tax,{companyTotals.StateTax:F2}");
            sb.AppendLine($"Social Security,{companyTotals.SocialSecurity:F2}");
            sb.AppendLine($"Medicare,{companyTotals.Medicare:F2}");
            sb.AppendLine($"Total Taxes,{companyTotals.TotalTaxes:F2}");
            sb.AppendLine($"Pre-Tax 401k,{companyTotals.PreTax401k:F2}");
            sb.AppendLine($"Post-Tax Deductions,{companyTotals.PostTaxDeductions:F2}");
            sb.AppendLine($"Total Deductions,{companyTotals.TotalDeductions:F2}");
            sb.AppendLine($"Net Pay,{companyTotals.NetPay:F2}");
            sb.AppendLine();
        }

        sb.AppendLine("EMPLOYEE TOTALS");
        sb.AppendLine("Employee ID,Employee Name,Gross Pay,Federal Tax,State Tax,Social Security,Medicare,Total Taxes,Pre-Tax 401k,Post-Tax Deductions,Total Deductions,Net Pay,Pay Stubs");
        
        foreach (var totals in employeeTotals)
        {
            sb.AppendLine($"{totals.EmployeeId}," +
                         $"{EscapeCsv(totals.EmployeeName)}," +
                         $"{totals.GrossPay:F2}," +
                         $"{totals.FederalTax:F2}," +
                         $"{totals.StateTax:F2}," +
                         $"{totals.SocialSecurity:F2}," +
                         $"{totals.Medicare:F2}," +
                         $"{totals.TotalTaxes:F2}," +
                         $"{totals.PreTax401k:F2}," +
                         $"{totals.PostTaxDeductions:F2}," +
                         $"{totals.TotalDeductions:F2}," +
                         $"{totals.NetPay:F2}," +
                         $"{totals.PayStubCount}");
        }

        var content = sb.ToString();
        
        if (string.IsNullOrEmpty(outputPath))
        {
            var fileName = $"payroll_report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv";
            outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        }

        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8);
        return outputPath;
    }

    /// <summary>
    /// Export payroll report to PDF
    /// </summary>
    public async Task<string> ExportReportToPdfAsync(
        List<EmployeeTotals> employeeTotals,
        CompanyTotals? companyTotals,
        DateTime startDate,
        DateTime endDate,
        string? outputPath = null)
    {
        var companySettings = await _dbContext.CompanySettings.FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(outputPath))
        {
            var fileName = $"payroll_report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf";
            outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Row(row =>
                    {
                        row.RelativeItem().Column(column =>
                        {
                            column.Item().Text(companySettings?.CompanyName ?? "Company Name")
                                .FontSize(18).Bold();
                            column.Item().Text("Payroll Report")
                                .FontSize(14);
                        });
                        row.ConstantItem(100).AlignRight().Text($"{startDate:MMM dd, yyyy}\n{endDate:MMM dd, yyyy}")
                            .FontSize(10);
                    });

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        if (companyTotals != null)
                        {
                            column.Item().Text("COMPANY TOTALS").FontSize(14).Bold();
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(120);
                                });

                                table.Cell().Text("Employees");
                                table.Cell().AlignRight().Text(companyTotals.EmployeeCount.ToString());

                                table.Cell().Text("Gross Pay");
                                table.Cell().AlignRight().Text($"${companyTotals.GrossPay:F2}");

                                table.Cell().Text("Total Taxes");
                                table.Cell().AlignRight().Text($"${companyTotals.TotalTaxes:F2}");

                                table.Cell().Text("Total Deductions");
                                table.Cell().AlignRight().Text($"${companyTotals.TotalDeductions:F2}");

                                table.Cell().Text("Net Pay").Bold();
                                table.Cell().AlignRight().Text($"${companyTotals.NetPay:F2}").Bold();
                            });

                            column.Item().PaddingTop(15);
                        }

                        column.Item().Text("EMPLOYEE TOTALS").FontSize(14).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(80);
                                columns.RelativeColumn();
                                columns.ConstantColumn(90);
                                columns.ConstantColumn(90);
                                columns.ConstantColumn(90);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("ID").Bold();
                                header.Cell().Text("Employee").Bold();
                                header.Cell().AlignRight().Text("Gross").Bold();
                                header.Cell().AlignRight().Text("Taxes").Bold();
                                header.Cell().AlignRight().Text("Net").Bold();
                            });

                            foreach (var totals in employeeTotals)
                            {
                                table.Cell().Text(totals.EmployeeId.ToString());
                                table.Cell().Text(totals.EmployeeName);
                                table.Cell().AlignRight().Text($"${totals.GrossPay:F2}");
                                table.Cell().AlignRight().Text($"${totals.TotalTaxes:F2}");
                                table.Cell().AlignRight().Text($"${totals.NetPay:F2}");
                            }
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .DefaultTextStyle(TextStyle.Default.FontSize(8))
                    .Text(x =>
                    {
                        x.Span("Generated on ");
                        x.Span(DateTime.Now.ToString("MMM dd, yyyy HH:mm"));
                    });
            });
        });

        document.GeneratePdf(outputPath);
        return outputPath;
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
