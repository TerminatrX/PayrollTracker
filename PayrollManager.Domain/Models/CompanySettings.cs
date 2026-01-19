namespace PayrollManager.Domain.Models;

public class CompanySettings
{
    public int Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string CompanyAddress { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public decimal FederalTaxPercent { get; set; } = 12m;

    public decimal StateTaxPercent { get; set; } = 5m;

    public decimal SocialSecurityPercent { get; set; } = 6.2m;

    public decimal MedicarePercent { get; set; } = 1.45m;

    public int PayPeriodsPerYear { get; set; } = 26;

    public int DefaultHoursPerPeriod { get; set; } = 80;
}
