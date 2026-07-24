using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

/// <summary>
/// A fully-assembled pay stub: the stub with its lines, the employee, the pay run, the company
/// settings, and the year-to-date totals through this stub's pay date.
///
/// This is the SINGLE SOURCE OF TRUTH for a pay stub. Both the on-screen statement (sidecar DTO)
/// and the printable PDF are built from one of these, so they can never disagree.
/// </summary>
public sealed class PayStubStatement
{
    public required PayStub PayStub { get; init; }
    public required Employee Employee { get; init; }
    public required PayRun PayRun { get; init; }
    public required CompanySettings Company { get; init; }

    /// <summary>YTD figures (per-tax, gross, net, deductions) through this stub's pay date.</summary>
    public required EmployeeTotals Ytd { get; init; }

    /// <summary>Masked SSN for display, e.g. "XXX-XX-1234", or null when none is on file.</summary>
    public string? MaskedSsn =>
        string.IsNullOrEmpty(Employee.SsnLast4) ? null : $"XXX-XX-{Employee.SsnLast4}";

    /// <summary>Human-readable pay frequency from the company's periods-per-year.</summary>
    public string PayScheduleLabel => Company.PayPeriodsPerYear switch
    {
        52 => "Weekly",
        26 => "Bi-Weekly",
        24 => "Semi-Monthly",
        12 => "Monthly",
        _ => $"{Company.PayPeriodsPerYear} periods/year"
    };

    /// <summary>Stable per-stub reference number for the statement / deposit advice.</summary>
    public string CheckNumber => PayStub.Id.ToString("D4");
}

/// <summary>
/// Builds <see cref="PayStubStatement"/>s. Only posted pay stubs are valid statements - a stub
/// on a draft or voided run is not a pay stub to hand an employee.
/// </summary>
public sealed class PayStubStatementService
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettingsService _companySettingsService;
    private readonly AggregationService _aggregationService;

    public PayStubStatementService(
        AppDbContext dbContext,
        CompanySettingsService companySettingsService,
        AggregationService aggregationService)
    {
        _dbContext = dbContext;
        _companySettingsService = companySettingsService;
        _aggregationService = aggregationService;
    }

    public async Task<PayStubStatement> BuildAsync(int payStubId)
    {
        var stub = await _dbContext.PayStubs
            .Include(ps => ps.Employee)
            .Include(ps => ps.PayRun)
            .Include(ps => ps.EarningLines)
            .Include(ps => ps.DeductionLines)
            .Include(ps => ps.TaxLines)
            .FirstOrDefaultAsync(ps => ps.Id == payStubId)
            ?? throw new PayStubNotFoundException(payStubId);

        if (stub.Employee is null || stub.PayRun is null)
        {
            throw new InvalidOperationException(
                $"Pay stub {payStubId} is missing its employee or pay run.");
        }

        if (stub.PayRun.Status != PayRunStatus.Posted)
        {
            throw new PayStubNotPostedException(payStubId, stub.PayRun.Status);
        }

        var company = await _companySettingsService.GetSettingsAsync();
        var ytd = await _aggregationService.GetEmployeeYtdThroughAsync(
            stub.EmployeeId, stub.PayRun.PayDate);

        return new PayStubStatement
        {
            PayStub = stub,
            Employee = stub.Employee,
            PayRun = stub.PayRun,
            Company = company,
            Ytd = ytd
        };
    }
}

public sealed class PayStubNotFoundException : Exception
{
    public int PayStubId { get; }

    public PayStubNotFoundException(int payStubId)
        : base($"No pay stub with id {payStubId}.")
    {
        PayStubId = payStubId;
    }
}

public sealed class PayStubNotPostedException : InvalidOperationException
{
    public int PayStubId { get; }
    public PayRunStatus Status { get; }

    public PayStubNotPostedException(int payStubId, PayRunStatus status)
        : base($"Pay stub {payStubId} belongs to a {status} run. Only posted pay runs produce " +
               "a pay stub that can be viewed, printed, or exported.")
    {
        PayStubId = payStubId;
        Status = status;
    }
}
