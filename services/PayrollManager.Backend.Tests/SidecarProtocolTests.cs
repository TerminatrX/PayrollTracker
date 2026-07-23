using System.Text;
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
/// End-to-end tests of the sidecar protocol: JSON in on stdin, JSON out on stdout, against a
/// real SQLite database.
/// </summary>
public class SidecarProtocolTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"payroll_sidecar_{Guid.NewGuid():N}");

    public SidecarProtocolTests()
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

        dispatcher.Register("health", (_, _) => Task.FromResult<object?>(new HealthResponse
        {
            Status = "ok",
            EngineVersion = "test",
            DatabasePath = DbPaths.GetDatabasePath()
        }));

        new EmployeeCommands(CreateDbContext).RegisterOn(dispatcher);
        new SettingsCommands(CreateDbContext).RegisterOn(dispatcher);
        new PayRunCommands(CreateDbContext).RegisterOn(dispatcher);
        new ReportingCommands(CreateDbContext).RegisterOn(dispatcher);
        return dispatcher;
    }

    /// <summary>
    /// Pipes newline-delimited requests through a real StdioHost and returns the raw stdout
    /// lines - the same bytes the Tauri host would parse.
    /// </summary>
    private static async Task<List<string>> RunAsync(params string[] requestLines)
    {
        var input = new StringReader(string.Join("\n", requestLines) + "\n");
        var output = new StringWriter();

        var host = new StdioHost(BuildDispatcher(), input, output);
        await host.RunAsync();

        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();
    }

    private static JsonElement ParseResult(string line)
    {
        var doc = JsonDocument.Parse(line);

        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            Assert.Fail($"Expected a result but got error: {error}");
        }

        return doc.RootElement.GetProperty("result");
    }

    private static JsonElement ParseError(string line)
    {
        var doc = JsonDocument.Parse(line);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error),
            $"Expected an error but got: {line}");
        return error;
    }

    private static string Request(string id, string method, object? parameters = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method
        };

        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        return JsonSerializer.Serialize(payload, RpcJson.Options);
    }

    private static object NewEmployee(string first = "Ada", string last = "Lovelace", bool hourly = true) => new
    {
        firstName = first,
        lastName = last,
        isActive = true,
        isHourly = hourly,
        hourlyRate = hourly ? 42.50m : 0m,
        annualSalary = hourly ? 0m : 90_000m,
        defaultHoursPerPeriod = 80,
        w4OnFile = true,
        filingStatus = "single",
        ilBasicAllowances = 1
    };

    // ═══════════════════════════════════════════════════════════════
    // PROTOCOL
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var lines = await RunAsync(Request("1", "health"));

        var result = ParseResult(Assert.Single(lines));
        Assert.Equal("ok", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EveryResponse_IsExactlyOneLine()
    {
        // The whole protocol depends on this. A response containing a raw newline would
        // desynchronize the stream and every later response would be misattributed.
        var lines = await RunAsync(
            Request("1", "health"),
            Request("2", "create_employee", new { employee = NewEmployee("Multi\nLine", "Na\rme") }),
            Request("3", "get_employees"));

        Assert.Equal(3, lines.Count);

        foreach (var line in lines)
        {
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
            JsonDocument.Parse(line);   // each line parses independently
        }
    }

    [Fact]
    public async Task Responses_CorrelateByRequestId()
    {
        var lines = await RunAsync(
            Request("alpha", "health"),
            Request("beta", "get_employees"),
            Request("gamma", "health"));

        var ids = lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("id").GetString()).ToList();

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, ids);
    }

    [Fact]
    public async Task MalformedJson_ReturnsAnErrorRatherThanCrashing()
    {
        // A crash here would take down the sidecar and the UI with it.
        var lines = await RunAsync("{ this is not json", Request("2", "health"));

        Assert.Equal(2, lines.Count);
        Assert.Equal(ErrorCodes.BadRequest, ParseError(lines[0]).GetProperty("code").GetString());

        // The host survives and keeps serving.
        Assert.Equal("ok", ParseResult(lines[1]).GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnknownMethod_IsRejectedCleanly()
    {
        var lines = await RunAsync(Request("1", "drop_all_tables"));

        var error = ParseError(Assert.Single(lines));
        Assert.Equal(ErrorCodes.UnknownMethod, error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public void NoGenericSqlCommand_IsExposed()
    {
        // The frontend must never be able to express arbitrary database access.
        var methods = BuildDispatcher().RegisteredMethods;

        Assert.DoesNotContain(methods, m =>
            m.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("query", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("exec", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("eval", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BlankLines_AreIgnored()
    {
        var lines = await RunAsync("", "   ", Request("1", "health"));

        Assert.Single(lines);
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYEE COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateEmployee_PersistsAndReturnsTheRecord()
    {
        var lines = await RunAsync(Request("1", "create_employee", new { employee = NewEmployee() }));

        var result = ParseResult(Assert.Single(lines));
        Assert.Equal("Ada", result.GetProperty("firstName").GetString());
        Assert.Equal("Ada Lovelace", result.GetProperty("fullName").GetString());
        Assert.True(result.GetProperty("id").GetInt32() > 0);

        using var db = CreateDbContext();
        Assert.Single(db.Employees);

        // Creating an employee is an audited action.
        Assert.Contains(db.AuditLog, a => a.Action == AuditAction.EmployeeCreated);
    }

    [Fact]
    public async Task CreateEmployee_RejectsInvalidInput_WithFieldLevelErrors()
    {
        var lines = await RunAsync(Request("1", "create_employee", new
        {
            employee = new { firstName = "", lastName = "", isHourly = true, hourlyRate = 0m }
        }));

        var error = ParseError(Assert.Single(lines));
        Assert.Equal(ErrorCodes.Validation, error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());

        var validation = error.GetProperty("validationErrors");
        Assert.True(validation.TryGetProperty("firstName", out _));
        Assert.True(validation.TryGetProperty("lastName", out _));
        Assert.True(validation.TryGetProperty("hourlyRate", out _));

        using var db = CreateDbContext();
        Assert.Empty(db.Employees);
    }

    [Fact]
    public async Task UpdateEmployee_ChangesFieldsAndAuditsCompensation()
    {
        var created = ParseResult((await RunAsync(
            Request("1", "create_employee", new { employee = NewEmployee() })))[0]);
        var id = created.GetProperty("id").GetInt32();

        var lines = await RunAsync(Request("2", "update_employee", new
        {
            employeeId = id,
            employee = new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                isActive = true,
                isHourly = true,
                hourlyRate = 55.00m,
                defaultHoursPerPeriod = 80,
                w4OnFile = true,
                filingStatus = "headOfHousehold"
            }
        }));

        var result = ParseResult(Assert.Single(lines));
        Assert.Equal(55.00m, result.GetProperty("hourlyRate").GetDecimal());
        Assert.Equal("headOfHousehold", result.GetProperty("filingStatus").GetString());

        using var db = CreateDbContext();
        var audit = db.AuditLog.Single(a => a.Action == AuditAction.CompensationChanged);
        Assert.Contains("42.50", audit.OldValue);
        Assert.Contains("55.00", audit.NewValue);
    }

    [Fact]
    public async Task UpdateEmployee_SwitchingPayType_ClearsTheStaleCompensation()
    {
        // An hourly employee switched to salary must not keep an hourly rate that a future
        // pay run could pick up.
        var created = ParseResult((await RunAsync(
            Request("1", "create_employee", new { employee = NewEmployee(hourly: true) })))[0]);
        var id = created.GetProperty("id").GetInt32();

        var lines = await RunAsync(Request("2", "update_employee", new
        {
            employeeId = id,
            employee = new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                isActive = true,
                isHourly = false,
                annualSalary = 120_000m,
                defaultHoursPerPeriod = 80
            }
        }));

        var result = ParseResult(Assert.Single(lines));
        Assert.Equal(0m, result.GetProperty("hourlyRate").GetDecimal());
        Assert.Equal(120_000m, result.GetProperty("annualSalary").GetDecimal());
    }

    [Fact]
    public async Task GetEmployee_NotFound_ReturnsNotFound()
    {
        var lines = await RunAsync(Request("1", "get_employee", new { employeeId = 9999 }));

        var error = ParseError(Assert.Single(lines));
        Assert.Equal(ErrorCodes.NotFound, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetEmployees_ExcludesInactiveByDefault()
    {
        await RunAsync(
            Request("1", "create_employee", new { employee = NewEmployee("Active", "One") }),
            Request("2", "create_employee", new
            {
                employee = new
                {
                    firstName = "Inactive", lastName = "Two", isActive = false,
                    isHourly = true, hourlyRate = 20m, defaultHoursPerPeriod = 80
                }
            }));

        var active = ParseResult((await RunAsync(Request("3", "get_employees")))[0]);
        Assert.Equal(1, active.GetArrayLength());

        var all = ParseResult((await RunAsync(
            Request("4", "get_employees", new { includeInactive = true })))[0]);
        Assert.Equal(2, all.GetArrayLength());
    }

    [Fact]
    public async Task GetEmployees_SearchMatchesNameAndDepartment()
    {
        await RunAsync(
            Request("1", "create_employee", new
            {
                employee = new
                {
                    firstName = "Grace", lastName = "Hopper", isActive = true, isHourly = false,
                    annualSalary = 100_000m, defaultHoursPerPeriod = 80, department = "Engineering"
                }
            }),
            Request("2", "create_employee", new
            {
                employee = new
                {
                    firstName = "Alan", lastName = "Turing", isActive = true, isHourly = false,
                    annualSalary = 100_000m, defaultHoursPerPeriod = 80, department = "Research"
                }
            }));

        var byName = ParseResult((await RunAsync(
            Request("3", "get_employees", new { search = "hopp" })))[0]);
        Assert.Equal(1, byName.GetArrayLength());

        var byDept = ParseResult((await RunAsync(
            Request("4", "get_employees", new { search = "Research" })))[0]);
        Assert.Equal(1, byDept.GetArrayLength());
        Assert.Equal("Turing", byDept[0].GetProperty("lastName").GetString());
    }

    [Fact]
    public async Task HireDate_RoundTripsThroughTheProtocol()
    {
        // Defect #9 end to end: the field the old UI silently dropped.
        var lines = await RunAsync(Request("1", "create_employee", new
        {
            employee = new
            {
                firstName = "Hire", lastName = "Date", isActive = true, isHourly = true,
                hourlyRate = 30m, defaultHoursPerPeriod = 80, hireDate = "2024-03-15T00:00:00"
            }
        }));

        var result = ParseResult(Assert.Single(lines));
        Assert.StartsWith("2024-03-15", result.GetProperty("hireDate").GetString());
    }

    [Fact]
    public async Task EmployeeDto_DoesNotLeakNavigationCollections()
    {
        // The frontend has no business receiving pay stub graphs off an employee record.
        var lines = await RunAsync(Request("1", "create_employee", new { employee = NewEmployee() }));

        var result = ParseResult(Assert.Single(lines));
        Assert.False(result.TryGetProperty("payStubs", out _));
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
            // A locked file on Windows should not fail the test run.
        }
    }
}
