using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Contracts;

/// <summary>
/// Company settings as sent to the frontend.
///
/// Only fields the payroll engine actually uses are exposed. Federal and Illinois income tax
/// are statutory (Pub 15-T / IL-700-T) and NOT configurable, so there is deliberately no
/// federal/state rate here.
/// </summary>
public sealed record CompanySettingsDto
{
    public string CompanyName { get; init; } = string.Empty;
    public string CompanyAddress { get; init; } = string.Empty;
    public string TaxId { get; init; } = string.Empty;

    public int PayPeriodsPerYear { get; init; }
    public int DefaultHoursPerPeriod { get; init; }

    /// <summary>Human-readable frequency derived from <see cref="PayPeriodsPerYear"/>.</summary>
    public string PayFrequencyLabel { get; init; } = string.Empty;

    public decimal SocialSecurityPercent { get; init; }
    public decimal MedicarePercent { get; init; }

    // Employer unemployment - from the annual IDES rate notice, no defensible default.
    public decimal SuiRatePercent { get; init; }
    public decimal SuiWageBase { get; init; }
    public bool ReceivesFullFutaCredit { get; init; }

    /// <summary>True until SUI is configured; the UI surfaces this because it understates cost.</summary>
    public bool SuiConfigured { get; init; }

    public static CompanySettingsDto From(CompanySettings s) => new()
    {
        CompanyName = s.CompanyName,
        CompanyAddress = s.CompanyAddress,
        TaxId = s.TaxId,
        PayPeriodsPerYear = s.PayPeriodsPerYear,
        DefaultHoursPerPeriod = s.DefaultHoursPerPeriod,
        PayFrequencyLabel = DescribeFrequency(s.PayPeriodsPerYear),
        SocialSecurityPercent = s.SocialSecurityPercent,
        MedicarePercent = s.MedicarePercent,
        SuiRatePercent = s.SuiRatePercent,
        SuiWageBase = s.SuiWageBase,
        ReceivesFullFutaCredit = s.ReceivesFullFutaCredit,
        SuiConfigured = s.SuiRatePercent > 0m && s.SuiWageBase > 0m
    };

    private static string DescribeFrequency(int payPeriodsPerYear) => payPeriodsPerYear switch
    {
        52 => "Weekly",
        26 => "Bi-weekly",
        24 => "Semi-monthly",
        12 => "Monthly",
        _ => $"{payPeriodsPerYear} periods/year"
    };
}

/// <summary>Fields the frontend may set on company settings.</summary>
public sealed record CompanySettingsInput
{
    public string CompanyName { get; init; } = string.Empty;
    public string CompanyAddress { get; init; } = string.Empty;
    public string TaxId { get; init; } = string.Empty;
    public int PayPeriodsPerYear { get; init; } = 26;
    public int DefaultHoursPerPeriod { get; init; } = 80;
    public decimal SocialSecurityPercent { get; init; } = 6.2m;
    public decimal MedicarePercent { get; init; } = 1.45m;
    public decimal SuiRatePercent { get; init; }
    public decimal SuiWageBase { get; init; }
    public bool ReceivesFullFutaCredit { get; init; } = true;
}

public sealed record UpdateCompanySettingsRequest
{
    public CompanySettingsInput Settings { get; init; } = new();
}

public sealed record BackupDto
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime CreatedUtc { get; init; }
}

public sealed record CreateBackupResponse
{
    public BackupDto Backup { get; init; } = new();
}

public sealed record ListBackupsResponse
{
    public IReadOnlyList<BackupDto> Backups { get; init; } = Array.Empty<BackupDto>();
}
