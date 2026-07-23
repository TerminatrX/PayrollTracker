using PayrollManager.Backend.Contracts;

namespace PayrollManager.Backend.Validation;

/// <summary>
/// Server-side validation for employee input.
///
/// The frontend validates too (for immediate feedback), but this is the authority - a request
/// can reach the sidecar from anywhere, and payroll inputs decide what people are paid.
/// </summary>
public static class EmployeeValidator
{
    public const decimal MaxHourlyRate = 10_000m;
    public const decimal MaxAnnualSalary = 100_000_000m;

    public static Dictionary<string, string[]> Validate(EmployeeInput input)
    {
        var errors = new Dictionary<string, List<string>>();

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                list = new List<string>();
                errors[field] = list;
            }

            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(input.FirstName))
        {
            Add(nameof(input.FirstName), "First name is required.");
        }
        else if (input.FirstName.Trim().Length > 100)
        {
            Add(nameof(input.FirstName), "First name cannot exceed 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(input.LastName))
        {
            Add(nameof(input.LastName), "Last name is required.");
        }
        else if (input.LastName.Trim().Length > 100)
        {
            Add(nameof(input.LastName), "Last name cannot exceed 100 characters.");
        }

        // Compensation must match the pay type, or the pay run silently computes zero gross.
        if (input.IsHourly)
        {
            if (input.HourlyRate <= 0m)
            {
                Add(nameof(input.HourlyRate), "An hourly employee needs an hourly rate above zero.");
            }
            else if (input.HourlyRate > MaxHourlyRate)
            {
                Add(nameof(input.HourlyRate), $"Hourly rate cannot exceed {MaxHourlyRate:C}.");
            }
        }
        else
        {
            if (input.AnnualSalary <= 0m)
            {
                Add(nameof(input.AnnualSalary), "A salaried employee needs an annual salary above zero.");
            }
            else if (input.AnnualSalary > MaxAnnualSalary)
            {
                Add(nameof(input.AnnualSalary), $"Annual salary cannot exceed {MaxAnnualSalary:C}.");
            }
        }

        if (input.DefaultHoursPerPeriod is < 0 or > 400)
        {
            Add(nameof(input.DefaultHoursPerPeriod), "Default hours per period must be between 0 and 400.");
        }

        if (input.PreTax401kPercent is < 0m or > 100m)
        {
            Add(nameof(input.PreTax401kPercent), "401(k) percentage must be between 0 and 100.");
        }

        if (input.HealthInsurancePerPeriod < 0m)
        {
            Add(nameof(input.HealthInsurancePerPeriod), "Health insurance cannot be negative.");
        }

        if (input.OtherDeductionsPerPeriod < 0m)
        {
            Add(nameof(input.OtherDeductionsPerPeriod), "Other deductions cannot be negative.");
        }

        // W-4 amounts are annual dollar figures and are never negative on the form.
        if (input.W4DependentsAndOtherCredits < 0m)
        {
            Add(nameof(input.W4DependentsAndOtherCredits), "W-4 Step 3 credits cannot be negative.");
        }

        if (input.W4OtherIncome < 0m)
        {
            Add(nameof(input.W4OtherIncome), "W-4 Step 4(a) other income cannot be negative.");
        }

        if (input.W4Deductions < 0m)
        {
            Add(nameof(input.W4Deductions), "W-4 Step 4(b) deductions cannot be negative.");
        }

        if (input.W4ExtraWithholding < 0m)
        {
            Add(nameof(input.W4ExtraWithholding),
                "W-4 Step 4(c) extra withholding cannot be negative - withholding can only be added.");
        }

        if (input.IlBasicAllowances < 0)
        {
            Add(nameof(input.IlBasicAllowances), "IL-W-4 basic allowances cannot be negative.");
        }

        if (input.IlAdditionalAllowances < 0)
        {
            Add(nameof(input.IlAdditionalAllowances), "IL-W-4 additional allowances cannot be negative.");
        }

        if (input.HireDate is { } hired && input.TerminationDate is { } terminated && terminated < hired)
        {
            Add(nameof(input.TerminationDate), "Termination date cannot precede the hire date.");
        }

        // An active employee with a past termination date would be offered up for pay runs.
        if (input.IsActive && input.TerminationDate is { } termination && termination < DateTime.Today)
        {
            Add(nameof(input.IsActive),
                "An employee with a past termination date cannot be marked active.");
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }
}
