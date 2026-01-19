namespace PayrollManager.Domain.Models;

public class PayStub
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public int PayRunId { get; set; }

    public PayRun? PayRun { get; set; }

    public decimal HoursWorked { get; set; }

    /// <summary>
    /// Stored gross pay value. When EarningLines are present, this should equal their sum.
    /// </summary>
    public decimal GrossPay { get; set; }

    public decimal PreTax401kDeduction { get; set; }

    public decimal TaxFederal { get; set; }

    public decimal TaxState { get; set; }

    public decimal TaxSocialSecurity { get; set; }

    public decimal TaxMedicare { get; set; }

    public decimal PostTaxDeductions { get; set; }

    public decimal NetPay { get; set; }

    public decimal YtdGross { get; set; }

    public decimal YtdNet { get; set; }

    public decimal TotalTaxes => TaxFederal + TaxState + TaxSocialSecurity + TaxMedicare;

    /// <summary>
    /// Collection of earning lines (Regular, Overtime, Bonus, Commission)
    /// </summary>
    public ICollection<EarningLine> EarningLines { get; set; } = new List<EarningLine>();

    /// <summary>
    /// Computed gross pay from earning lines. Use this when EarningLines are loaded.
    /// </summary>
    public decimal ComputedGrossPay => EarningLines.Count > 0 
        ? EarningLines.Sum(e => e.Amount) 
        : GrossPay;

    // Convenience properties for earnings breakdown
    public decimal RegularEarnings => EarningLines.Where(e => e.Type == EarningType.Regular).Sum(e => e.Amount);
    public decimal OvertimeEarnings => EarningLines.Where(e => e.Type == EarningType.Overtime).Sum(e => e.Amount);
    public decimal BonusEarnings => EarningLines.Where(e => e.Type == EarningType.Bonus).Sum(e => e.Amount);
    public decimal CommissionEarnings => EarningLines.Where(e => e.Type == EarningType.Commission).Sum(e => e.Amount);

    public decimal RegularHours => EarningLines.Where(e => e.Type == EarningType.Regular).Sum(e => e.Hours);
    public decimal OvertimeHours => EarningLines.Where(e => e.Type == EarningType.Overtime).Sum(e => e.Hours);
}
