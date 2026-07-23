using System.IO;
using Microsoft.Data.Sqlite;

namespace PayrollManager.Domain.Data;

/// <summary>
/// Resolves where the payroll database lives.
///
/// This is the single choke point for the database location - every consumer (WinUI app,
/// sidecar, EF design-time tooling) goes through it, so the location can change here alone.
/// </summary>
public static class DbPaths
{
    private const string AppFolderName = "PayrollManager";
    private const string DatabaseFileName = "payroll.db";

    /// <summary>
    /// Overrides the data directory. Used by tests and by the sidecar when a host supplies an
    /// explicit path. Set to null to fall back to the per-user application data directory.
    /// </summary>
    public static string? DataDirectoryOverride { get; set; }

    /// <summary>
    /// Per-user writable data directory, e.g.
    /// <c>C:\Users\&lt;user&gt;\AppData\Local\PayrollManager</c>.
    ///
    /// Deliberately NOT the application install directory: that may be read-only for a
    /// non-elevated user, is wiped or replaced by upgrades, and is shared between users on a
    /// multi-user machine. Payroll data must survive an app update.
    /// </summary>
    public static string GetDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(DataDirectoryOverride))
        {
            Directory.CreateDirectory(DataDirectoryOverride);
            return DataDirectoryOverride;
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        var dataFolder = Path.Combine(localAppData, AppFolderName);
        Directory.CreateDirectory(dataFolder);
        return dataFolder;
    }

    public static string GetDatabasePath() => Path.Combine(GetDataDirectory(), DatabaseFileName);

    /// <summary>Directory holding pre-migration and manual backups.</summary>
    public static string GetBackupDirectory()
    {
        var backups = Path.Combine(GetDataDirectory(), "Backups");
        Directory.CreateDirectory(backups);
        return backups;
    }

    /// <summary>
    /// Overrides the legacy lookup location. Exists so tests can exercise relocation without
    /// writing into the real install directory, which is shared process-wide and would let one
    /// test's fixture leak into another's.
    /// </summary>
    public static string? LegacyDataDirectoryOverride { get; set; }

    /// <summary>
    /// The pre-1.0 database location: a "Data" folder beside the executable.
    /// </summary>
    public static string GetLegacyDatabasePath() =>
        Path.Combine(
            LegacyDataDirectoryOverride ?? Path.Combine(AppContext.BaseDirectory, "Data"),
            DatabaseFileName);

    /// <summary>
    /// Moves a database from the old install-directory location to the per-user directory,
    /// if one exists there and no database exists at the new location yet.
    ///
    /// The legacy file is COPIED, not moved: if anything goes wrong the original is still
    /// there. Losing payroll history to a failed relocation is not an acceptable outcome.
    /// </summary>
    /// <returns>True if a legacy database was migrated.</returns>
    public static bool MigrateLegacyDatabaseIfPresent()
    {
        var legacyPath = GetLegacyDatabasePath();
        var currentPath = GetDatabasePath();

        if (!File.Exists(legacyPath) || File.Exists(currentPath))
        {
            return false;
        }

        // Snapshot rather than file-copy, for the same reason as CreateBackup: copying the
        // .db, -wal and -shm files separately can capture them at inconsistent moments, and
        // omitting the -wal loses recent transactions outright.
        SnapshotTo(legacyPath, currentPath);

        return true;
    }

    /// <summary>
    /// Snapshots the current database into the backup directory with a timestamped name.
    /// Call before applying migrations.
    ///
    /// Uses SQLite's online backup API rather than File.Copy. A plain file copy captures only
    /// the main database file, so anything still in the write-ahead log - or held by a pooled
    /// connection that has not checkpointed - is silently missing from the copy. That produces
    /// a backup which looks fine, restores cleanly, and has lost recent payroll. The backup API
    /// walks the live database and writes a consistent snapshot including WAL content.
    /// </summary>
    /// <returns>The backup path, or null if there was no database to back up.</returns>
    public static string? CreateBackup(string? label = null)
    {
        var dbPath = GetDatabasePath();

        if (!File.Exists(dbPath))
        {
            return null;
        }

        var suffix = string.IsNullOrWhiteSpace(label) ? string.Empty : $"-{label}";
        var name = $"payroll-{DateTime.UtcNow:yyyyMMdd-HHmmss}{suffix}.db";
        var backupPath = Path.Combine(GetBackupDirectory(), name);

        SnapshotTo(dbPath, backupPath);
        return backupPath;
    }

    /// <summary>
    /// Writes a consistent snapshot of <paramref name="sourceDbPath"/> to
    /// <paramref name="destinationDbPath"/> using SQLite's online backup API.
    /// </summary>
    private static void SnapshotTo(string sourceDbPath, string destinationDbPath)
    {
        using var source = new SqliteConnection($"Data Source={sourceDbPath}");
        using var destination = new SqliteConnection($"Data Source={destinationDbPath}");

        source.Open();
        destination.Open();

        source.BackupDatabase(destination);
    }
}
