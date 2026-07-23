using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Handlers;
using PayrollManager.Backend.Rpc;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using Xunit;

namespace PayrollManager.Backend.Tests;

/// <summary>
/// Company settings and backup commands, end to end through the dispatcher against real SQLite.
/// </summary>
public class SettingsCommandsTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"payroll_settings_{Guid.NewGuid():N}");

    public SettingsCommandsTests()
    {
        Directory.CreateDirectory(_dataDir);
        DbPaths.DataDirectoryOverride = _dataDir;

        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPaths.GetDatabasePath()}")
            .Options;

        return new AppDbContext(options);
    }

    private static CommandDispatcher BuildDispatcher()
    {
        var dispatcher = new CommandDispatcher();
        new SettingsCommands(CreateDbContext).RegisterOn(dispatcher);
        return dispatcher;
    }

    private static async Task<JsonElement> DispatchAsync(string method, object? parameters = null)
    {
        var dispatcher = BuildDispatcher();
        var paramsElement = parameters is null
            ? (JsonElement?)null
            : JsonSerializer.SerializeToElement(parameters, RpcJson.Options);

        var response = await dispatcher.DispatchAsync(new RpcRequest
        {
            Id = "1",
            Method = method,
            Params = paramsElement
        });

        var json = JsonSerializer.Serialize(response, RpcJson.Options);
        return JsonDocument.Parse(json).RootElement;
    }

    private static JsonElement Result(JsonElement envelope)
    {
        Assert.False(envelope.TryGetProperty("error", out var err), $"unexpected error: {err}");
        return envelope.GetProperty("result");
    }

    private static object ValidSettings(object? overrides = null)
    {
        // A complete, valid settings payload; individual tests spread overrides on top.
        var baseSettings = new Dictionary<string, object?>
        {
            ["companyName"] = "Acme Illinois LLC",
            ["companyAddress"] = "1 State St, Chicago, IL",
            ["taxId"] = "36-1234567",
            ["payPeriodsPerYear"] = 26,
            ["defaultHoursPerPeriod"] = 80,
            ["socialSecurityPercent"] = 6.2m,
            ["medicarePercent"] = 1.45m,
            ["suiRatePercent"] = 3.0m,
            ["suiWageBase"] = 13590m,
            ["receivesFullFutaCredit"] = true
        };

        if (overrides is not null)
        {
            foreach (var prop in overrides.GetType().GetProperties())
            {
                baseSettings[prop.Name] = prop.GetValue(overrides);
            }
        }

        return baseSettings;
    }

    [Fact]
    public async Task GetCompanySettings_ReturnsDefaults_WhenNoneSaved()
    {
        var result = Result(await DispatchAsync("get_company_settings"));

        // GetSettingsAsync seeds a single default record.
        Assert.Equal("Bi-weekly", result.GetProperty("payFrequencyLabel").GetString());
        Assert.False(result.GetProperty("suiConfigured").GetBoolean());   // no SUI by default
    }

    [Fact]
    public async Task UpdateCompanySettings_PersistsAndAudits()
    {
        var result = Result(await DispatchAsync("update_company_settings",
            new { settings = ValidSettings() }));

        Assert.Equal("Acme Illinois LLC", result.GetProperty("companyName").GetString());
        Assert.True(result.GetProperty("suiConfigured").GetBoolean());
        Assert.Equal("Bi-weekly", result.GetProperty("payFrequencyLabel").GetString());

        // Persisted and audited.
        var reloaded = Result(await DispatchAsync("get_company_settings"));
        Assert.Equal(3.0m, reloaded.GetProperty("suiRatePercent").GetDecimal());

        using var db = CreateDbContext();
        Assert.Contains(db.AuditLog, a => a.Action == AuditAction.SettingsChanged);
    }

    [Fact]
    public async Task UpdateCompanySettings_RejectsUnsupportedPayFrequency()
    {
        var envelope = await DispatchAsync("update_company_settings",
            new { settings = ValidSettings(new { payPeriodsPerYear = 13 }) });

        var error = envelope.GetProperty("error");
        Assert.Equal(ErrorCodes.Validation, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("validationErrors").TryGetProperty("payPeriodsPerYear", out _));
    }

    [Fact]
    public async Task UpdateCompanySettings_RejectsSuiRateWithoutWageBase()
    {
        var envelope = await DispatchAsync("update_company_settings",
            new { settings = ValidSettings(new { suiRatePercent = 3.0m, suiWageBase = 0m }) });

        var error = envelope.GetProperty("error");
        Assert.Equal(ErrorCodes.Validation, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("validationErrors").TryGetProperty("suiWageBase", out _));
    }

    [Fact]
    public async Task UpdateCompanySettings_RejectsBlankCompanyName()
    {
        var envelope = await DispatchAsync("update_company_settings",
            new { settings = ValidSettings(new { companyName = "" }) });

        Assert.Equal(ErrorCodes.Validation, envelope.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateBackup_ProducesAListedBackupFile()
    {
        // Need a database with content to back up.
        await DispatchAsync("update_company_settings", new { settings = ValidSettings() });

        var created = Result(await DispatchAsync("create_backup"));
        var fileName = created.GetProperty("backup").GetProperty("fileName").GetString();
        Assert.NotNull(fileName);
        Assert.True(created.GetProperty("backup").GetProperty("sizeBytes").GetInt64() > 0);

        var listed = Result(await DispatchAsync("list_backups"));
        var backups = listed.GetProperty("backups");
        Assert.True(backups.GetArrayLength() >= 1);
        Assert.Contains(backups.EnumerateArray(),
            b => b.GetProperty("fileName").GetString() == fileName);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DbPaths.DataDirectoryOverride = null;

        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
