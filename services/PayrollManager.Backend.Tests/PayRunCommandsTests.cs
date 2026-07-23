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
/// Pay-run lifecycle commands end to end through the dispatcher against real SQLite.
/// </summary>
public class PayRunCommandsTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"payroll_payrun_{Guid.NewGuid():N}");

    public PayRunCommandsTests()
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
        new PayRunCommands(CreateDbContext).RegisterOn(dispatcher);
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

    private static JsonElement Result(JsonElement envelope)
    {
        Assert.False(envelope.TryGetProperty("error", out var err), $"unexpected error: {err}");
        return envelope.GetProperty("result");
    }

    private int AddHourlyEmployee(string first, decimal rate = 25m, int defaultHours = 80)
    {
        using var db = CreateDbContext();
        var e = new Employee
        {
            FirstName = first, LastName = "Worker", IsActive = true, IsHourly = true,
            HourlyRate = rate, DefaultHoursPerPeriod = defaultHours, W4OnFile = true
        };
        db.Employees.Add(e);
        db.SaveChanges();
        return e.Id;
    }

    /// <summary>Runs a full preview→post and returns the posted summary element.</summary>
    private async Task<(JsonElement Summary, object Draft, object[] Inputs)> PostRunAsync(
        DateTime payDate, params int[] employeeIds)
    {
        var draft = new
        {
            periodStart = payDate.AddDays(-14),
            periodEnd = payDate.AddDays(-1),
            payDate,
        };
        var inputs = employeeIds.Select(id => (object)new { employeeId = id, regularHours = 80m }).ToArray();

        var preview = Result(await DispatchAsync("preview_pay_run", new { draft, employees = inputs }));
        var hash = preview.GetProperty("calculationHash").GetString();

        var summary = Result(await DispatchAsync("post_pay_run",
            new { draft, employees = inputs, calculationHash = hash }));

        return (summary, draft, inputs);
    }

    [Fact]
    public async Task Suggest_ReturnsNextPeriodAndActiveEmployees()
    {
        AddHourlyEmployee("Ann");

        var result = Result(await DispatchAsync("suggest_pay_run"));

        var draft = result.GetProperty("draft");
        Assert.True(draft.GetProperty("payDate").GetDateTime() > draft.GetProperty("periodEnd").GetDateTime());

        var employees = result.GetProperty("employees");
        Assert.Equal(1, employees.GetArrayLength());

        // 80 hours over 2 biweekly workweeks is two 40-hour weeks -> no overtime suggested.
        Assert.Equal(80m, employees[0].GetProperty("suggestedRegularHours").GetDecimal());
        Assert.Equal(0m, employees[0].GetProperty("suggestedOvertimeHours").GetDecimal());
    }

    [Fact]
    public async Task Preview_ComputesTotalsAndCalculationHash()
    {
        var id = AddHourlyEmployee("Ann");
        var draft = new { periodStart = new DateTime(2026, 1, 1), periodEnd = new DateTime(2026, 1, 14), payDate = new DateTime(2026, 1, 15) };

        var preview = Result(await DispatchAsync("preview_pay_run",
            new { draft, employees = new[] { new { employeeId = id, regularHours = 80m } } }));

        Assert.Equal(2000m, preview.GetProperty("totals").GetProperty("grossPay").GetDecimal());
        Assert.True(preview.GetProperty("canPost").GetBoolean());
        Assert.False(string.IsNullOrEmpty(preview.GetProperty("calculationHash").GetString()));
    }

    [Fact]
    public async Task Post_CreatesAPostedRun_ThenAppearsInHistory()
    {
        var id = AddHourlyEmployee("Ann");

        var (summary, _, _) = await PostRunAsync(new DateTime(2026, 1, 15), id);

        Assert.Equal("Posted", summary.GetProperty("status").GetString());
        Assert.Equal(1, summary.GetProperty("employeeCount").GetInt32());
        Assert.Equal(2000m, summary.GetProperty("grossPay").GetDecimal());
        Assert.True(summary.GetProperty("employerTaxes").GetDecimal() > 0);

        var history = Result(await DispatchAsync("get_pay_runs")).GetProperty("payRuns");
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal("Posted", history[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Post_WithStaleHash_IsRejected()
    {
        var id = AddHourlyEmployee("Ann");
        var draft = new { periodStart = new DateTime(2026, 1, 1), periodEnd = new DateTime(2026, 1, 14), payDate = new DateTime(2026, 1, 15) };
        var inputs = new[] { new { employeeId = id, regularHours = 80m } };

        var envelope = await DispatchAsync("post_pay_run",
            new { draft, employees = inputs, calculationHash = "not-the-real-hash" });

        var error = envelope.GetProperty("error");
        Assert.Equal(ErrorCodes.BusinessRule, error.GetProperty("code").GetString());
        Assert.Empty(CreateDbContext().PayRuns);
    }

    [Fact]
    public async Task Post_OverlappingPeriod_IsBlocked()
    {
        var id = AddHourlyEmployee("Ann");
        await PostRunAsync(new DateTime(2026, 1, 15), id);

        // Same period again.
        var draft = new { periodStart = new DateTime(2026, 1, 1), periodEnd = new DateTime(2026, 1, 14), payDate = new DateTime(2026, 1, 15) };
        var inputs = new[] { new { employeeId = id, regularHours = 80m } };
        var preview = Result(await DispatchAsync("preview_pay_run", new { draft, employees = inputs }));

        Assert.False(preview.GetProperty("canPost").GetBoolean());
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "OverlappingPayPeriod" && w.GetProperty("blocksPosting").GetBoolean());
    }

    [Fact]
    public async Task Void_MarksRunVoided_AndFreesThePeriod()
    {
        var id = AddHourlyEmployee("Ann");
        var (summary, _, _) = await PostRunAsync(new DateTime(2026, 1, 15), id);
        var runId = summary.GetProperty("id").GetInt32();

        var voided = Result(await DispatchAsync("void_pay_run", new { payRunId = runId, reason = "Wrong hours" }));
        Assert.Equal("Voided", voided.GetProperty("status").GetString());

        // The period is free again: a fresh preview of the same dates no longer overlaps.
        var draft = new { periodStart = new DateTime(2026, 1, 1), periodEnd = new DateTime(2026, 1, 14), payDate = new DateTime(2026, 1, 15) };
        var inputs = new[] { new { employeeId = id, regularHours = 80m } };
        var preview = Result(await DispatchAsync("preview_pay_run", new { draft, employees = inputs }));
        Assert.True(preview.GetProperty("canPost").GetBoolean());
    }

    [Fact]
    public async Task Void_RequiresReason()
    {
        var id = AddHourlyEmployee("Ann");
        var (summary, _, _) = await PostRunAsync(new DateTime(2026, 1, 15), id);
        var runId = summary.GetProperty("id").GetInt32();

        var envelope = await DispatchAsync("void_pay_run", new { payRunId = runId, reason = "  " });
        Assert.Equal(ErrorCodes.Validation, envelope.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetPayRun_ReturnsPerEmployeeStubLines()
    {
        var a = AddHourlyEmployee("Ann");
        var b = AddHourlyEmployee("Bob");
        var (summary, _, _) = await PostRunAsync(new DateTime(2026, 1, 15), a, b);
        var runId = summary.GetProperty("id").GetInt32();

        var detail = Result(await DispatchAsync("get_pay_run", new { payRunId = runId }));

        Assert.Equal(2, detail.GetProperty("stubs").GetArrayLength());
        Assert.Equal("Posted", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.All(detail.GetProperty("stubs").EnumerateArray(),
            s => Assert.True(s.GetProperty("netPay").GetDecimal() > 0));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DbPaths.DataDirectoryOverride = null;
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}
