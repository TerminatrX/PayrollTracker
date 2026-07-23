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

    public decimal YtdTaxes { get; set; }

    /// <summary>
    /// Total tax WITHHELD FROM THE EMPLOYEE. Deliberately excludes every employer-paid tax
    /// below - those are a company cost, not a deduction from this person's pay.
    /// </summary>
    public decimal TotalTaxes => TaxFederal + TaxState + TaxSocialSecurity + TaxMedicare;

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYER-PAID TAXES
    //
    // Stored as scalars rather than TaxLines on purpose: TaxLines represent amounts withheld
    // from the employee, and TotalTaxes ties out to their sum. Mixing employer taxes in would
    // overstate withholding and understate net pay.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Employer's matching Social Security contribution.</summary>
    public decimal EmployerSocialSecurity { get; set; }

    /// <summary>Employer's matching Medicare contribution (no match on Additional Medicare).</summary>
    public decimal EmployerMedicare { get; set; }

    /// <summary>Federal unemployment tax paid by the employer.</summary>
    public decimal EmployerFuta { get; set; }

    /// <summary>State unemployment tax paid by the employer.</summary>
    public decimal EmployerSui { get; set; }

    /// <summary>Total employer-paid payroll tax for this stub.</summary>
    public decimal TotalEmployerTaxes =>
        EmployerSocialSecurity + EmployerMedicare + EmployerFuta + EmployerSui;

    /// <summary>Full cost to the employer: gross pay plus employer-paid taxes.</summary>
    public decimal TotalEmployerCost => GrossPay + TotalEmployerTaxes;

    /// <summary>
    /// Collection of earning lines (Regular, Overtime, Bonus, Commission)
    /// </summary>
    public ICollection<EarningLine> EarningLines { get; set; } = new List<EarningLine>();

    /// <summary>
    /// Collection of deduction lines (401k, Health Insurance, etc.)
    /// </summary>
    public ICollection<DeductionLine> DeductionLines { get; set; } = new List<DeductionLine>();

    /// <summary>
    /// Collection of tax lines (Federal, State, Social Security, Medicare, etc.)
    /// </summary>
    public ICollection<TaxLine> TaxLines { get; set; } = new List<TaxLine>();

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
