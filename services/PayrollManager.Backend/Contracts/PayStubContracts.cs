using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Contracts;

/// <summary>An earning line as shown on the statement.</summary>
public sealed record PayStubEarningDto
{
    public string Description { get; init; } = string.Empty;
    public decimal Hours { get; init; }
    public decimal Rate { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>A deduction line as shown on the statement.</summary>
public sealed record PayStubDeductionDto
{
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public bool IsPreTax { get; init; }
}

/// <summary>One tax with its current-period and year-to-date amounts.</summary>
public sealed record PayStubTaxDto
{
    public string Label { get; init; } = string.Empty;
    public decimal Current { get; init; }
    public decimal Ytd { get; init; }
}

/// <summary>
/// The full pay stub statement for on-screen display.
///
/// The full SSN is never included - only the pre-masked <see cref="MaskedSsn"/> (e.g.
/// "XXX-XX-1234"), which is derived from the last four digits.
/// </summary>
public sealed record PayStubStatementDto
{
    // Identity
    public int PayStubId { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeCode { get; init; } = string.Empty;
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public string? MaskedSsn { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }

    // Company
    public string CompanyName { get; init; } = string.Empty;
    public string? CompanyAddress { get; init; }
    public string? CompanyTaxId { get; init; }

    // Period
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public DateTime PayDate { get; init; }
    public string PaySchedule { get; init; } = string.Empty;
    public string CheckNumber { get; init; } = string.Empty;

    // Lines
    public IReadOnlyList<PayStubEarningDto> Earnings { get; init; } = Array.Empty<PayStubEarningDto>();
    public IReadOnlyList<PayStubDeductionDto> Deductions { get; init; } = Array.Empty<PayStubDeductionDto>();

    /// <summary>Federal, Illinois, Social Security, Medicare - current and YTD each.</summary>
    public IReadOnlyList<PayStubTaxDto> Taxes { get; init; } = Array.Empty<PayStubTaxDto>();

    // Current-period summary
    public decimal GrossPay { get; init; }
    public decimal PreTaxDeductions { get; init; }
    public decimal TotalTaxes { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }

    // Year-to-date summary
    public decimal YtdGross { get; init; }
    public decimal YtdPreTaxDeductions { get; init; }
    public decimal YtdTotalTaxes { get; init; }
    public decimal YtdPostTaxDeductions { get; init; }
    public decimal YtdNet { get; init; }

    public static PayStubStatementDto From(PayStubStatement s)
    {
        var stub = s.PayStub;
        var ytd = s.Ytd;

        var addressLine2Parts = new[] { s.Employee.City, s.Employee.State }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var addressLine2 = string.Join(", ", addressLine2Parts);
        if (!string.IsNullOrWhiteSpace(s.Employee.PostalCode))
        {
            addressLine2 = string.IsNullOrEmpty(addressLine2)
                ? s.Employee.PostalCode
                : $"{addressLine2} {s.Employee.PostalCode}";
        }

        return new PayStubStatementDto
        {
            PayStubId = stub.Id,
            EmployeeId = s.Employee.Id,
            EmployeeName = s.Employee.FullName,
            EmployeeCode = s.Employee.EmployeeId,
            JobTitle = s.Employee.JobTitle,
            Department = s.Employee.Department,
            MaskedSsn = s.MaskedSsn,
            AddressLine1 = s.Employee.StreetAddress,
            AddressLine2 = string.IsNullOrEmpty(addressLine2) ? null : addressLine2,

            CompanyName = s.Company.CompanyName,
            CompanyAddress = s.Company.CompanyAddress,
            CompanyTaxId = s.Company.TaxId,

            PeriodStart = s.PayRun.PeriodStart,
            PeriodEnd = s.PayRun.PeriodEnd,
            PayDate = s.PayRun.PayDate,
            PaySchedule = s.PayScheduleLabel,
            CheckNumber = s.CheckNumber,

            Earnings = stub.EarningLines
                .OrderBy(e => e.Type)
                .Select(e => new PayStubEarningDto
                {
                    Description = e.Description,
                    Hours = e.Hours,
                    Rate = e.Rate,
                    Amount = e.Amount
                })
                .ToList(),

            Deductions = stub.DeductionLines
                .Select(d => new PayStubDeductionDto
                {
                    Description = d.Description,
                    Amount = d.Amount,
                    IsPreTax = d.IsPreTax
                })
                .ToList(),

            Taxes = new[]
            {
                new PayStubTaxDto { Label = "Federal Income Tax", Current = stub.TaxFederal, Ytd = ytd.FederalTax },
                new PayStubTaxDto { Label = "IL Income Tax", Current = stub.TaxState, Ytd = ytd.StateTax },
                new PayStubTaxDto { Label = "Social Security", Current = stub.TaxSocialSecurity, Ytd = ytd.SocialSecurity },
                new PayStubTaxDto { Label = "Medicare", Current = stub.TaxMedicare, Ytd = ytd.Medicare },
            },

            GrossPay = stub.GrossPay,
            // Pre-tax deductions include health (a §125 line), not just 401(k), so the stub's
            // PreTax401kDeduction scalar understates them. The exact total is the identity
            // gross - taxes - net - postTax (net = gross - preTax - taxes - postTax, always exact
            // because every component is already rounded to the cent).
            PreTaxDeductions = stub.GrossPay - stub.TotalTaxes - stub.PostTaxDeductions - stub.NetPay,
            TotalTaxes = stub.TotalTaxes,
            PostTaxDeductions = stub.PostTaxDeductions,
            NetPay = stub.NetPay,

            YtdGross = ytd.GrossPay,
            YtdPreTaxDeductions = ytd.GrossPay - ytd.TotalTaxes - ytd.PostTaxDeductions - ytd.NetPay,
            YtdTotalTaxes = ytd.TotalTaxes,
            YtdPostTaxDeductions = ytd.PostTaxDeductions,
            YtdNet = ytd.NetPay
        };
    }
}

public sealed record GetPayStubRequest
{
    public int PayStubId { get; init; }
}

public sealed record ExportPayStubPdfRequest
{
    public int PayStubId { get; init; }
    public string OutputPath { get; init; } = string.Empty;
}

public sealed record ExportPayStubPdfResponse
{
    public string Path { get; init; } = string.Empty;
}
