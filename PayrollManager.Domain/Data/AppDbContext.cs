using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Data;

/// <summary>
/// Thrown when something attempts to alter posted payroll or the audit log.
/// </summary>
public sealed class ImmutablePayrollRecordException : InvalidOperationException
{
    public ImmutablePayrollRecordException(string message) : base(message)
    {
    }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    public DbSet<PayStub> PayStubs => Set<PayStub>();
    public DbSet<EarningLine> EarningLines => Set<EarningLine>();
    public DbSet<DeductionLine> DeductionLines => Set<DeductionLine>();
    public DbSet<TaxLine> TaxLines => Set<TaxLine>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public override int SaveChanges()
    {
        EnforceImmutability();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceImmutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Rejects any attempt to alter posted payroll history.
    ///
    /// This lives at the SaveChanges boundary rather than in a service so that no caller -
    /// including a future sidecar command or an ad-hoc script - can route around it.
    /// </summary>
    private void EnforceImmutability()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries<AuditLogEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new ImmutablePayrollRecordException(
                    "The audit log is append-only; entries cannot be modified or deleted.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<PayRun>())
        {
            if (entry.State == EntityState.Deleted)
            {
                var status = entry.OriginalValues.GetValue<PayRunStatus>(nameof(PayRun.Status));
                if (status is PayRunStatus.Posted or PayRunStatus.Voided)
                {
                    throw new ImmutablePayrollRecordException(
                        $"Pay run {entry.Entity.Id} is {status} and cannot be deleted. " +
                        "Void it instead - posted payroll is retained permanently.");
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                var originalStatus = entry.OriginalValues.GetValue<PayRunStatus>(nameof(PayRun.Status));

                if (originalStatus == PayRunStatus.Voided)
                {
                    throw new ImmutablePayrollRecordException(
                        $"Pay run {entry.Entity.Id} is voided and cannot be modified further.");
                }

                if (originalStatus == PayRunStatus.Posted && !IsPermittedVoidTransition(entry))
                {
                    throw new ImmutablePayrollRecordException(
                        $"Pay run {entry.Entity.Id} is posted and cannot be edited. " +
                        "Void it and issue an adjustment run instead.");
                }
            }
        }

        EnforcePayStubImmutability();
    }

    /// <summary>
    /// The one legal change to a posted run: transitioning it to Voided.
    /// </summary>
    private static bool IsPermittedVoidTransition(EntityEntry<PayRun> entry)
    {
        if (entry.CurrentValues.GetValue<PayRunStatus>(nameof(PayRun.Status)) != PayRunStatus.Voided)
        {
            return false;
        }

        var allowed = new[] { nameof(PayRun.Status), nameof(PayRun.VoidedAtUtc), nameof(PayRun.VoidReason) };

        return entry.Properties
            .Where(p => p.IsModified)
            .All(p => allowed.Contains(p.Metadata.Name));
    }

    private void EnforcePayStubImmutability()
    {
        // Direct edits to, or deletion of, a posted stub.
        foreach (var entry in ChangeTracker.Entries<PayStub>()
                     .Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            var payRunId = entry.OriginalValues.GetValue<int>(nameof(PayStub.PayRunId));

            if (IsImmutableRun(payRunId, out var status))
            {
                throw new ImmutablePayrollRecordException(
                    $"Pay stub {entry.Entity.Id} belongs to pay run {payRunId}, which is {status}. " +
                    "Posted pay stubs are permanent records of what was paid and cannot be changed.");
            }
        }

        // Edits to a posted stub's CHILD LINES. Guarding only the PayStub itself left a hole:
        // a caller could load a posted stub's TaxLine/DeductionLine/EarningLine and edit it
        // directly, and SaveChanges would allow it. Those rows drive the displayed
        // earning/tax breakdown AND the taxable-wage reconstruction used to compute later YTD
        // wage-base limits, so they must be exactly as immutable as the stub they belong to.
        EnforceChildLineImmutability<EarningLine>("earning");
        EnforceChildLineImmutability<DeductionLine>("deduction");
        EnforceChildLineImmutability<TaxLine>("tax");
    }

    private void EnforceChildLineImmutability<TLine>(string lineKind) where TLine : class
    {
        foreach (var entry in ChangeTracker.Entries<TLine>()
                     .Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            // Use the ORIGINAL PayStubId so that reparenting a line cannot dodge the check.
            var payStubId = entry.OriginalValues.GetValue<int>("PayStubId");
            var payRunId = ResolvePayRunIdForStub(payStubId);

            if (payRunId is int runId && IsImmutableRun(runId, out var status))
            {
                throw new ImmutablePayrollRecordException(
                    $"A {lineKind} line on pay stub {payStubId} belongs to pay run {runId}, " +
                    $"which is {status}. Posted pay stub lines are permanent and cannot be changed.");
            }
        }
    }

    /// <summary>
    /// Finds the pay run a stub belongs to, preferring a tracked stub and falling back to the
    /// store for a stub loaded on another context.
    /// </summary>
    private int? ResolvePayRunIdForStub(int payStubId)
    {
        var tracked = ChangeTracker.Entries<PayStub>()
            .FirstOrDefault(s => s.Entity.Id == payStubId);

        if (tracked is not null)
        {
            return tracked.OriginalValues.GetValue<int>(nameof(PayStub.PayRunId));
        }

        return PayStubs.AsNoTracking()
            .Where(s => s.Id == payStubId)
            .Select(s => (int?)s.PayRunId)
            .FirstOrDefault();
    }

    /// <summary>True if the pay run is posted or voided, i.e. its payroll is locked.</summary>
    private bool IsImmutableRun(int payRunId, out PayRunStatus? status)
    {
        // Prefer the tracked run; fall back to the store for a run loaded elsewhere.
        status = ChangeTracker.Entries<PayRun>()
                     .FirstOrDefault(r => r.Entity.Id == payRunId)?.Entity.Status
                 ?? PayRuns.AsNoTracking()
                     .Where(r => r.Id == payRunId)
                     .Select(r => (PayRunStatus?)r.Status)
                     .FirstOrDefault();

        return status is PayRunStatus.Posted or PayRunStatus.Voided;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = DbPaths.GetDatabasePath();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>()
            .HasMany(e => e.PayStubs)
            .WithOne(s => s.Employee)
            .HasForeignKey(s => s.EmployeeId);

        modelBuilder.Entity<PayRun>()
            .HasMany(p => p.PayStubs)
            .WithOne(s => s.PayRun)
            .HasForeignKey(s => s.PayRunId);

        modelBuilder.Entity<PayStub>()
            .HasMany(ps => ps.EarningLines)
            .WithOne(el => el.PayStub)
            .HasForeignKey(el => el.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PayStub>()
            .HasMany(ps => ps.DeductionLines)
            .WithOne(dl => dl.PayStub)
            .HasForeignKey(dl => dl.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PayStub>()
            .HasMany(ps => ps.TaxLines)
            .WithOne(tl => tl.PayStub)
            .HasForeignKey(tl => tl.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);

        // Stored as text to match the other enums here - readable in the DB and stable if
        // enum members are ever reordered.
        modelBuilder.Entity<Employee>()
            .Property(e => e.FilingStatus)
            .HasConversion<string>();

        modelBuilder.Entity<PayRun>()
            .Property(p => p.Status)
            .HasConversion<string>();

        modelBuilder.Entity<AuditLogEntry>()
            .Property(a => a.Action)
            .HasConversion<string>();

        // Posted runs are queried constantly for YTD totals and overlap checks.
        modelBuilder.Entity<PayRun>()
            .HasIndex(p => new { p.Status, p.PayDate });

        modelBuilder.Entity<AuditLogEntry>()
            .HasIndex(a => a.TimestampUtc);

        modelBuilder.Entity<EarningLine>()
            .Property(el => el.Type)
            .HasConversion<string>();

        modelBuilder.Entity<DeductionLine>()
            .Property(dl => dl.Type)
            .HasConversion<string>();

        modelBuilder.Entity<TaxLine>()
            .Property(tl => tl.Type)
            .HasConversion<string>();
    }
}
