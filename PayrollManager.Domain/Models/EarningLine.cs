namespace PayrollManager.Domain.Models;

public enum EarningType
{
    Regular,
    Overtime,
    Bonus,
    Commission
}

public class EarningLine
{
    public int Id { get; set; }

    public int PayStubId { get; set; }

    public PayStub? PayStub { get; set; }

    public EarningType Type { get; set; }

    public decimal Hours { get; set; }

    public decimal Rate { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;
}
