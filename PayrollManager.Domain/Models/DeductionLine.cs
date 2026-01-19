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
