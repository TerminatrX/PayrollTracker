namespace PayrollManager.Domain.Models;

public class PayStub
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public Employee? Employee { get; set; }

    public int PayRunId { get; set; }

    public PayRun? PayRun { get; set; }

    public decimal HoursWorked { get; set; }

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
}
