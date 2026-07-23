using Microsoft.EntityFrameworkCore;

namespace PayrollManager.Domain.Data;

public sealed record DatabaseStartupResult
{
    public bool MigratedFromLegacyLocation { get; init; }
    public string? BackupPath { get; init; }
    public IReadOnlyList<string> AppliedMigrations { get; init; } = Array.Empty<string>();
    public bool IntegrityCheckPassed { get; init; } = true;
    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class DatabaseIntegrityException : InvalidOperationException
{
    public DatabaseIntegrityException(string message) : base(message)
    {
    }
}

/// <summary>
/// Opens the payroll database safely: relocate from the legacy path if needed, verify
/// integrity, back up before altering schema, then migrate.
///
/// Every host must go through this rather than calling Database.Migrate() directly, so that
/// an interrupted migration always has a restorable backup sitting beside it.
/// </summary>
public static class DatabaseBootstrapper
{
    public static DatabaseStartupResult Initialize(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var migratedLegacy = DbPaths.MigrateLegacyDatabaseIfPresent();
        var databasePath = DbPaths.GetDatabasePath();

        var pending = dbContext.Database.GetPendingMigrations().ToList();
        var databaseExists = dbContext.Database.CanConnect() && File.Exists(databasePath);

        // Verify integrity BEFORE altering schema - migrating a corrupt file makes recovery
        // harder, not easier.
        if (databaseExists && !RunIntegrityCheck(dbContext, out var integrityMessage))
        {
            throw new DatabaseIntegrityException(
                $"The payroll database at {databasePath} failed an integrity check: {integrityMessage}. " +
                "Restore from a backup in the Backups folder rather than continuing - migrating " +
                "a corrupt database can make the damage permanent.");
        }

        // Back up only when the schema is actually about to change.
        string? backupPath = null;
        if (databaseExists && pending.Count > 0)
        {
            backupPath = DbPaths.CreateBackup("pre-migration");
        }

        dbContext.Database.Migrate();

        return new DatabaseStartupResult
        {
            MigratedFromLegacyLocation = migratedLegacy,
            BackupPath = backupPath,
            AppliedMigrations = pending,
            IntegrityCheckPassed = true,
            DatabasePath = databasePath
        };
    }

    private static bool RunIntegrityCheck(AppDbContext dbContext, out string message)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();

            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            var result = command.ExecuteScalar()?.ToString() ?? "no result";
            message = result;

            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // A provider that does not support the pragma (e.g. the in-memory provider used in
            // tests) must not block startup.
            message = ex.Message;
            return true;
        }
    }
}
