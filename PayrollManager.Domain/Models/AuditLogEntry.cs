namespace PayrollManager.Domain.Models;

public enum AuditAction
{
    EmployeeCreated,
    EmployeeUpdated,
    CompensationChanged,
    DeductionChanged,
    W4Changed,
    PayRunCalculated,
    PayRunPosted,
    PayRunVoided,
    SettingsChanged,
    DataExported,
    BackupRestored
}

/// <summary>
/// Append-only record of a sensitive action.
///
/// For real payroll this is not optional: it is how you answer "who changed this employee's
/// rate, and when" months later. Entries are never updated or deleted - the SaveChanges guard
/// in AppDbContext enforces that.
/// </summary>
public class AuditLogEntry
{
    public int Id { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public AuditAction Action { get; set; }

    /// <summary>Entity type affected, e.g. "Employee" or "PayRun".</summary>
    public string EntityType { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    /// <summary>Prior value, where the action changed something. Free-form for readability.</summary>
    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    /// <summary>Who performed the action. Single-user today, but recorded from the start.</summary>
    public string? PerformedBy { get; set; }

    /// <summary>Application version, so an entry can be interpreted against the code of its time.</summary>
    public string? ApplicationVersion { get; set; }

    public string? Notes { get; set; }
}
