using System.ComponentModel.DataAnnotations;

namespace PayrollManager.Domain.Models;

public class Employee
{
    public int Id { get; set; }

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool IsHourly { get; set; }

    public decimal AnnualSalary { get; set; }

    public decimal HourlyRate { get; set; }

    public int DefaultHoursPerPeriod { get; set; } = 80;

    public decimal PreTax401kPercent { get; set; }

    public decimal HealthInsurancePerPeriod { get; set; }

    public decimal OtherDeductionsPerPeriod { get; set; }

    public string? JobTitle { get; set; }

    public string? Department { get; set; }

    public string? AvatarUrl { get; set; }

    /// <summary>Date the employee was hired. Previously collected in the UI but never persisted.</summary>
    public DateTime? HireDate { get; set; }

    /// <summary>Date employment ended, if it has. Terminated employees must not appear in new pay runs.</summary>
    public DateTime? TerminationDate { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // FEDERAL FORM W-4 (2020 and later)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// False until a real Form W-4 has been collected. An employer is required to hold a
    /// signed W-4 for every employee; until then these fields are defaults, not facts.
    /// </summary>
    public bool W4OnFile { get; set; }

    /// <summary>Step 1(c) filing status.</summary>
    public FilingStatus FilingStatus { get; set; } = FilingStatus.Single;

    /// <summary>Step 2(c) - multiple jobs / spouse works checkbox.</summary>
    public bool W4MultipleJobsChecked { get; set; }

    /// <summary>Step 3 - annual credit for dependents and other credits.</summary>
    public decimal W4DependentsAndOtherCredits { get; set; }

    /// <summary>Step 4(a) - other annual income not from jobs.</summary>
    public decimal W4OtherIncome { get; set; }

    /// <summary>Step 4(b) - annual deductions beyond the standard deduction.</summary>
    public decimal W4Deductions { get; set; }

    /// <summary>Step 4(c) - extra amount to withhold each pay period.</summary>
    public decimal W4ExtraWithholding { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // ILLINOIS FORM IL-W-4
    // ═══════════════════════════════════════════════════════════════

    /// <summary>IL-W-4 Line 1 - basic allowances (self, spouse, dependents).</summary>
    public int IlBasicAllowances { get; set; }

    /// <summary>IL-W-4 Line 2 - additional allowances (age 65 or older, legally blind).</summary>
    public int IlAdditionalAllowances { get; set; }

    public ICollection<PayStub> PayStubs { get; set; } = new List<PayStub>();

    public string FullName => $"{FirstName} {LastName}".Trim();

    public string EmployeeId => $"EMP{Id:D3}";

    public string DepartmentCode => Department?.Length > 3 ? Department[..3] : Department ?? "";
}
