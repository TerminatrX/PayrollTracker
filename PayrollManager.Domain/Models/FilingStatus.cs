namespace PayrollManager.Domain.Models;

/// <summary>
/// Federal filing status as reported on Form W-4, Step 1(c).
///
/// Married Filing Separately shares the Single withholding schedule, which is why the
/// published Pub 15-T table is titled "Single or Married Filing Separately".
/// </summary>
public enum FilingStatus
{
    Single = 0,
    MarriedFilingJointly = 1,
    MarriedFilingSeparately = 2,
    HeadOfHousehold = 3
}
