using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Rpc;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Handlers;

/// <summary>
/// Individual pay stub commands: fetch the on-screen statement, and export it as a PDF.
///
/// Both are built from the domain PayStubStatementService, the single source of truth, so the
/// on-screen figures and the exported PDF always agree. The full SSN is never returned or
/// written to a response - only the pre-masked last four.
/// </summary>
public sealed class PayStubCommands
{
    private readonly Func<AppDbContext> _contextFactory;

    public PayStubCommands(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public void RegisterOn(CommandDispatcher dispatcher)
    {
        dispatcher.Register("get_pay_stub", (p, ct) => GetAsync(p, ct));
        dispatcher.Register("export_pay_stub_pdf", (p, ct) => ExportPdfAsync(p, ct));
    }

    private PayStubStatementService StatementService(AppDbContext db) =>
        new(db, new CompanySettingsService(db), new AggregationService(db));

    private async Task<object?> GetAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<GetPayStubRequest>(parameters);

        await using var db = _contextFactory();

        try
        {
            var statement = await StatementService(db).BuildAsync(request.PayStubId);
            return PayStubStatementDto.From(statement);
        }
        catch (PayStubNotFoundException ex)
        {
            throw RpcException.NotFound(ex.Message);
        }
        catch (PayStubNotPostedException ex)
        {
            throw RpcException.BusinessRule(ex.Message);
        }
    }

    private async Task<object?> ExportPdfAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<ExportPayStubPdfRequest>(parameters);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw RpcException.Validation(new Dictionary<string, string[]>
            {
                ["outputPath"] = new[] { "A destination path is required." }
            });
        }

        await using var db = _contextFactory();
        var exportService = new ExportService(db, new CompanySettingsService(db));

        try
        {
            var path = await exportService.ExportPayStubToPdfAsync(request.PayStubId, request.OutputPath);
            return new ExportPayStubPdfResponse { Path = path };
        }
        catch (PayStubNotFoundException ex)
        {
            throw RpcException.NotFound(ex.Message);
        }
        catch (PayStubNotPostedException ex)
        {
            throw RpcException.BusinessRule(ex.Message);
        }
    }
}
