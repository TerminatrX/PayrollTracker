namespace PayrollManager.Domain.Models;

public class PayRun
{
    public int Id { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime PayDate { get; set; }

    public ICollection<PayStub> PayStubs { get; set; } = new List<PayStub>();
}
