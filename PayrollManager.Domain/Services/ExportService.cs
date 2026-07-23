using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;
using System.Linq;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Service for exporting payroll data to CSV and PDF formats.
/// </summary>
public class ExportService
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettingsService _companySettingsService;

    public ExportService(AppDbContext dbContext, CompanySettingsService companySettingsService)
    {
        _dbContext = dbContext;
        _companySettingsService = companySettingsService;
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
    /// Export pay stub to PDF using QuestPDF, matching the Design LLC Earnings Statement layout
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

        if (payStub.Employee == null || payStub.PayRun == null)
            throw new InvalidOperationException("Pay stub is missing required Employee or PayRun data");

        var companySettings = await _companySettingsService.GetSettingsAsync();

        if (string.IsNullOrEmpty(outputPath))
        {
            var fileName = $"paystub_{payStub.Employee.LastName}_{payStub.PayRun.PayDate:yyyyMMdd}.pdf";
            outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
        }

        // Generate PDF bytes
        var pdfBytes = GeneratePayStubPdfBytes(payStub, companySettings);
        
        // Write to file
        await File.WriteAllBytesAsync(outputPath, pdfBytes);
        return outputPath;
    }

    /// <summary>
    /// Generates PDF bytes for a pay stub matching the Design LLC Earnings Statement layout
    /// </summary>
    public byte[] GeneratePayStubPdfBytes(PayStub payStub, CompanySettings companySettings)
    {
        var employee = payStub.Employee!;
        var payRun = payStub.PayRun!;
        
        // Derive check number from PayStub ID
        var checkNumber = payStub.Id.ToString("D4");
        
        // Determine pay schedule
        var paySchedule = companySettings.PayPeriodsPerYear switch
        {
            52 => "Weekly",
            26 => "Bi-Weekly",
            24 => "Semi-Monthly",
            12 => "Monthly",
            _ => $"{companySettings.PayPeriodsPerYear} periods/year"
        };

        // Calculate total deductions (pre-tax + post-tax)
        var totalDeductions = payStub.PreTax401kDeduction + payStub.PostTaxDeductions;
        var ytdDeductions = payStub.YtdGross - payStub.YtdNet - payStub.YtdTaxes;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.5f, Unit.Inch);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                // ═══════════════════════════════════════════════════════════════
                // TOP SECTION: EARNINGS STATEMENT
                // ═══════════════════════════════════════════════════════════════
                
                page.Content()
                    .Column(column =>
                    {
                        // Header Row: Company Name (left) | Earnings Statement + Check Number (right)
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text(companySettings.CompanyName)
                                    .FontSize(16).Bold();
                                if (!string.IsNullOrEmpty(companySettings.TaxId))
                                {
                                    col.Item().Text($"ID: {companySettings.TaxId}")
                                        .FontSize(8);
                                }
                                if (!string.IsNullOrEmpty(companySettings.CompanyAddress))
                                {
                                    col.Item().Text(companySettings.CompanyAddress)
                                        .FontSize(9);
                                }
                            });
                            
                            row.ConstantItem(200).Column(col =>
                            {
                                col.Item().AlignRight().Text("EARNINGS STATEMENT")
                                    .FontSize(14).Bold();
                                col.Item().AlignRight().Text($"Check Number: {checkNumber}")
                                    .FontSize(9);
                            });
                        });

                        column.Item().PaddingTop(10);

                        // Information Table: Employee Info | Pay Date | Pay Period | Pay Schedule
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2); // Employee Info
                                columns.RelativeColumn();    // Pay Date
                                columns.RelativeColumn();    // Pay Period
                                columns.RelativeColumn();    // Pay Schedule
                            });

                            // Employee Information Column
                            table.Cell().Column(empCol =>
                            {
                                empCol.Item().Text("EMPLOYEE INFORMATION").FontSize(8).Bold();
                                empCol.Item().Text(employee.FullName).FontSize(10).Bold();
                                empCol.Item().Text("SSN: XXX-XX-XXXX").FontSize(8); // Placeholder
                                empCol.Item().Text("123 Main Street").FontSize(8); // Placeholder address
                                empCol.Item().Text("City, ST 12345").FontSize(8); // Placeholder
                            });

                            // Pay Date Column
                            table.Cell().Column(dateCol =>
                            {
                                dateCol.Item().Text("PAY DATE").FontSize(8).Bold();
                                dateCol.Item().Text(payRun.PayDate.ToString("MMM dd, yyyy"))
                                    .FontSize(10).Bold();
                            });

                            // Pay Period Column
                            table.Cell().Column(periodCol =>
                            {
                                periodCol.Item().Text("PAY PERIOD").FontSize(8).Bold();
                                periodCol.Item().Text($"{payRun.PeriodStart:MMM dd, yyyy}")
                                    .FontSize(9);
                                periodCol.Item().Text($"to {payRun.PeriodEnd:MMM dd, yyyy}")
                                    .FontSize(9);
                            });

                            // Pay Schedule Column
                            table.Cell().Column(schedCol =>
                            {
                                schedCol.Item().Text("PAY SCHEDULE").FontSize(8).Bold();
                                schedCol.Item().Text(paySchedule)
                                    .FontSize(9);
                            });
                        });

                        column.Item().PaddingTop(10);

                        // Earnings Table: Description | Rate | Hours | Total | YTD
                        column.Item().Text("EARNINGS").FontSize(10).Bold();
                        column.Item().Table(earningsTable =>
                        {
                            earningsTable.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3); // Description
                                columns.ConstantColumn(70); // Rate
                                columns.ConstantColumn(60); // Hours
                                columns.ConstantColumn(80); // Total
                                columns.ConstantColumn(80); // YTD
                            });

                            earningsTable.Header(header =>
                            {
                                header.Cell().Text("Description").FontSize(9).Bold();
                                header.Cell().AlignRight().Text("Rate").FontSize(9).Bold();
                                header.Cell().AlignRight().Text("Hours").FontSize(9).Bold();
                                header.Cell().AlignRight().Text("Total").FontSize(9).Bold();
                                header.Cell().AlignRight().Text("YTD").FontSize(9).Bold();
                            });

                            // Add earning lines
                            if (payStub.EarningLines.Any())
                            {
                                foreach (var line in payStub.EarningLines.OrderBy(e => e.Type))
                                {
                                    earningsTable.Cell().Text(line.Description).FontSize(9);
                                    earningsTable.Cell().AlignRight().Text($"${line.Rate:F2}").FontSize(9);
                                    earningsTable.Cell().AlignRight().Text(line.Hours > 0 ? line.Hours.ToString("F2") : "—").FontSize(9);
                                    earningsTable.Cell().AlignRight().Text($"${line.Amount:F2}").FontSize(9);
                                    earningsTable.Cell().AlignRight().Text("—").FontSize(9); // YTD per line not tracked
                                }
                            }
                            else
                            {
                                // Fallback: single regular earnings row
                                var rate = employee.IsHourly ? employee.HourlyRate : (payStub.GrossPay / (payStub.HoursWorked > 0 ? payStub.HoursWorked : 1));
                                earningsTable.Cell().Text(employee.IsHourly ? "Regular Earnings" : "Salary").FontSize(9);
                                earningsTable.Cell().AlignRight().Text($"${rate:F2}").FontSize(9);
                                earningsTable.Cell().AlignRight().Text(payStub.HoursWorked > 0 ? payStub.HoursWorked.ToString("F2") : "—").FontSize(9);
                                earningsTable.Cell().AlignRight().Text($"${payStub.GrossPay:F2}").FontSize(9);
                                earningsTable.Cell().AlignRight().Text("—").FontSize(9);
                            }

                            // Total Gross row
                            earningsTable.Cell().ColumnSpan(3).AlignRight().Text("Total Gross").FontSize(9).Bold();
                            earningsTable.Cell().AlignRight().Text($"${payStub.GrossPay:F2}").FontSize(9).Bold();
                            earningsTable.Cell().AlignRight().Text($"${payStub.YtdGross:F2}").FontSize(9).Bold();
                        });

                        column.Item().PaddingTop(10);

                        // Taxes/Deductions Section (Current & YTD)
                        column.Item().Row(taxesRow =>
                        {
                            // Left: Taxes/Deductions table
                            taxesRow.RelativeItem().Table(taxesTable =>
                            {
                                taxesTable.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.ConstantColumn(80); // Current
                                    columns.ConstantColumn(80); // YTD
                                });

                                taxesTable.Header(header =>
                                {
                                    header.Cell().Text("TAXES / DEDUCTIONS").FontSize(9).Bold();
                                    header.Cell().AlignRight().Text("Current").FontSize(9).Bold();
                                    header.Cell().AlignRight().Text("YTD").FontSize(9).Bold();
                                });

                                // Federal Tax
                                taxesTable.Cell().Text("Federal").FontSize(9);
                                taxesTable.Cell().AlignRight().Text($"${payStub.TaxFederal:F2}").FontSize(9);
                                taxesTable.Cell().AlignRight().Text("—").FontSize(9); // YTD per tax not tracked separately

                                // Medicare
                                taxesTable.Cell().Text("Medicare").FontSize(9);
                                taxesTable.Cell().AlignRight().Text($"${payStub.TaxMedicare:F2}").FontSize(9);
                                taxesTable.Cell().AlignRight().Text("—").FontSize(9);

                                // FICA (Social Security)
                                taxesTable.Cell().Text("FICA").FontSize(9);
                                taxesTable.Cell().AlignRight().Text($"${payStub.TaxSocialSecurity:F2}").FontSize(9);
                                taxesTable.Cell().AlignRight().Text("—").FontSize(9);

                                // State Tax
                                taxesTable.Cell().Text("State").FontSize(9);
                                taxesTable.Cell().AlignRight().Text($"${payStub.TaxState:F2}").FontSize(9);
                                taxesTable.Cell().AlignRight().Text("—").FontSize(9);

                                // Other Deductions (if any)
                                if (totalDeductions > 0)
                                {
                                    taxesTable.Cell().Text("Other Deductions").FontSize(9);
                                    taxesTable.Cell().AlignRight().Text($"${totalDeductions:F2}").FontSize(9);
                                    taxesTable.Cell().AlignRight().Text($"${ytdDeductions:F2}").FontSize(9);
                                }

                                // Summary rows
                                taxesTable.Cell().Text("Gross").FontSize(9).Bold();
                                taxesTable.Cell().AlignRight().Text($"${payStub.GrossPay:F2}").FontSize(9).Bold();
                                taxesTable.Cell().AlignRight().Text($"${payStub.YtdGross:F2}").FontSize(9).Bold();

                                taxesTable.Cell().Text("Taxes / Deductions").FontSize(9).Bold();
                                taxesTable.Cell().AlignRight().Text($"${payStub.TotalTaxes + totalDeductions:F2}").FontSize(9).Bold();
                                taxesTable.Cell().AlignRight().Text($"${payStub.YtdTaxes + ytdDeductions:F2}").FontSize(9).Bold();

                                taxesTable.Cell().Text("Net Pay").FontSize(9).Bold();
                                taxesTable.Cell().AlignRight().Text($"${payStub.NetPay:F2}").FontSize(9).Bold();
                                taxesTable.Cell().AlignRight().Text($"${payStub.YtdNet:F2}").FontSize(9).Bold();
                            });
                        });

                        column.Item().PaddingTop(10);

                        // YTD Summary Strip (horizontal bar)
                        column.Item().Background(Colors.Grey.Lighten3)
                            .Padding(8)
                            .Row(ytdRow =>
                            {
                                ytdRow.RelativeItem().Column(ytdCol =>
                                {
                                    ytdCol.Item().Text("YTD GROSS").FontSize(8).Bold();
                                    ytdCol.Item().Text($"${payStub.YtdGross:F2}").FontSize(11).Bold();
                                });
                                ytdRow.RelativeItem().Column(ytdCol =>
                                {
                                    ytdCol.Item().Text("YTD TAXES / DEDUCTIONS").FontSize(8).Bold();
                                    ytdCol.Item().Text($"${payStub.YtdTaxes + ytdDeductions:F2}").FontSize(11).Bold();
                                });
                                ytdRow.RelativeItem().Column(ytdCol =>
                                {
                                    ytdCol.Item().Text("YTD NET PAY").FontSize(8).Bold();
                                    ytdCol.Item().Text($"${payStub.YtdNet:F2}").FontSize(11).Bold();
                                });
                            });

                        column.Item().PaddingTop(15);

                        // ═══════════════════════════════════════════════════════════════
                        // BOTTOM SECTION: DIRECT DEPOSIT ADVICE (Tear-off)
                        // ═══════════════════════════════════════════════════════════════

                        // Dashed line separator
                        column.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);

                        column.Item().PaddingTop(10);

                        // Direct Deposit Advice Header
                        column.Item().Row(ddRow =>
                        {
                            ddRow.RelativeItem().Column(ddCol =>
                            {
                                ddCol.Item().Text(companySettings.CompanyName).FontSize(10).Bold();
                                if (!string.IsNullOrEmpty(companySettings.CompanyAddress))
                                {
                                    ddCol.Item().Text(companySettings.CompanyAddress).FontSize(8);
                                }
                            });
                            ddRow.ConstantItem(200).Column(ddCol =>
                            {
                                ddCol.Item().AlignRight().Text($"Check Number: {checkNumber}").FontSize(9);
                                ddCol.Item().AlignRight().Text($"Pay Date: {payRun.PayDate:MMM dd, yyyy}").FontSize(9);
                            });
                        });

                        column.Item().PaddingTop(10);

                        // Body: Deposited to / Account of
                        column.Item().Column(bodyCol =>
                        {
                            bodyCol.Item().Text($"Deposited to {employee.FullName}").FontSize(10);
                            bodyCol.Item().Text("to the Account of:").FontSize(9);
                            bodyCol.Item().Text("123 Main Street").FontSize(9); // Placeholder
                            bodyCol.Item().Text("City, ST 12345").FontSize(9); // Placeholder
                        });

                        column.Item().PaddingTop(10);

                        // Amount Block (prominent box)
                        column.Item().Background(Colors.White)
                            .Border(1)
                            .BorderColor(Colors.Black)
                            .Padding(12)
                            .Row(amountRow =>
                            {
                                amountRow.RelativeItem().Column(amountCol =>
                                {
                                    amountCol.Item().Text("Amount:").FontSize(9);
                                    amountCol.Item().Text(AmountToWordsConverter.ConvertToWords(payStub.NetPay))
                                        .FontSize(9);
                                });
                                amountRow.ConstantItem(120).AlignRight().Column(amountCol =>
                                {
                                    amountCol.Item().Text("$").FontSize(16).Bold();
                                    amountCol.Item().Text($"{payStub.NetPay:F2}").FontSize(20).Bold();
                                });
                            });

                        column.Item().PaddingTop(10);

                        // Footer text
                        column.Item().AlignCenter().Column(footerCol =>
                        {
                            footerCol.Item().Text("THIS IS NOT A CHECK").FontSize(8).Bold();
                            footerCol.Item().Text("DIRECT DEPOSIT").FontSize(8).Bold();
                            footerCol.Item().Text("NON-NEGOTIABLE").FontSize(8).Bold();
                        });
                    });
            });
        });

        return document.GeneratePdf();
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
        // Header row with all columns
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

        // Add Totals row at the end with company totals
        if (companyTotals != null)
        {
            sb.AppendLine($"Totals,{EscapeCsv("COMPANY TOTALS")}," +
                         $"{companyTotals.GrossPay:F2}," +
                         $"{companyTotals.FederalTax:F2}," +
                         $"{companyTotals.StateTax:F2}," +
                         $"{companyTotals.SocialSecurity:F2}," +
                         $"{companyTotals.Medicare:F2}," +
                         $"{companyTotals.TotalTaxes:F2}," +
                         $"{companyTotals.PreTax401k:F2}," +
                         $"{companyTotals.PostTaxDeductions:F2}," +
                         $"{companyTotals.TotalDeductions:F2}," +
                         $"{companyTotals.NetPay:F2}," +
                         $"{companyTotals.PayStubCount}");
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
        var companySettings = await _companySettingsService.GetSettingsAsync();

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
