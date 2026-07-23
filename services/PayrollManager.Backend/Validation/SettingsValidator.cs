using PayrollManager.Backend.Contracts;

namespace PayrollManager.Backend.Validation;

/// <summary>
/// Server-side validation for company settings. The backend is the authority; the frontend
/// mirrors these for immediate feedback.
/// </summary>
public static class SettingsValidator
{
    // Pay frequencies the pay-period calculator understands. Anything else silently defaults
    // to bi-weekly, so reject it rather than let a typo change everyone's pay.
    private static readonly int[] SupportedPayPeriods = { 52, 26, 24, 12 };

    public static Dictionary<string, string[]> Validate(CompanySettingsInput input)
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

        if (string.IsNullOrWhiteSpace(input.CompanyName))
        {
            Add(nameof(input.CompanyName), "Company name is required.");
        }
        else if (input.CompanyName.Trim().Length > 200)
        {
            Add(nameof(input.CompanyName), "Company name cannot exceed 200 characters.");
        }

        if (!SupportedPayPeriods.Contains(input.PayPeriodsPerYear))
        {
            Add(nameof(input.PayPeriodsPerYear),
                "Pay periods per year must be 52 (weekly), 26 (bi-weekly), 24 (semi-monthly), or 12 (monthly).");
        }

        if (input.DefaultHoursPerPeriod is < 0 or > 400)
        {
            Add(nameof(input.DefaultHoursPerPeriod), "Default hours per period must be between 0 and 400.");
        }

        // FICA rates are effectively fixed by statute, but remain configurable for edge cases;
        // guard against a value that is obviously a mistake (e.g. 62 instead of 6.2).
        if (input.SocialSecurityPercent is < 0m or > 20m)
        {
            Add(nameof(input.SocialSecurityPercent), "Social Security rate must be between 0 and 20 percent.");
        }

        if (input.MedicarePercent is < 0m or > 20m)
        {
            Add(nameof(input.MedicarePercent), "Medicare rate must be between 0 and 20 percent.");
        }

        // SUI is optional (0 = not yet configured), but if provided it must be sane.
        if (input.SuiRatePercent is < 0m or > 15m)
        {
            Add(nameof(input.SuiRatePercent), "SUI rate must be between 0 and 15 percent.");
        }

        if (input.SuiWageBase < 0m)
        {
            Add(nameof(input.SuiWageBase), "SUI wage base cannot be negative.");
        }

        // A rate without a base (or vice versa) computes zero SUI silently; flag the mismatch.
        if (input.SuiRatePercent > 0m && input.SuiWageBase <= 0m)
        {
            Add(nameof(input.SuiWageBase),
                "A SUI wage base is required when a SUI rate is set, or no SUI is withheld.");
        }

        if (input.SuiWageBase > 0m && input.SuiRatePercent <= 0m)
        {
            Add(nameof(input.SuiRatePercent),
                "A SUI rate is required when a SUI wage base is set, or no SUI is withheld.");
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }
}
