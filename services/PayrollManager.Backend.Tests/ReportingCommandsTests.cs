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
/// Dashboard, payroll report, CSV export, and audit-log commands.
/// </summary>
public class ReportingCommandsTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"payroll_report_{Guid.NewGuid():N}");

    public ReportingCommandsTests()
    {
        Directory.CreateDirectory(_dataDir);
        DbPaths.DataDirectoryOverride = _dataDir;
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    private static AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPaths.GetDatabasePath()}")
            .Options);

    private static CommandDispatcher BuildDispatcher()
    {
        var dispatcher = new CommandDispatcher();
        new EmployeeCommands(CreateDbContext).RegisterOn(dispatcher);
        new PayRunCommands(CreateDbContext).RegisterOn(dispatcher);
        new ReportingCommands(CreateDbContext).RegisterOn(dispatcher);
        return dispatcher;
    }

    private static async Task<JsonElement> DispatchAsync(string method, object? parameters = null)
    {
        var dispatcher = BuildDispatcher();
        var paramsElement = parameters is null
            ? (JsonElement?)null
            : JsonSerializer.SerializeToElement(parameters, RpcJson.Options);
        var response = await dispatcher.DispatchAsync(new RpcRequest { Id = "1", Method = method, Params = paramsElement });
        return JsonDocument.Parse(JsonSerializer.Serialize(response, RpcJson.Options)).RootElement;
    }

    private static JsonElement Result(JsonElement env)
    {
        Assert.False(env.TryGetProperty("error", out var e), $"unexpected error: {e}");
        return env.GetProperty("result");
    }

    /// <summary>Creates an hourly employee and posts a run for them; returns the run summary.</summary>
    private async Task<JsonElement> SeedPostedRunAsync(DateTime payDate)
    {
        var create = Result(await DispatchAsync("create_employee", new
        {
            employee = new
            {
                firstName = "Ann", lastName = "Worker", isActive = true, isHourly = true,
                hourlyRate = 25m, defaultHoursPerPeriod = 80, w4OnFile = true, ilBasicAllowances = 1
            }
        }));
        var id = create.GetProperty("id").GetInt32();

        var draft = new { periodStart = payDate.AddDays(-14), periodEnd = payDate.AddDays(-1), payDate };
        var inputs = new[] { new { employeeId = id, regularHours = 80m } };
        var preview = Result(await DispatchAsync("preview_pay_run", new { draft, employees = inputs }));
        var hash = preview.GetProperty("calculationHash").GetString();
        return Result(await DispatchAsync("post_pay_run", new { draft, employees = inputs, calculationHash = hash }));
    }

    [Fact]
    public async Task Dashboard_ReflectsPostedPayroll()
    {
        await SeedPostedRunAsync(new DateTime(DateTime.Today.Year, 1, 15));

        var dash = Result(await DispatchAsync("get_dashboard"));

        Assert.Equal(1, dash.GetProperty("activeEmployeeCount").GetInt32());
        Assert.Equal(1, dash.GetProperty("postedRunCount").GetInt32());
        Assert.Equal(2000m, dash.GetProperty("companyYtd").GetProperty("grossPay").GetDecimal());
        Assert.True(dash.GetProperty("companyYtd").GetProperty("employerTaxes").GetDecimal() > 0);
        Assert.False(dash.GetProperty("suiConfigured").GetBoolean());
        Assert.False(string.IsNullOrEmpty(dash.GetProperty("nextPayDate").GetString()));
    }

    [Fact]
    public async Task Dashboard_ExcludesVoidedRunsFromYtd()
    {
        var posted = await SeedPostedRunAsync(new DateTime(DateTime.Today.Year, 1, 15));

        // A second run, then voided - it must not inflate YTD.
        var runId = posted.GetProperty("id").GetInt32();
        Assert.True(runId > 0);

        var dashBefore = Result(await DispatchAsync("get_dashboard"));
        Assert.Equal(2000m, dashBefore.GetProperty("companyYtd").GetProperty("grossPay").GetDecimal());

        await DispatchAsync("void_pay_run", new { payRunId = runId, reason = "test" });

        var dashAfter = Result(await DispatchAsync("get_dashboard"));
        Assert.Equal(0m, dashAfter.GetProperty("companyYtd").GetProperty("grossPay").GetDecimal());
        Assert.Equal(0, dashAfter.GetProperty("postedRunCount").GetInt32());
    }

    [Fact]
    public async Task PayrollReport_ReturnsCompanyAndEmployeeTotals()
    {
        var year = DateTime.Today.Year;
        await SeedPostedRunAsync(new DateTime(year, 1, 15));

        var report = Result(await DispatchAsync("get_payroll_report", new
        {
            startDate = new DateTime(year, 1, 1),
            endDate = new DateTime(year, 12, 31)
        }));

        Assert.Equal(2000m, report.GetProperty("company").GetProperty("grossPay").GetDecimal());
        Assert.Equal(1, report.GetProperty("employees").GetArrayLength());
        Assert.Equal("Ann Worker", report.GetProperty("employees")[0].GetProperty("employeeName").GetString());
    }

    [Fact]
    public async Task PayrollReport_RejectsInvertedDateRange()
    {
        var env = await DispatchAsync("get_payroll_report", new
        {
            startDate = new DateTime(2026, 12, 31),
            endDate = new DateTime(2026, 1, 1)
        });
        Assert.Equal(ErrorCodes.Validation, env.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportPayrollReport_WritesACsvFile()
    {
        var year = DateTime.Today.Year;
        await SeedPostedRunAsync(new DateTime(year, 1, 15));

        var outputPath = Path.Combine(_dataDir, "report.csv");
        var result = Result(await DispatchAsync("export_payroll_report", new
        {
            startDate = new DateTime(year, 1, 1),
            endDate = new DateTime(year, 12, 31),
            outputPath
        }));

        var writtenPath = result.GetProperty("path").GetString();
        Assert.NotNull(writtenPath);
        Assert.True(File.Exists(writtenPath));
        var content = await File.ReadAllTextAsync(writtenPath!);
        Assert.Contains("Ann Worker", content);
    }

    [Fact]
    public async Task ExportPayrollReport_RequiresAnOutputPath()
    {
        var env = await DispatchAsync("export_payroll_report", new
        {
            startDate = new DateTime(2026, 1, 1),
            endDate = new DateTime(2026, 12, 31),
            outputPath = ""
        });
        Assert.Equal(ErrorCodes.Validation, env.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AuditLog_ReturnsRecordedActions_NewestFirst()
    {
        await SeedPostedRunAsync(new DateTime(DateTime.Today.Year, 1, 15));

        var log = Result(await DispatchAsync("get_audit_log"));
        var entries = log.GetProperty("entries");

        Assert.True(entries.GetArrayLength() >= 2); // employee created + pay run posted
        Assert.Contains(entries.EnumerateArray(),
            e => e.GetProperty("action").GetString() == "PayRunPosted");
        Assert.Contains(entries.EnumerateArray(),
            e => e.GetProperty("action").GetString() == "EmployeeCreated");

        // Newest first: timestamps are non-increasing.
        var timestamps = entries.EnumerateArray().Select(e => e.GetProperty("timestampUtc").GetDateTime()).ToList();
        for (var i = 1; i < timestamps.Count; i++)
        {
            Assert.True(timestamps[i] <= timestamps[i - 1]);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DbPaths.DataDirectoryOverride = null;
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}
