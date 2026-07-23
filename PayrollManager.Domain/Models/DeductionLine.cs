namespace PayrollManager.Domain.Models;

public enum DeductionType
{
    PreTax401k,
    HealthInsurance,
    DentalInsurance,
    VisionInsurance,
    LifeInsurance,
    OtherPreTax,
    OtherPostTax
}

public static class DeductionTypeExtensions
{
    /// <summary>
    /// True when this deduction reduces FICA (Social Security and Medicare) wages.
    ///
    /// The distinction matters and is easy to get wrong:
    ///   - §125 cafeteria-plan premiums (health/dental/vision) are exempt from federal income
    ///     tax AND from FICA.
    ///   - 401(k) elective deferrals are exempt from federal income tax ONLY. They remain
    ///     fully subject to Social Security and Medicare.
    ///
    /// Where a category is ambiguous we return false, which over-withholds rather than
    /// under-withholds. Under-withholding creates an employee liability at filing time and is
    /// the more damaging error.
    /// </summary>
    public static bool ReducesFicaWages(this DeductionType type) => type switch
    {
        DeductionType.HealthInsurance => true,
        DeductionType.DentalInsurance => true,
        DeductionType.VisionInsurance => true,

        // Pre-tax for income tax only - explicitly still FICA-taxable.
        DeductionType.PreTax401k => false,

        // Group-term life over $50,000 creates imputed income that is itself FICA-taxable,
        // so this cannot be treated as a simple FICA-wage reduction. Revisit with explicit
        // imputed-income handling before changing.
        DeductionType.LifeInsurance => false,

        // Unclassified pre-tax items: conservative default.
        DeductionType.OtherPreTax => false,

        DeductionType.OtherPostTax => false,
        _ => false
    };
}

public class DeductionLine
{
    public int Id { get; set; }

    public int PayStubId { get; set; }

    public PayStub? PayStub { get; set; }

    public DeductionType Type { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsPreTax { get; set; }
}
