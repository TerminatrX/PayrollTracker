using System.Text;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Handlers;
using PayrollManager.Backend.Rpc;
using PayrollManager.Domain.Data;

namespace PayrollManager.Backend;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // UTF-8 without a BOM. A BOM on stdout would prepend invisible bytes to the first
        // response and break JSON parsing on the host side.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8;

        var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false };
        var stdin = new StreamReader(Console.OpenStandardInput(), utf8);

        using var shutdown = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            // An explicit data directory lets the Tauri host place the database under its own
            // app-data folder. Without it we fall back to the per-user default.
            var dataDirectory = GetOption(args, "--data-dir");
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                DbPaths.DataDirectoryOverride = dataDirectory;
            }

            var startup = InitializeDatabase();

            Console.Error.WriteLine(
                $"[info] payroll-backend {BackendInfo.Version} ready; database: {startup.DatabasePath}");

            if (startup.MigratedFromLegacyLocation)
            {
                Console.Error.WriteLine("[info] migrated database from the legacy install-directory location");
            }

            if (startup.AppliedMigrations.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[info] applied {startup.AppliedMigrations.Count} migration(s); backup: {startup.BackupPath ?? "none"}");
            }

            var dispatcher = BuildDispatcher(startup);
            var host = new StdioHost(dispatcher, stdin, stdout);

            await host.RunAsync(shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            // Startup failure. Report on stderr and exit non-zero so the host surfaces it,
            // rather than leaving a process alive that answers nothing.
            Console.Error.WriteLine($"[fatal] {ex}");
            return 1;
        }
        finally
        {
            await stdout.FlushAsync();
        }
    }

    internal static CommandDispatcher BuildDispatcher(DatabaseStartupResult startup)
    {
        var dispatcher = new CommandDispatcher();

        dispatcher.Register("health", (_, _) => Task.FromResult<object?>(new HealthResponse
        {
            Status = "ok",
            EngineVersion = BackendInfo.Version,
            DatabasePath = startup.DatabasePath,
            MigratedFromLegacyLocation = startup.MigratedFromLegacyLocation,
            BackupPath = startup.BackupPath,
            AppliedMigrations = startup.AppliedMigrations
        }));

        new EmployeeCommands(CreateDbContext).RegisterOn(dispatcher);
        new SettingsCommands(CreateDbContext).RegisterOn(dispatcher);

        return dispatcher;
    }

    /// <summary>
    /// A fresh DbContext per command. The sidecar is long-lived, and a single shared context
    /// would accumulate tracked entities across unrelated requests and leak stale state
    /// between them.
    /// </summary>
    internal static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPaths.GetDatabasePath()}")
            .Options;

        return new AppDbContext(options);
    }

    private static DatabaseStartupResult InitializeDatabase()
    {
        using var db = CreateDbContext();
        return DatabaseBootstrapper.Initialize(db);
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
