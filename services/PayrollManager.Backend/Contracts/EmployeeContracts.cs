using PayrollManager.Domain.Models;

namespace PayrollManager.Backend.Contracts;

/// <summary>
/// Employee as sent to the frontend.
///
/// Deliberately a DTO rather than the EF entity: the frontend must not receive navigation
/// collections (PayStubs) or be able to round-trip fields it has no business changing.
/// </summary>
public sealed record EmployeeDto
{
    public int Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string EmployeeCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsHourly { get; init; }
    public decimal AnnualSalary { get; init; }
    public decimal HourlyRate { get; init; }
    public int DefaultHoursPerPeriod { get; init; }
    public decimal PreTax401kPercent { get; init; }
    public decimal HealthInsurancePerPeriod { get; init; }
    public decimal OtherDeductionsPerPeriod { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public DateTime? HireDate { get; init; }
    public DateTime? TerminationDate { get; init; }

    public bool W4OnFile { get; init; }
    public FilingStatus FilingStatus { get; init; }
    public bool W4MultipleJobsChecked { get; init; }
    public decimal W4DependentsAndOtherCredits { get; init; }
    public decimal W4OtherIncome { get; init; }
    public decimal W4Deductions { get; init; }
    public decimal W4ExtraWithholding { get; init; }

    public int IlBasicAllowances { get; init; }
    public int IlAdditionalAllowances { get; init; }

    public static EmployeeDto From(Employee e) => new()
    {
        Id = e.Id,
        FirstName = e.FirstName,
        LastName = e.LastName,
        FullName = e.FullName,
        EmployeeCode = e.EmployeeId,
        IsActive = e.IsActive,
        IsHourly = e.IsHourly,
        AnnualSalary = e.AnnualSalary,
        HourlyRate = e.HourlyRate,
        DefaultHoursPerPeriod = e.DefaultHoursPerPeriod,
        PreTax401kPercent = e.PreTax401kPercent,
        HealthInsurancePerPeriod = e.HealthInsurancePerPeriod,
        OtherDeductionsPerPeriod = e.OtherDeductionsPerPeriod,
        JobTitle = e.JobTitle,
        Department = e.Department,
        HireDate = e.HireDate,
        TerminationDate = e.TerminationDate,
        W4OnFile = e.W4OnFile,
        FilingStatus = e.FilingStatus,
        W4MultipleJobsChecked = e.W4MultipleJobsChecked,
        W4DependentsAndOtherCredits = e.W4DependentsAndOtherCredits,
        W4OtherIncome = e.W4OtherIncome,
        W4Deductions = e.W4Deductions,
        W4ExtraWithholding = e.W4ExtraWithholding,
        IlBasicAllowances = e.IlBasicAllowances,
        IlAdditionalAllowances = e.IlAdditionalAllowances
    };
}

/// <summary>Fields the frontend may set when creating or updating an employee.</summary>
public sealed record EmployeeInput
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public bool IsHourly { get; init; }
    public decimal AnnualSalary { get; init; }
    public decimal HourlyRate { get; init; }
    public int DefaultHoursPerPeriod { get; init; } = 80;
    public decimal PreTax401kPercent { get; init; }
    public decimal HealthInsurancePerPeriod { get; init; }
    public decimal OtherDeductionsPerPeriod { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public DateTime? HireDate { get; init; }
    public DateTime? TerminationDate { get; init; }

    public bool W4OnFile { get; init; }
    public FilingStatus FilingStatus { get; init; } = FilingStatus.Single;
    public bool W4MultipleJobsChecked { get; init; }
    public decimal W4DependentsAndOtherCredits { get; init; }
    public decimal W4OtherIncome { get; init; }
    public decimal W4Deductions { get; init; }
    public decimal W4ExtraWithholding { get; init; }

    public int IlBasicAllowances { get; init; }
    public int IlAdditionalAllowances { get; init; }
}

public sealed record GetEmployeesRequest
{
    /// <summary>Include inactive employees. Defaults to active-only.</summary>
    public bool IncludeInactive { get; init; }

    /// <summary>Case-insensitive substring match on name, job title, or department.</summary>
    public string? Search { get; init; }
}

public sealed record GetEmployeeRequest
{
    public int EmployeeId { get; init; }
}

public sealed record CreateEmployeeRequest
{
    public EmployeeInput Employee { get; init; } = new();
}

public sealed record UpdateEmployeeRequest
{
    public int EmployeeId { get; init; }
    public EmployeeInput Employee { get; init; } = new();
}

public sealed record HealthResponse
{
    public string Status { get; init; } = "ok";
    public string EngineVersion { get; init; } = string.Empty;
    public string DatabasePath { get; init; } = string.Empty;
    public bool MigratedFromLegacyLocation { get; init; }
    public string? BackupPath { get; init; }
    public IReadOnlyList<string> AppliedMigrations { get; init; } = Array.Empty<string>();
}
