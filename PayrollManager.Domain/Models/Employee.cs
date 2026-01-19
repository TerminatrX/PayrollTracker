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

    public decimal PreTax401kPercent { get; set; }

    public decimal HealthInsurancePerPeriod { get; set; }

    public decimal OtherDeductionsPerPeriod { get; set; }

    public ICollection<PayStub> PayStubs { get; set; } = new List<PayStub>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
