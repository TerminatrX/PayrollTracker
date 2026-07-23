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
/// Pay stub statement + PDF export, and SSN/address handling on employees, end to end through
/// the dispatcher against real SQLite.
/// </summary>
public class PayStubCommandsTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"payroll_paystub_{Guid.NewGuid():N}");

    public PayStubCommandsTests()
    {
        Directory.CreateDirectory(_dataDir);
        DbPaths.DataDirectoryOverride = _dataDir;
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    private static AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPaths.GetDatabasePath()}").Options);

    private static CommandDispatcher BuildDispatcher()
    {
        var dispatcher = new CommandDispatcher();
        new EmployeeCommands(CreateDbContext).RegisterOn(dispatcher);
        new PayRunCommands(CreateDbContext).RegisterOn(dispatcher);
        new PayStubCommands(CreateDbContext).RegisterOn(dispatcher);
        return dispatcher;
    }

    private static async Task<JsonElement> DispatchAsync(string method, object? p = null)
    {
        var dispatcher = BuildDispatcher();
        var pe = p is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(p, RpcJson.Options);
        var r = await dispatcher.DispatchAsync(new RpcRequest { Id = "1", Method = method, Params = pe });
        return JsonDocument.Parse(JsonSerializer.Serialize(r, RpcJson.Options)).RootElement;
    }

    private static JsonElement Result(JsonElement env)
    {
        Assert.False(env.TryGetProperty("error", out var e), $"unexpected error: {e}");
        return env.GetProperty("result");
    }

    private object NewEmployeeWithSsn() => new
    {
        firstName = "Grace", lastName = "Hopper", isActive = true, isHourly = true,
        hourlyRate = 25m, defaultHoursPerPeriod = 80, w4OnFile = true, filingStatus = "single",
        ilBasicAllowances = 1,
        streetAddress = "500 W Madison St", city = "Chicago", state = "IL", postalCode = "60661",
        ssn = "123-45-6789"
    };

    /// <summary>Creates an employee, posts a run, returns (employeeId, payStubId).</summary>
    private async Task<(int EmployeeId, int PayStubId)> SeedPostedStubAsync(object employee)
    {
        var created = Result(await DispatchAsync("create_employee", new { employee }));
        var empId = created.GetProperty("id").GetInt32();

        var payDate = new DateTime(DateTime.Today.Year, 1, 15);
        var draft = new { periodStart = payDate.AddDays(-14), periodEnd = payDate.AddDays(-1), payDate };
        var inputs = new[] { new { employeeId = empId, regularHours = 80m } };
        var preview = Result(await DispatchAsync("preview_pay_run", new { draft, employees = inputs }));
        var hash = preview.GetProperty("calculationHash").GetString();
        await DispatchAsync("post_pay_run", new { draft, employees = inputs, calculationHash = hash });

        using var db = CreateDbContext();
        var stubId = db.PayStubs.Single().Id;
        return (empId, stubId);
    }

    // ═══════════════════════════════════════════════════════════════
    // SSN handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateEmployee_StoresSsnEncrypted_AndNeverReturnsIt()
    {
        var result = Result(await DispatchAsync("create_employee", new { employee = NewEmployeeWithSsn() }));

        // The response exposes only last-4 and a flag - never the full SSN.
        Assert.Equal("6789", result.GetProperty("ssnLast4").GetString());
        Assert.True(result.GetProperty("ssnOnFile").GetBoolean());
        Assert.False(result.TryGetProperty("ssn", out _));
        Assert.False(result.TryGetProperty("ssnEncrypted", out _));

        // The full SSN appears nowhere in the serialized response.
        var raw = result.GetRawText();
        Assert.DoesNotContain("123456789", raw);
        Assert.DoesNotContain("123-45-6789", raw);

        // Stored encrypted, not plaintext.
        using var db = CreateDbContext();
        var employee = db.Employees.Single();
        Assert.False(string.IsNullOrEmpty(employee.SsnEncrypted));
        Assert.DoesNotContain("6789", employee.SsnEncrypted!);
        Assert.Equal("6789", employee.SsnLast4);
    }

    [Fact]
    public async Task UpdateEmployee_BlankSsn_LeavesExistingOnFile()
    {
        var created = Result(await DispatchAsync("create_employee", new { employee = NewEmployeeWithSsn() }));
        var id = created.GetProperty("id").GetInt32();

        // Update without an SSN field - must keep the one on file.
        var updated = Result(await DispatchAsync("update_employee", new
        {
            employeeId = id,
            employee = new
            {
                firstName = "Grace", lastName = "Hopper", isActive = true, isHourly = true,
                hourlyRate = 30m, defaultHoursPerPeriod = 80
            }
        }));

        Assert.True(updated.GetProperty("ssnOnFile").GetBoolean());
        Assert.Equal("6789", updated.GetProperty("ssnLast4").GetString());
    }

    [Fact]
    public async Task CreateEmployee_InvalidSsn_IsRejected()
    {
        var bad = new
        {
            firstName = "Bad", lastName = "Ssn", isActive = true, isHourly = true,
            hourlyRate = 25m, defaultHoursPerPeriod = 80, ssn = "12-34"
        };
        var env = await DispatchAsync("create_employee", new { employee = bad });

        var error = env.GetProperty("error");
        Assert.Equal(ErrorCodes.Validation, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("validationErrors").TryGetProperty("ssn", out _));
    }

    // ═══════════════════════════════════════════════════════════════
    // Statement
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPayStub_ReturnsCurrentAndYtdTaxesAndMaskedSsn()
    {
        var (_, stubId) = await SeedPostedStubAsync(NewEmployeeWithSsn());

        var stub = Result(await DispatchAsync("get_pay_stub", new { payStubId = stubId }));

        Assert.Equal("Grace Hopper", stub.GetProperty("employeeName").GetString());
        Assert.Equal("XXX-XX-6789", stub.GetProperty("maskedSsn").GetString());
        Assert.Equal("500 W Madison St", stub.GetProperty("addressLine1").GetString());
        Assert.Equal("Chicago, IL 60661", stub.GetProperty("addressLine2").GetString());

        // Four taxes, each with current + YTD (the core ask).
        var taxes = stub.GetProperty("taxes");
        Assert.Equal(4, taxes.GetArrayLength());
        Assert.Contains(taxes.EnumerateArray(), t => t.GetProperty("label").GetString() == "Social Security");
        foreach (var t in taxes.EnumerateArray())
        {
            Assert.True(t.TryGetProperty("current", out _));
            Assert.True(t.TryGetProperty("ytd", out _));
        }

        // First stub of the year: each tax's YTD equals its current amount.
        var fed = taxes.EnumerateArray().Single(t => t.GetProperty("label").GetString() == "Federal Income Tax");
        Assert.Equal(fed.GetProperty("current").GetDecimal(), fed.GetProperty("ytd").GetDecimal());

        // No full SSN leaks anywhere.
        Assert.DoesNotContain("123456789", stub.GetRawText());
    }

    [Fact]
    public async Task GetPayStub_NotFound_ReturnsNotFound()
    {
        var env = await DispatchAsync("get_pay_stub", new { payStubId = 9999 });
        Assert.Equal(ErrorCodes.NotFound, env.GetProperty("error").GetProperty("code").GetString());
    }

    // ═══════════════════════════════════════════════════════════════
    // PDF export
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportPayStubPdf_WritesAValidPdfFile()
    {
        var (_, stubId) = await SeedPostedStubAsync(NewEmployeeWithSsn());

        var outputPath = Path.Combine(_dataDir, "stub.pdf");
        var result = Result(await DispatchAsync("export_pay_stub_pdf", new { payStubId = stubId, outputPath }));

        var written = result.GetProperty("path").GetString();
        Assert.Equal(outputPath, written);
        Assert.True(File.Exists(written));

        var bytes = await File.ReadAllBytesAsync(written!);
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task ExportPayStubPdf_RequiresAnOutputPath()
    {
        var (_, stubId) = await SeedPostedStubAsync(NewEmployeeWithSsn());

        var env = await DispatchAsync("export_pay_stub_pdf", new { payStubId = stubId, outputPath = "" });
        Assert.Equal(ErrorCodes.Validation, env.GetProperty("error").GetProperty("code").GetString());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        DbPaths.DataDirectoryOverride = null;
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}
