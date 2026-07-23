using System.Text.Json;
using PayrollManager.Backend.Contracts;

namespace PayrollManager.Backend.Rpc;

public delegate Task<object?> CommandHandler(JsonElement? parameters, CancellationToken cancellationToken);

/// <summary>
/// Raised by a handler to reject a request for a business reason. Distinguished from an
/// unexpected exception so the frontend gets an actionable message instead of "internal error".
/// </summary>
public sealed class RpcException : Exception
{
    public string Code { get; }
    public bool Retryable { get; }
    public Dictionary<string, string[]>? ValidationErrors { get; }

    public RpcException(string code, string message, bool retryable = false,
        Dictionary<string, string[]>? validationErrors = null)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
        ValidationErrors = validationErrors;
    }

    public static RpcException NotFound(string message) => new(ErrorCodes.NotFound, message);

    public static RpcException Validation(Dictionary<string, string[]> errors) =>
        new(ErrorCodes.Validation, "The submitted values are not valid.", false, errors);

    public static RpcException BusinessRule(string message) => new(ErrorCodes.BusinessRule, message);
}

/// <summary>
/// Routes a command name to its handler.
///
/// Only narrow business commands are ever registered. There is deliberately no generic
/// "execute_sql" or "run_query" command: the frontend must not be able to express arbitrary
/// database access, and the Tauri capability that launches this process cannot pass arguments.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly Dictionary<string, CommandHandler> _handlers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> RegisteredMethods => _handlers.Keys;

    public void Register(string method, CommandHandler handler)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("Command name is required.", nameof(method));
        }

        if (!_handlers.TryAdd(method, handler))
        {
            throw new InvalidOperationException($"Command '{method}' is already registered.");
        }
    }

    /// <summary>
    /// Executes a request, converting any failure into a structured error response. This never
    /// throws: an unhandled exception here would kill the sidecar and take the UI down with it.
    /// </summary>
    public async Task<RpcResponse> DispatchAsync(RpcRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return RpcResponse.Fail(request.Id, ErrorCodes.BadRequest, "Request is missing a method name.");
        }

        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            return RpcResponse.Fail(
                request.Id, ErrorCodes.UnknownMethod, $"Unknown command '{request.Method}'.");
        }

        try
        {
            var result = await handler(request.Params, cancellationToken);
            return RpcResponse.Ok(request.Id, result);
        }
        catch (RpcException ex)
        {
            return RpcResponse.Fail(request.Id, ex.Code, ex.Message, ex.Retryable, ex.ValidationErrors);
        }
        catch (JsonException ex)
        {
            return RpcResponse.Fail(
                request.Id, ErrorCodes.BadRequest, $"Request parameters could not be read: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Log the detail to stderr; return a safe message. Stack traces must never reach
            // stdout, which carries protocol only.
            Console.Error.WriteLine($"[error] {request.Method}: {ex}");

            return RpcResponse.Fail(
                request.Id, ErrorCodes.Internal,
                $"The '{request.Method}' command failed: {ex.Message}", retryable: true);
        }
    }

    /// <summary>Deserializes command parameters, or throws a structured bad-request error.</summary>
    public static T RequireParams<T>(JsonElement? parameters) where T : notnull
    {
        if (parameters is null || parameters.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new RpcException(ErrorCodes.BadRequest, "This command requires parameters.");
        }

        var value = parameters.Value.Deserialize<T>(RpcJson.Options);

        if (value is null)
        {
            throw new RpcException(ErrorCodes.BadRequest, "Command parameters could not be read.");
        }

        return value;
    }

    /// <summary>Deserializes optional parameters, falling back to defaults when absent.</summary>
    public static T ParamsOrDefault<T>(JsonElement? parameters) where T : new()
    {
        if (parameters is null || parameters.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new T();
        }

        return parameters.Value.Deserialize<T>(RpcJson.Options) ?? new T();
    }
}
