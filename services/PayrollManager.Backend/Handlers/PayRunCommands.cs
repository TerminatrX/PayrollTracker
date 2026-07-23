using Microsoft.EntityFrameworkCore;
using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Rpc;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Handlers;

/// <summary>
/// Pay-run lifecycle commands: bootstrap a wizard, preview, post, void, and read history.
///
/// The actual posting/voiding rules (transactional commit, hash guard, immutability, warnings)
/// live in the domain PayRunService; these commands are thin adapters over it.
/// </summary>
public sealed class PayRunCommands
{
    private readonly Func<AppDbContext> _contextFactory;

    public PayRunCommands(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public void RegisterOn(CommandDispatcher dispatcher)
    {
        dispatcher.Register("suggest_pay_run", (_, ct) => SuggestAsync(ct));
        dispatcher.Register("preview_pay_run", (p, ct) => PreviewAsync(p, ct));
        dispatcher.Register("post_pay_run", (p, ct) => PostAsync(p, ct));
        dispatcher.Register("void_pay_run", (p, ct) => VoidAsync(p, ct));
        dispatcher.Register("get_pay_runs", (_, ct) => GetPayRunsAsync(ct));
        dispatcher.Register("get_pay_run", (p, ct) => GetPayRunAsync(p, ct));
    }

    private (PayRunService Runs, PayrollService Payroll, CompanySettingsService Settings) Services(AppDbContext db)
    {
        var settings = new CompanySettingsService(db);
        var payroll = new PayrollService(db, settings);
        return (new PayRunService(db, payroll), payroll, settings);
    }

    /// <summary>
    /// Bootstraps the wizard: the next pay period (from the last posted run and the company's
    /// frequency) and the active employees with suggested hours already split for FLSA.
    /// </summary>
    private async Task<object?> SuggestAsync(CancellationToken ct)
    {
        await using var db = _contextFactory();
        var (_, _, settingsService) = Services(db);

        var settings = await settingsService.GetSettingsAsync();
        var frequency = PayPeriodCalculator.GetPayFrequency(settings.PayPeriodsPerYear);

        // Base the next period on the most recent NON-voided run.
        var lastRun = await db.PayRuns
            .Where(r => r.Status != PayRunStatus.Voided)
            .OrderByDescending(r => r.PayDate)
            .FirstOrDefaultAsync(ct);

        var next = PayPeriodCalculator.CalculateNextPeriod(lastRun, frequency);

        var workweeks = FlsaOvertime.WorkweeksInPeriod(settings.PayPeriodsPerYear);

        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        var suggestions = employees.Select(e =>
        {
            // Hourly: split the default hours across the period's workweeks. Salaried: no
            // hours (the domain pays a fixed per-period salary regardless of hours).
            decimal regular = 0m, overtime = 0m;
            if (e.IsHourly)
            {
                (regular, overtime) =
                    FlsaOvertime.SplitEvenlyAcrossWorkweeks(e.DefaultHoursPerPeriod, workweeks);
            }

            return new PayRunEmployeeSuggestion
            {
                EmployeeId = e.Id,
                FullName = e.FullName,
                JobTitle = e.JobTitle,
                Department = e.Department,
                IsHourly = e.IsHourly,
                HourlyRate = e.HourlyRate,
                AnnualSalary = e.AnnualSalary,
                SalaryPerPeriod = settings.PayPeriodsPerYear > 0
                    ? Money.Round(e.AnnualSalary / settings.PayPeriodsPerYear)
                    : 0m,
                SuggestedRegularHours = regular,
                SuggestedOvertimeHours = overtime,
                W4OnFile = e.W4OnFile
            };
        }).ToList();

        return new SuggestPayRunResponse
        {
            Draft = new PayRunDraftInput
            {
                PeriodStart = next.PeriodStart,
                PeriodEnd = next.PeriodEnd,
                PayDate = next.PayDate
            },
            Employees = suggestions
        };
    }

    private async Task<object?> PreviewAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<PreviewPayRunRequest>(parameters);

        await using var db = _contextFactory();
        var (runs, _, _) = Services(db);

        try
        {
            var preview = await runs.PreviewAsync(
                request.Draft.ToDomain(),
                request.Employees.Select(e => e.ToDomain()).ToList(),
                ct);

            return PayRunPreviewDto.From(preview);
        }
        catch (PayRunPostingException ex)
        {
            throw RpcException.BusinessRule(ex.Message);
        }
    }

    private async Task<object?> PostAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<PostPayRunRequest>(parameters);

        await using var db = _contextFactory();
        var (runs, _, _) = Services(db);

        try
        {
            var posted = await runs.PostAsync(
                request.Draft.ToDomain(),
                request.Employees.Select(e => e.ToDomain()).ToList(),
                request.CalculationHash,
                Environment.UserName,
                ct);

            return await BuildSummaryAsync(db, posted.Id, ct);
        }
        catch (PayRunPostingException ex)
        {
            // A blocked post is a business-rule rejection, not a server error.
            throw RpcException.BusinessRule(ex.Message);
        }
    }

    private async Task<object?> VoidAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<VoidPayRunRequest>(parameters);

        await using var db = _contextFactory();
        var (runs, _, _) = Services(db);

        try
        {
            await runs.VoidAsync(request.PayRunId, request.Reason, Environment.UserName, ct);
            return await BuildSummaryAsync(db, request.PayRunId, ct);
        }
        catch (PayRunPostingException ex)
        {
            throw RpcException.BusinessRule(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw RpcException.Validation(new Dictionary<string, string[]>
            {
                ["reason"] = new[] { ex.Message }
            });
        }
    }

    private async Task<object?> GetPayRunsAsync(CancellationToken ct)
    {
        await using var db = _contextFactory();

        var runs = await db.PayRuns
            .AsNoTracking()
            .OrderByDescending(r => r.PayDate)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct);

        // Aggregate every run's stub totals with a single stub query, grouped IN MEMORY.
        // SQLite stores decimals as TEXT and cannot SUM them server-side, so aggregation must
        // happen client-side after materialization. This is still two queries total (runs +
        // stubs) instead of the previous 2N+1.
        var aggregates = (await db.PayStubs.AsNoTracking().ToListAsync(ct))
            .GroupBy(s => s.PayRunId)
            .ToDictionary(
                g => g.Key,
                g => new StubAggregate(
                    Count: g.Count(),
                    Gross: g.Sum(s => s.GrossPay),
                    Net: g.Sum(s => s.NetPay),
                    EmployeeTaxes: g.Sum(s => s.TotalTaxes),
                    EmployerTaxes: g.Sum(s => s.TotalEmployerTaxes)));

        var summaries = runs
            .Select(run => MapSummary(run, aggregates.GetValueOrDefault(run.Id, StubAggregate.Empty)))
            .ToList();

        return new GetPayRunsResponse { PayRuns = summaries };
    }

    private async Task<object?> GetPayRunAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<GetPayRunRequest>(parameters);

        await using var db = _contextFactory();

        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.PayRunId, ct)
            ?? throw RpcException.NotFound($"No pay run with id {request.PayRunId}.");

        var stubs = await db.PayStubs
            .AsNoTracking()
            .Include(s => s.Employee)
            .Where(s => s.PayRunId == run.Id)
            .ToListAsync(ct);

        var stubLines = stubs
            .OrderBy(s => s.Employee!.LastName)
            .Select(s => new PayStubLineDto
            {
                PayStubId = s.Id,
                EmployeeId = s.EmployeeId,
                EmployeeName = s.Employee?.FullName ?? $"Employee {s.EmployeeId}",
                HoursWorked = s.HoursWorked,
                GrossPay = s.GrossPay,
                TotalTaxes = s.TotalTaxes,
                PostTaxDeductions = s.PostTaxDeductions,
                NetPay = s.NetPay,
                EmployerTaxes = s.TotalEmployerTaxes
            })
            .ToList();

        return new PayRunDetailDto
        {
            Summary = await BuildSummaryAsync(db, run.Id, ct),
            Stubs = stubLines
        };
    }

    /// <summary>Stub totals for one pay run.</summary>
    private readonly record struct StubAggregate(
        int Count, decimal Gross, decimal Net, decimal EmployeeTaxes, decimal EmployerTaxes)
    {
        public static readonly StubAggregate Empty = new(0, 0m, 0m, 0m, 0m);
    }

    /// <summary>Loads and aggregates a single run's stub totals. Used by post/void/detail.</summary>
    private static async Task<PayRunSummaryDto> BuildSummaryAsync(AppDbContext db, int payRunId, CancellationToken ct)
    {
        var run = await db.PayRuns.AsNoTracking().FirstAsync(r => r.Id == payRunId, ct);

        var stubs = await db.PayStubs
            .AsNoTracking()
            .Where(s => s.PayRunId == payRunId)
            .ToListAsync(ct);

        var aggregate = new StubAggregate(
            Count: stubs.Count,
            Gross: stubs.Sum(s => s.GrossPay),
            Net: stubs.Sum(s => s.NetPay),
            EmployeeTaxes: stubs.Sum(s => s.TotalTaxes),
            EmployerTaxes: stubs.Sum(s => s.TotalEmployerTaxes));

        return MapSummary(run, aggregate);
    }

    private static PayRunSummaryDto MapSummary(PayRun run, StubAggregate agg) => new()
    {
        Id = run.Id,
        PeriodStart = run.PeriodStart,
        PeriodEnd = run.PeriodEnd,
        PayDate = run.PayDate,
        Status = run.Status.ToString(),
        EmployeeCount = agg.Count,
        GrossPay = agg.Gross,
        NetPay = agg.Net,
        EmployeeTaxes = agg.EmployeeTaxes,
        EmployerTaxes = agg.EmployerTaxes,
        TotalEmployerCost = agg.Gross + agg.EmployerTaxes,
        PostedAtUtc = run.PostedAtUtc,
        VoidedAtUtc = run.VoidedAtUtc,
        VoidReason = run.VoidReason,
        EngineVersion = run.CalculationEngineVersion
    };
}
