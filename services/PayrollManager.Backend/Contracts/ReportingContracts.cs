using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Contracts;

/// <summary>Company-wide totals for a period, plus employer cost (which CompanyTotals omits).</summary>
public sealed record CompanyTotalsDto
{
    public int EmployeeCount { get; init; }
    public int PayStubCount { get; init; }
    public decimal GrossPay { get; init; }
    public decimal FederalTax { get; init; }
    public decimal StateTax { get; init; }
    public decimal SocialSecurity { get; init; }
    public decimal Medicare { get; init; }
    public decimal TotalTaxes { get; init; }
    public decimal PreTax401k { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }
    public decimal EmployerTaxes { get; init; }
    public decimal TotalEmployerCost { get; init; }

    public static CompanyTotalsDto From(CompanyTotals t, decimal employerTaxes) => new()
    {
        EmployeeCount = t.EmployeeCount,
        PayStubCount = t.PayStubCount,
        GrossPay = t.GrossPay,
        FederalTax = t.FederalTax,
        StateTax = t.StateTax,
        SocialSecurity = t.SocialSecurity,
        Medicare = t.Medicare,
        TotalTaxes = t.TotalTaxes,
        PreTax401k = t.PreTax401k,
        PostTaxDeductions = t.PostTaxDeductions,
        NetPay = t.NetPay,
        EmployerTaxes = employerTaxes,
        TotalEmployerCost = t.GrossPay + employerTaxes
    };
}

public sealed record EmployeeTotalsDto
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public decimal GrossPay { get; init; }
    public decimal TotalTaxes { get; init; }
    public decimal PreTax401k { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }
    public int PayStubCount { get; init; }

    public static EmployeeTotalsDto From(EmployeeTotals t) => new()
    {
        EmployeeId = t.EmployeeId,
        EmployeeName = t.EmployeeName,
        GrossPay = t.GrossPay,
        TotalTaxes = t.TotalTaxes,
        PreTax401k = t.PreTax401k,
        PostTaxDeductions = t.PostTaxDeductions,
        NetPay = t.NetPay,
        PayStubCount = t.PayStubCount
    };
}

public sealed record DashboardDto
{
    public int Year { get; init; }
    public CompanyTotalsDto CompanyYtd { get; init; } = new();
    public int ActiveEmployeeCount { get; init; }
    public int PostedRunCount { get; init; }
    public PayRunSummaryDto? LastPayRun { get; init; }
    public DateTime? NextPayDate { get; init; }
    public bool SuiConfigured { get; init; }
}

public sealed record PayrollReportRequest
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
}

public sealed record PayrollReportDto
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public CompanyTotalsDto Company { get; init; } = new();
    public IReadOnlyList<EmployeeTotalsDto> Employees { get; init; } = Array.Empty<EmployeeTotalsDto>();
}

public sealed record ExportPayrollReportRequest
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }

    /// <summary>Where to write the CSV, chosen by the frontend's native save dialog.</summary>
    public string OutputPath { get; init; } = string.Empty;
}

public sealed record ExportReportResponse
{
    public string Path { get; init; } = string.Empty;
}

/* ── Audit log ───────────────────────────────────────────────────────────── */

public sealed record AuditEntryDto
{
    public int Id { get; init; }
    public DateTime TimestampUtc { get; init; }
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public int? EntityId { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? PerformedBy { get; init; }
    public string? Notes { get; init; }

    public static AuditEntryDto From(AuditLogEntry e) => new()
    {
        Id = e.Id,
        TimestampUtc = e.TimestampUtc,
        Action = e.Action.ToString(),
        EntityType = e.EntityType,
        EntityId = e.EntityId,
        OldValue = e.OldValue,
        NewValue = e.NewValue,
        PerformedBy = e.PerformedBy,
        Notes = e.Notes
    };
}

public sealed record GetAuditLogRequest
{
    /// <summary>Maximum entries to return, newest first. Defaults to 200.</summary>
    public int Limit { get; init; } = 200;
}

public sealed record GetAuditLogResponse
{
    public IReadOnlyList<AuditEntryDto> Entries { get; init; } = Array.Empty<AuditEntryDto>();
}
