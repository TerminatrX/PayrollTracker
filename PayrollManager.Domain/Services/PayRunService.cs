using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

/// <summary>Hours and extra earnings for one employee in a pay run.</summary>
public sealed record PayRunEmployeeInput
{
    public required int EmployeeId { get; init; }
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal BonusAmount { get; init; }
    public decimal CommissionAmount { get; init; }
    public string? BonusDescription { get; init; }
    public string? CommissionDescription { get; init; }
}

public enum PayRunWarningCode
{
    ZeroHours,
    NegativeNetPay,
    MissingCompensation,
    NoW4OnFile,
    EmployeeTerminated,
    EmployeeInactive,
    OverlappingPayPeriod,
    LargePayChange
}

/// <summary>
/// A condition the operator must see before posting. <see cref="BlocksPosting"/> separates
/// "look at this" from "this must not post".
/// </summary>
public sealed record PayRunWarning(
    PayRunWarningCode Code,
    string Message,
    bool BlocksPosting,
    int? EmployeeId = null);

public sealed record PayRunLine
{
    public required int EmployeeId { get; init; }
    public required string EmployeeName { get; init; }
    public decimal HoursWorked { get; init; }
    public decimal GrossPay { get; init; }
    public decimal PreTaxDeductions { get; init; }
    public decimal TotalTaxes { get; init; }
    public decimal PostTaxDeductions { get; init; }
    public decimal NetPay { get; init; }

    /// <summary>Employer-paid taxes for this employee. Not withheld from them.</summary>
    public decimal EmployerTaxes { get; init; }
}

public sealed record PayRunTotals
{
    public int EmployeeCount { get; init; }
    public decimal GrossPay { get; init; }
    public decimal PreTaxDeductions { get; init; }

    /// <summary>Tax withheld from employees. Never combined with <see cref="EmployerTaxes"/>.</summary>
    public decimal EmployeeTaxes { get; init; }

    public decimal PostTaxDeductions { get; init; }

    /// <summary>Total net pay - what actually leaves the bank account to employees.</summary>
    public decimal NetPay { get; init; }

    /// <summary>Employer-paid payroll taxes. An additional company cost on top of gross.</summary>
    public decimal EmployerTaxes { get; init; }

    /// <summary>Full payroll cost to the company: gross wages plus employer taxes.</summary>
    public decimal TotalEmployerCost => GrossPay + EmployerTaxes;
}

/// <summary>
/// What the operator reviews before posting. <see cref="CalculationHash"/> is handed back to
/// <see cref="PayRunService.PostAsync"/> so posting can prove it is committing these numbers.
/// </summary>
public sealed record PayRunPreview
{
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public required DateTime PayDate { get; init; }
    public required IReadOnlyList<PayRunLine> Lines { get; init; }
    public required PayRunTotals Totals { get; init; }
    public required IReadOnlyList<PayRunWarning> Warnings { get; init; }
    public required string CalculationHash { get; init; }

    public bool CanPost => !Warnings.Any(w => w.BlocksPosting);
}

public sealed class PayRunPostingException : InvalidOperationException
{
    public IReadOnlyList<PayRunWarning> Blockers { get; }

    public PayRunPostingException(string message, IReadOnlyList<PayRunWarning>? blockers = null)
        : base(message)
    {
        Blockers = blockers ?? Array.Empty<PayRunWarning>();
    }
}

/// <summary>
/// Owns the pay-run lifecycle: preview, post, void.
///
/// This logic previously lived in PayRunWizardViewModel, where posting saved the PayRun in one
/// transaction and its stubs in another. A failure between the two left an orphaned empty run,
/// and a missing employee was skipped with `continue`, silently posting a PARTIAL payroll.
/// Here, a run and all of its stubs commit together or not at all.
/// </summary>
public class PayRunService
{
    /// <summary>Bumped when a change alters computed amounts, so history stays interpretable.</summary>
    public const string EngineVersion = "2.0-pub15t-il";

    private readonly AppDbContext _dbContext;
    private readonly PayrollService _payrollService;

    public PayRunService(AppDbContext dbContext, PayrollService payrollService)
    {
        _dbContext = dbContext;
        _payrollService = payrollService;
    }

    /// <summary>
    /// Calculates a pay run without persisting anything, returning per-employee lines, totals,
    /// and everything the operator needs to be warned about.
    /// </summary>
    public async Task<PayRunPreview> PreviewAsync(
        PayRunDraft draft,
        IReadOnlyList<PayRunEmployeeInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(inputs);

        var warnings = new List<PayRunWarning>();

        if (draft.PeriodEnd < draft.PeriodStart)
        {
            throw new ArgumentException("Pay period end cannot precede its start.", nameof(draft));
        }

        await AddOverlapWarningsAsync(draft, warnings, cancellationToken);

        if (inputs.Count == 0)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.MissingCompensation,
                "The pay run includes no employees.",
                BlocksPosting: true));
        }

        // The same employee twice in one run would post them two stubs for one period, and
        // the second stub's YTD figures would not even include the first (both read priors
        // from the database before either is written).
        var duplicates = inputs
            .GroupBy(i => i.EmployeeId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var employeeId in duplicates)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.MissingCompensation,
                $"Employee {employeeId} appears more than once in this pay run.",
                BlocksPosting: true,
                employeeId));
        }

        var lines = new List<PayRunLine>();

        foreach (var input in inputs)
        {
            var employee = await _dbContext.Employees
                .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);

            if (employee is null)
            {
                // A missing employee is a hard failure. Skipping would post a partial payroll.
                throw new PayRunPostingException(
                    $"Employee {input.EmployeeId} was included in the pay run but no longer exists. " +
                    "Refresh the employee list and rebuild the run.");
            }

            AddEmployeeWarnings(employee, input, draft, warnings);

            var preview = await _payrollService.PreviewPayStubAsync(
                employee,
                draft,
                new PayStubInput
                {
                    RegularHours = input.RegularHours,
                    OvertimeHours = input.OvertimeHours,
                    BonusAmount = input.BonusAmount,
                    CommissionAmount = input.CommissionAmount,
                    BonusDescription = input.BonusDescription,
                    CommissionDescription = input.CommissionDescription
                });

            if (preview.NetPay < 0m)
            {
                warnings.Add(new PayRunWarning(
                    PayRunWarningCode.NegativeNetPay,
                    $"{employee.FullName} has negative net pay ({preview.NetPay:C}). Deductions exceed " +
                    "earnings for this period; resolve the arrears before posting.",
                    BlocksPosting: true,
                    employee.Id));
            }

            var preTax = preview.GrossPay - (preview.TaxFederal + preview.TaxState +
                                             preview.TaxSocialSecurity + preview.TaxMedicare +
                                             preview.PostTaxDeductions + preview.NetPay);

            lines.Add(new PayRunLine
            {
                EmployeeId = employee.Id,
                EmployeeName = employee.FullName,
                HoursWorked = input.RegularHours + input.OvertimeHours,
                GrossPay = preview.GrossPay,
                PreTaxDeductions = preTax,
                TotalTaxes = preview.TotalTaxes,
                PostTaxDeductions = preview.PostTaxDeductions,
                NetPay = preview.NetPay,
                EmployerTaxes = preview.TotalEmployerTaxes
            });

            // Surface employer-tax conditions (e.g. SUI not configured) once, not per employee.
            foreach (var message in preview.EmployerTaxWarnings)
            {
                if (!warnings.Any(w => w.Message == message))
                {
                    warnings.Add(new PayRunWarning(
                        PayRunWarningCode.MissingCompensation, message, BlocksPosting: false));
                }
            }
        }

        var totals = new PayRunTotals
        {
            EmployeeCount = lines.Count,
            GrossPay = lines.Sum(l => l.GrossPay),
            PreTaxDeductions = lines.Sum(l => l.PreTaxDeductions),
            EmployeeTaxes = lines.Sum(l => l.TotalTaxes),
            PostTaxDeductions = lines.Sum(l => l.PostTaxDeductions),
            NetPay = lines.Sum(l => l.NetPay),
            EmployerTaxes = lines.Sum(l => l.EmployerTaxes)
        };

        return new PayRunPreview
        {
            PeriodStart = draft.PeriodStart,
            PeriodEnd = draft.PeriodEnd,
            PayDate = draft.PayDate,
            Lines = lines,
            Totals = totals,
            Warnings = warnings,
            CalculationHash = ComputeHash(draft, lines)
        };
    }

    /// <summary>
    /// Posts a pay run: recalculates, verifies the result still matches what was reviewed,
    /// then commits the run and every stub inside ONE transaction.
    /// </summary>
    /// <param name="expectedCalculationHash">
    /// The hash from the preview the operator approved. If recalculation no longer matches -
    /// because an employee's rate or the company settings changed in between - posting is
    /// refused rather than quietly paying different amounts.
    /// </param>
    public async Task<PayRun> PostAsync(
        PayRunDraft draft,
        IReadOnlyList<PayRunEmployeeInput> inputs,
        string expectedCalculationHash,
        string? postedBy = null,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(draft, inputs, cancellationToken);

        if (!string.Equals(preview.CalculationHash, expectedCalculationHash, StringComparison.Ordinal))
        {
            throw new PayRunPostingException(
                "The pay run has changed since it was reviewed - employee compensation or company " +
                "settings were edited in the meantime. Review the recalculated figures and post again.");
        }

        var blockers = preview.Warnings.Where(w => w.BlocksPosting).ToList();
        if (blockers.Count > 0)
        {
            throw new PayRunPostingException(
                "The pay run cannot be posted until these are resolved: " +
                string.Join(" ", blockers.Select(b => b.Message)),
                blockers);
        }

        // One transaction: the run and every stub commit together, or nothing does.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var payRun = new PayRun
            {
                PeriodStart = draft.PeriodStart,
                PeriodEnd = draft.PeriodEnd,
                PayDate = draft.PayDate,
                Status = PayRunStatus.Posted,
                PostedAtUtc = DateTime.UtcNow,
                CalculationHash = preview.CalculationHash,
                CalculationEngineVersion = EngineVersion
            };

            _dbContext.PayRuns.Add(payRun);

            // Saving here assigns PayRun.Id for the stub foreign keys. Unlike the previous
            // implementation this is INSIDE the transaction, so a later failure rolls it back
            // rather than leaving an orphaned empty run behind.
            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var input in inputs)
            {
                var employee = await _dbContext.Employees
                    .FirstAsync(e => e.Id == input.EmployeeId, cancellationToken);

                var stub = await _payrollService.GeneratePayStubAsync(employee, payRun, new PayStubInput
                {
                    RegularHours = input.RegularHours,
                    OvertimeHours = input.OvertimeHours,
                    BonusAmount = input.BonusAmount,
                    CommissionAmount = input.CommissionAmount,
                    BonusDescription = input.BonusDescription,
                    CommissionDescription = input.CommissionDescription
                });

                _dbContext.PayStubs.Add(stub);
            }

            _dbContext.AuditLog.Add(new AuditLogEntry
            {
                Action = AuditAction.PayRunPosted,
                EntityType = nameof(PayRun),
                EntityId = payRun.Id,
                NewValue = $"{preview.Totals.EmployeeCount} employees, gross {preview.Totals.GrossPay:C}, " +
                           $"net {preview.Totals.NetPay:C}, pay date {draft.PayDate:yyyy-MM-dd}",
                PerformedBy = postedBy,
                ApplicationVersion = EngineVersion
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return payRun;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Voids a posted run. The run and its stubs are retained permanently; corrections are
    /// made by posting an adjustment run.
    /// </summary>
    public async Task VoidAsync(
        int payRunId,
        string reason,
        string? voidedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A void requires a reason - it is a permanent audit record.", nameof(reason));
        }

        var payRun = await _dbContext.PayRuns
            .FirstOrDefaultAsync(p => p.Id == payRunId, cancellationToken)
            ?? throw new PayRunPostingException($"Pay run {payRunId} was not found.");

        if (payRun.Status != PayRunStatus.Posted)
        {
            throw new PayRunPostingException(
                $"Only a posted pay run can be voided; run {payRunId} is {payRun.Status}.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            payRun.Status = PayRunStatus.Voided;
            payRun.VoidedAtUtc = DateTime.UtcNow;
            payRun.VoidReason = reason;

            _dbContext.AuditLog.Add(new AuditLogEntry
            {
                Action = AuditAction.PayRunVoided,
                EntityType = nameof(PayRun),
                EntityId = payRun.Id,
                OldValue = nameof(PayRunStatus.Posted),
                NewValue = nameof(PayRunStatus.Voided),
                PerformedBy = voidedBy,
                ApplicationVersion = EngineVersion,
                Notes = reason
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task AddOverlapWarningsAsync(
        PayRunDraft draft, List<PayRunWarning> warnings, CancellationToken cancellationToken)
    {
        // Voided runs do not reserve their period - that is the point of voiding.
        var overlapping = await _dbContext.PayRuns
            .Where(p => p.Status != PayRunStatus.Voided &&
                        p.PeriodStart <= draft.PeriodEnd &&
                        draft.PeriodStart <= p.PeriodEnd)
            .Select(p => new { p.Id, p.PeriodStart, p.PeriodEnd })
            .ToListAsync(cancellationToken);

        foreach (var run in overlapping)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.OverlappingPayPeriod,
                $"Pay run {run.Id} already covers {run.PeriodStart:yyyy-MM-dd} to {run.PeriodEnd:yyyy-MM-dd}, " +
                "which overlaps this period. Paying the same period twice is the most common payroll duplication.",
                BlocksPosting: true));
        }
    }

    private static void AddEmployeeWarnings(
        Employee employee, PayRunEmployeeInput input, PayRunDraft draft, List<PayRunWarning> warnings)
    {
        if (employee.TerminationDate is { } terminated && terminated < draft.PeriodStart)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.EmployeeTerminated,
                $"{employee.FullName} was terminated on {terminated:yyyy-MM-dd}, before this period began.",
                BlocksPosting: true,
                employee.Id));
        }

        if (!employee.IsActive)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.EmployeeInactive,
                $"{employee.FullName} is marked inactive but is included in this run.",
                BlocksPosting: false,
                employee.Id));
        }

        if (!employee.W4OnFile)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.NoW4OnFile,
                $"No Form W-4 is on file for {employee.FullName}; withholding uses defaults " +
                "(Single, no adjustments). An employer is required to hold a signed W-4.",
                BlocksPosting: false,
                employee.Id));
        }

        var hasHours = input.RegularHours > 0 || input.OvertimeHours > 0;
        var hasExtra = input.BonusAmount > 0 || input.CommissionAmount > 0;

        if (employee.IsHourly && !hasHours && !hasExtra)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.ZeroHours,
                $"{employee.FullName} is hourly but has no hours recorded for this period.",
                BlocksPosting: false,
                employee.Id));
        }

        if (employee.IsHourly && employee.HourlyRate <= 0m)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.MissingCompensation,
                $"{employee.FullName} is hourly but has no hourly rate set.",
                BlocksPosting: true,
                employee.Id));
        }

        if (!employee.IsHourly && employee.AnnualSalary <= 0m)
        {
            warnings.Add(new PayRunWarning(
                PayRunWarningCode.MissingCompensation,
                $"{employee.FullName} is salaried but has no annual salary set.",
                BlocksPosting: true,
                employee.Id));
        }
    }

    /// <summary>
    /// Hashes the period and every per-employee amount. Any change to what would be paid
    /// produces a different hash, which is what makes the post-time comparison meaningful.
    /// </summary>
    private static string ComputeHash(PayRunDraft draft, IReadOnlyList<PayRunLine> lines)
    {
        var builder = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        builder.Append(draft.PeriodStart.ToString("O", invariant)).Append('|')
               .Append(draft.PeriodEnd.ToString("O", invariant)).Append('|')
               .Append(draft.PayDate.ToString("O", invariant)).Append('|')
               .Append(EngineVersion).Append('\n');

        // Ordered by employee so the hash does not depend on input sequence.
        foreach (var line in lines.OrderBy(l => l.EmployeeId))
        {
            builder.Append(line.EmployeeId).Append('|')
                   .Append(line.GrossPay.ToString("F2", invariant)).Append('|')
                   .Append(line.TotalTaxes.ToString("F2", invariant)).Append('|')
                   .Append(line.PostTaxDeductions.ToString("F2", invariant)).Append('|')
                   .Append(line.NetPay.ToString("F2", invariant)).Append('|')
                   // Included so that editing the SUI rate between review and posting is
                   // caught too - it changes employer cost without touching net pay.
                   .Append(line.EmployerTaxes.ToString("F2", invariant)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
