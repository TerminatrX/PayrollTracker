namespace PayrollManager.Domain.Models;

public enum TaxType
{
    FederalIncome,
    StateIncome,
    SocialSecurity,
    Medicare,
    FUTA,
    SUI,
    Local
}

public class TaxLine
{
    public int Id { get; set; }

    public int PayStubId { get; set; }

    public PayStub? PayStub { get; set; }

    public TaxType Type { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Rate { get; set; }

    public decimal TaxableAmount { get; set; }
}
