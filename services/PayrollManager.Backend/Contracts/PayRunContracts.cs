using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Contracts;

/// <summary>Pay period dates for a draft run.</summary>
public sealed record PayRunDraftInput
{
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public DateTime PayDate { get; init; }

    public PayRunDraft ToDomain() => new()
    {
        PeriodStart = PeriodStart,
        PeriodEnd = PeriodEnd,
        PayDate = PayDate
    };
}

/// <summary>Per-employee hours and extra earnings for a run.</summary>
public sealed record PayRunEmployeeInputDto
{
    public int EmployeeId { get; init; }
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal BonusAmount { get; init; }
    public decimal CommissionAmount { get; init; }
    public string? BonusDescription { get; init; }
    public string? CommissionDescription { get; init; }

    public PayRunEmployeeInput ToDomain() => new()
    {
        EmployeeId = EmployeeId,
        RegularHours = RegularHours,
        OvertimeHours = OvertimeHours,
        BonusAmount = BonusAmount,
        CommissionAmount = CommissionAmount,
        BonusDescription = BonusDescription,
        CommissionDescription = CommissionDescription
    };
}

public sealed record PreviewPayRunRequest
{
    public PayRunDraftInput Draft { get; init; } = new();
    public IReadOnlyList<PayRunEmployeeInputDto> Employees { get; init; } = Array.Empty<PayRunEmployeeInputDto>();
}

public sealed record PostPayRunRequest
{
    public PayRunDraftInput Draft { get; init; } = new();
    public IReadOnlyList<PayRunEmployeeInputDto> Employees { get; init; } = Array.Empty<PayRunEmployeeInputDto>();

    /// <summary>The hash from the reviewed preview; posting is refused if recomputation differs.</summary>
    public string CalculationHash { get; init; } = string.Empty;
}

public sealed record VoidPayRunRequest
{
    public int PayRunId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record GetPayRunRequest
{
    public int PayRunId { get; init; }
}

/* ── Preview / line DTOs ─────────────────────────────────────────────────── */

public sealed record PayRunWarningDto
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool BlocksPosting { get; init; }
    public int? EmployeeId { get; init; }

    public static PayRunWarningDto From(PayRunWarning w) => new()
    {
        Code = w.Code.ToString(),
        Message = w.Message,
        BlocksPosting = w.BlocksPosting,
        EmployeeId = w.EmployeeId
    };
}

public sealed record PayRunLineDto
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public decimal HoursWorked { get; init; }
    public decimal GrossPay { get; init; }
    public decimal PreTaxDeductions { get; init; }
    public decimal TotalTaxes { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }
    public decimal EmployerTaxes { get; init; }

    public static PayRunLineDto From(PayRunLine l) => new()
    {
        EmployeeId = l.EmployeeId,
        EmployeeName = l.EmployeeName,
        HoursWorked = l.HoursWorked,
        GrossPay = l.GrossPay,
        PreTaxDeductions = l.PreTaxDeductions,
        TotalTaxes = l.TotalTaxes,
        PostTaxDeductions = l.PostTaxDeductions,
        NetPay = l.NetPay,
        EmployerTaxes = l.EmployerTaxes
    };
}

public sealed record PayRunTotalsDto
{
    public int EmployeeCount { get; init; }
    public decimal GrossPay { get; init; }
    public decimal PreTaxDeductions { get; init; }
    public decimal EmployeeTaxes { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }
    public decimal EmployerTaxes { get; init; }
    public decimal TotalEmployerCost { get; init; }

    public static PayRunTotalsDto From(PayRunTotals t) => new()
    {
        EmployeeCount = t.EmployeeCount,
        GrossPay = t.GrossPay,
        PreTaxDeductions = t.PreTaxDeductions,
        EmployeeTaxes = t.EmployeeTaxes,
        PostTaxDeductions = t.PostTaxDeductions,
        NetPay = t.NetPay,
        EmployerTaxes = t.EmployerTaxes,
        TotalEmployerCost = t.TotalEmployerCost
    };
}

public sealed record PayRunPreviewDto
{
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public DateTime PayDate { get; init; }
    public IReadOnlyList<PayRunLineDto> Lines { get; init; } = Array.Empty<PayRunLineDto>();
    public PayRunTotalsDto Totals { get; init; } = new();
    public IReadOnlyList<PayRunWarningDto> Warnings { get; init; } = Array.Empty<PayRunWarningDto>();
    public string CalculationHash { get; init; } = string.Empty;
    public bool CanPost { get; init; }

    public static PayRunPreviewDto From(PayRunPreview p) => new()
    {
        PeriodStart = p.PeriodStart,
        PeriodEnd = p.PeriodEnd,
        PayDate = p.PayDate,
        Lines = p.Lines.Select(PayRunLineDto.From).ToList(),
        Totals = PayRunTotalsDto.From(p.Totals),
        Warnings = p.Warnings.Select(PayRunWarningDto.From).ToList(),
        CalculationHash = p.CalculationHash,
        CanPost = p.CanPost
    };
}

/* ── Wizard bootstrap ────────────────────────────────────────────────────── */

public sealed record PayRunEmployeeSuggestion
{
    public int EmployeeId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public bool IsHourly { get; init; }
    public decimal HourlyRate { get; init; }
    public decimal AnnualSalary { get; init; }
    public decimal SalaryPerPeriod { get; init; }
    public decimal SuggestedRegularHours { get; init; }
    public decimal SuggestedOvertimeHours { get; init; }
    public bool W4OnFile { get; init; }
}

public sealed record SuggestPayRunResponse
{
    public PayRunDraftInput Draft { get; init; } = new();
    public IReadOnlyList<PayRunEmployeeSuggestion> Employees { get; init; } = Array.Empty<PayRunEmployeeSuggestion>();
}

/* ── History ─────────────────────────────────────────────────────────────── */

public sealed record PayRunSummaryDto
{
    public int Id { get; init; }
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public DateTime PayDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public int EmployeeCount { get; init; }
    public decimal GrossPay { get; init; }
    public decimal NetPay { get; init; }
    public decimal EmployeeTaxes { get; init; }
    public decimal EmployerTaxes { get; init; }
    public decimal TotalEmployerCost { get; init; }
    public DateTime? PostedAtUtc { get; init; }
    public DateTime? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
    public string? EngineVersion { get; init; }
}

public sealed record GetPayRunsResponse
{
    public IReadOnlyList<PayRunSummaryDto> PayRuns { get; init; } = Array.Empty<PayRunSummaryDto>();
}

public sealed record PayStubLineDto
{
    public int PayStubId { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public decimal HoursWorked { get; init; }
    public decimal GrossPay { get; init; }
    public decimal TotalTaxes { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }
    public decimal EmployerTaxes { get; init; }
}

public sealed record PayRunDetailDto
{
    public PayRunSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<PayStubLineDto> Stubs { get; init; } = Array.Empty<PayStubLineDto>();
}
