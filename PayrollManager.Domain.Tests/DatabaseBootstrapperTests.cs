using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using Xunit;

namespace PayrollManager.Domain.Tests;

/// <summary>
/// Database location, relocation, and safe startup.
/// </summary>
public class DatabaseBootstrapperTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"payroll_boot_{Guid.NewGuid():N}");

    private readonly string _legacyDir;

    public DatabaseBootstrapperTests()
    {
        Directory.CreateDirectory(_dataDir);
        DbPaths.DataDirectoryOverride = _dataDir;

        // Point the legacy lookup at a per-test directory. Left at its default it resolves to
        // the test assembly's own output folder - shared by every test in the run, so a fixture
        // left behind by one test would be picked up by another and quietly copied forward.
        _legacyDir = Path.Combine(_dataDir, "legacy-install");
        DbPaths.LegacyDataDirectoryOverride = _legacyDir;
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPaths.GetDatabasePath()}")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void Initialize_CreatesAndMigratesAFreshDatabase()
    {
        using var db = CreateContext();

        var result = DatabaseBootstrapper.Initialize(db);

        Assert.True(File.Exists(DbPaths.GetDatabasePath()));
        Assert.NotEmpty(result.AppliedMigrations);
        Assert.Empty(db.Database.GetPendingMigrations());

        // Nothing to back up on a first run - a backup here would just be an empty file.
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using (var db = CreateContext())
        {
            DatabaseBootstrapper.Initialize(db);
        }

        using (var db = CreateContext())
        {
            var second = DatabaseBootstrapper.Initialize(db);

            Assert.Empty(second.AppliedMigrations);
            Assert.Null(second.BackupPath);      // no schema change, so no backup taken
        }
    }

    [Fact]
    public void DataDirectory_IsNotTheInstallDirectory()
    {
        // The install directory may be read-only for a standard user and is replaced by
        // upgrades. Payroll data must not live there.
        DbPaths.DataDirectoryOverride = null;

        try
        {
            var dataDir = DbPaths.GetDataDirectory();

            Assert.False(
                dataDir.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase),
                $"Data directory {dataDir} must not be inside the install directory.");

            Assert.Contains("PayrollManager", dataDir);
        }
        finally
        {
            DbPaths.DataDirectoryOverride = _dataDir;
        }
    }

    [Fact]
    public void CreateBackup_CopiesTheDatabase()
    {
        using (var db = CreateContext())
        {
            DatabaseBootstrapper.Initialize(db);
            db.Employees.Add(new Employee
            {
                FirstName = "Backup", LastName = "Me", IsHourly = true, HourlyRate = 25m
            });
            db.SaveChanges();
        }

        var backupPath = DbPaths.CreateBackup("manual");

        Assert.NotNull(backupPath);
        Assert.True(File.Exists(backupPath));
        Assert.Contains("manual", backupPath);

        // The backup is a real database, not a zero-byte placeholder.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={backupPath}")
            .Options;

        using var restored = new AppDbContext(options);
        Assert.Single(restored.Employees);
    }

    [Fact]
    public void CreateBackup_ReturnsNull_WhenThereIsNoDatabase()
    {
        Assert.Null(DbPaths.CreateBackup());
    }

    [Fact]
    public void LegacyDatabase_IsCopiedForward_AndTheOriginalIsLeftInPlace()
    {
        // Simulate a database sitting at the old install-directory location.
        var legacyPath = DbPaths.GetLegacyDatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);

        var legacyOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={legacyPath}")
            .Options;

        using (var legacyDb = new AppDbContext(legacyOptions))
        {
            legacyDb.Database.Migrate();
            legacyDb.Employees.Add(new Employee
            {
                FirstName = "Legacy", LastName = "Record", IsHourly = true, HourlyRate = 31m
            });
            legacyDb.SaveChanges();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            var migrated = DbPaths.MigrateLegacyDatabaseIfPresent();
            Assert.True(migrated);

            using var db = CreateContext();
            DatabaseBootstrapper.Initialize(db);

            // The employee came across.
            Assert.Equal("Legacy", db.Employees.Single().FirstName);

            // The original is COPIED, not moved - a failed relocation must not lose payroll data.
            Assert.True(File.Exists(legacyPath));

            // Running again is a no-op now that a database exists at the new location.
            Assert.False(DbPaths.MigrateLegacyDatabaseIfPresent());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Locked files on Windows should not fail the run.
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DbPaths.DataDirectoryOverride = null;
        DbPaths.LegacyDataDirectoryOverride = null;
        TryDelete(_dataDir);
    }
}
