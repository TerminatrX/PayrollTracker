using Microsoft.EntityFrameworkCore;
using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Rpc;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Handlers;

/// <summary>
/// Read-only reporting: dashboard KPIs, the payroll register report (with CSV export), and the
/// audit log. All totals count only POSTED runs (AggregationService enforces this).
/// </summary>
public sealed class ReportingCommands
{
    private readonly Func<AppDbContext> _contextFactory;

    public ReportingCommands(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public void RegisterOn(CommandDispatcher dispatcher)
    {
        dispatcher.Register("get_dashboard", (_, ct) => GetDashboardAsync(ct));
        dispatcher.Register("get_payroll_report", (p, ct) => GetReportAsync(p, ct));
        dispatcher.Register("export_payroll_report", (p, ct) => ExportReportAsync(p, ct));
        dispatcher.Register("get_audit_log", (p, ct) => GetAuditLogAsync(p, ct));
    }

    private async Task<object?> GetDashboardAsync(CancellationToken ct)
    {
        await using var db = _contextFactory();
        var aggregation = new AggregationService(db);
        var settingsService = new CompanySettingsService(db);

        var year = DateTime.Today.Year;
        var companyYtd = await aggregation.GetCompanyYtdTotalsAsync(year);
        var employerYtd = await SumEmployerTaxesAsync(db, new DateTime(year, 1, 1), new DateTime(year, 12, 31), ct);

        var activeEmployeeCount = await db.Employees.CountAsync(e => e.IsActive, ct);

        var postedRuns = await db.PayRuns.AsNoTracking()
            .Where(r => r.Status == PayRunStatus.Posted)
            .OrderByDescending(r => r.PayDate)
            .ToListAsync(ct);

        var lastRun = postedRuns.FirstOrDefault();

        // Next pay date, projected from the last non-voided run and the company frequency.
        var settings = await settingsService.GetSettingsAsync();
        var frequency = PayPeriodCalculator.GetPayFrequency(settings.PayPeriodsPerYear);
        var lastNonVoided = await db.PayRuns.AsNoTracking()
            .Where(r => r.Status != PayRunStatus.Voided)
            .OrderByDescending(r => r.PayDate)
            .FirstOrDefaultAsync(ct);
        var nextPayDate = PayPeriodCalculator.CalculateNextPeriod(lastNonVoided, frequency).PayDate;

        return new DashboardDto
        {
            Year = year,
            CompanyYtd = CompanyTotalsDto.From(companyYtd, employerYtd),
            ActiveEmployeeCount = activeEmployeeCount,
            PostedRunCount = postedRuns.Count,
            LastPayRun = lastRun is null ? null : await BuildRunSummaryAsync(db, lastRun, ct),
            NextPayDate = nextPayDate,
            SuiConfigured = settings.SuiRatePercent > 0m && settings.SuiWageBase > 0m
        };
    }

    private async Task<object?> GetReportAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<PayrollReportRequest>(parameters);
        ValidateRange(request.StartDate, request.EndDate);

        await using var db = _contextFactory();
        var aggregation = new AggregationService(db);

        var employees = await aggregation.GetAllEmployeeTotalsAsync(request.StartDate, request.EndDate);
        var company = BuildCompanyTotals(employees);
        var employerTaxes = await SumEmployerTaxesAsync(db, request.StartDate, request.EndDate, ct);

        return new PayrollReportDto
        {
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Company = CompanyTotalsDto.From(company, employerTaxes),
            Employees = employees.Select(EmployeeTotalsDto.From).ToList()
        };
    }

    private async Task<object?> ExportReportAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<ExportPayrollReportRequest>(parameters);
        ValidateRange(request.StartDate, request.EndDate);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw RpcException.Validation(new Dictionary<string, string[]>
            {
                ["outputPath"] = new[] { "A destination path is required." }
            });
        }

        await using var db = _contextFactory();
        var aggregation = new AggregationService(db);
        var settingsService = new CompanySettingsService(db);
        var exportService = new ExportService(db, settingsService);

        var employees = await aggregation.GetAllEmployeeTotalsAsync(request.StartDate, request.EndDate);
        var company = BuildCompanyTotals(employees);

        var path = await exportService.ExportReportToCsvAsync(
            employees, company, request.StartDate, request.EndDate, request.OutputPath);

        return new ExportReportResponse { Path = path };
    }

    private async Task<object?> GetAuditLogAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.ParamsOrDefault<GetAuditLogRequest>(parameters);
        var limit = Math.Clamp(request.Limit <= 0 ? 200 : request.Limit, 1, 1000);

        await using var db = _contextFactory();

        var entries = await db.AuditLog
            .AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync(ct);

        return new GetAuditLogResponse { Entries = entries.Select(AuditEntryDto.From).ToList() };
    }

    /// <summary>
    /// Sums employer-paid taxes for POSTED runs whose pay date is in the range. CompanyTotals
    /// predates employer taxes, so this is computed separately (client-side, as SQLite cannot
    /// SUM decimals server-side).
    /// </summary>
    private static async Task<decimal> SumEmployerTaxesAsync(AppDbContext db, DateTime start, DateTime end, CancellationToken ct)
    {
        var stubs = await db.PayStubs
            .AsNoTracking()
            .Where(s => s.PayRun!.Status == PayRunStatus.Posted &&
                        s.PayRun.PayDate >= start && s.PayRun.PayDate <= end)
            .ToListAsync(ct);

        return stubs.Sum(s => s.TotalEmployerTaxes);
    }

    /// <summary>Rolls per-employee totals up into a company total.</summary>
    private static CompanyTotals BuildCompanyTotals(IReadOnlyList<EmployeeTotals> employees) => new()
    {
        EmployeeCount = employees.Count,
        PayStubCount = employees.Sum(e => e.PayStubCount),
        GrossPay = employees.Sum(e => e.GrossPay),
        FederalTax = employees.Sum(e => e.FederalTax),
        StateTax = employees.Sum(e => e.StateTax),
        SocialSecurity = employees.Sum(e => e.SocialSecurity),
        Medicare = employees.Sum(e => e.Medicare),
        TotalTaxes = employees.Sum(e => e.TotalTaxes),
        PreTax401k = employees.Sum(e => e.PreTax401k),
        PostTaxDeductions = employees.Sum(e => e.PostTaxDeductions),
        NetPay = employees.Sum(e => e.NetPay)
    };

    private async Task<PayRunSummaryDto> BuildRunSummaryAsync(AppDbContext db, PayRun run, CancellationToken ct)
    {
        var stubs = await db.PayStubs.AsNoTracking().Where(s => s.PayRunId == run.Id).ToListAsync(ct);
        var gross = stubs.Sum(s => s.GrossPay);
        var employerTaxes = stubs.Sum(s => s.TotalEmployerTaxes);

        return new PayRunSummaryDto
        {
            Id = run.Id,
            PeriodStart = run.PeriodStart,
            PeriodEnd = run.PeriodEnd,
            PayDate = run.PayDate,
            Status = run.Status.ToString(),
            EmployeeCount = stubs.Count,
            GrossPay = gross,
            NetPay = stubs.Sum(s => s.NetPay),
            EmployeeTaxes = stubs.Sum(s => s.TotalTaxes),
            EmployerTaxes = employerTaxes,
            TotalEmployerCost = gross + employerTaxes,
            PostedAtUtc = run.PostedAtUtc,
            VoidedAtUtc = run.VoidedAtUtc,
            VoidReason = run.VoidReason,
            EngineVersion = run.CalculationEngineVersion
        };
    }

    private static void ValidateRange(DateTime start, DateTime end)
    {
        if (end < start)
        {
            throw RpcException.Validation(new Dictionary<string, string[]>
            {
                ["endDate"] = new[] { "End date cannot precede the start date." }
            });
        }
    }
}
