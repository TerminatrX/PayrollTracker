namespace PayrollManager.Domain.Models;

/// <summary>
/// Lifecycle state of a pay run.
///
/// Draft -> Calculated -> Posted, with Voided as the only exit from Posted. A posted run is
/// immutable: money has been committed against it, so corrections are made by voiding and
/// issuing an adjustment run, never by editing history.
/// </summary>
public enum PayRunStatus
{
    /// <summary>Period and employees chosen; amounts not yet computed.</summary>
    Draft = 0,

    /// <summary>Amounts computed and reviewable. Recalculating is allowed.</summary>
    Calculated = 1,

    /// <summary>Committed. Immutable - cannot be edited or deleted.</summary>
    Posted = 2,

    /// <summary>Reversed after posting. Retained for audit; never removed.</summary>
    Voided = 3
}

public class PayRun
{
    public int Id { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime PayDate { get; set; }

    public PayRunStatus Status { get; set; } = PayRunStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public DateTime? VoidedAtUtc { get; set; }

    public string? VoidReason { get; set; }

    /// <summary>
    /// Hash of the calculated amounts the user reviewed before posting. Posting recomputes and
    /// compares, so a run can never post numbers different from the ones that were approved.
    /// </summary>
    public string? CalculationHash { get; set; }

    /// <summary>Engine version that produced this run's stubs, for interpreting history.</summary>
    public string? CalculationEngineVersion { get; set; }

    public ICollection<PayStub> PayStubs { get; set; } = new List<PayStub>();

    /// <summary>A posted run is locked; a voided run stays locked.</summary>
    public bool IsImmutable => Status is PayRunStatus.Posted or PayRunStatus.Voided;
}
