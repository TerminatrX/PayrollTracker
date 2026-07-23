using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayrollManager.Backend.Contracts;

/// <summary>
/// One request from the Tauri host. Newline-delimited JSON on stdin.
/// </summary>
public sealed class RpcRequest
{
    /// <summary>Correlates the response. Echoed back verbatim.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Business command name, e.g. "get_employees".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Command-specific payload; shape depends on <see cref="Method"/>.</summary>
    public JsonElement? Params { get; set; }
}

/// <summary>
/// Structured error. Defined once so every failure surfaces the same shape to the frontend.
/// </summary>
public sealed class RpcError
{
    public string Code { get; set; } = ErrorCodes.Internal;
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// True when retrying the identical request could plausibly succeed. Validation failures
    /// and business-rule rejections are NOT retryable - retrying just fails again.
    /// </summary>
    public bool Retryable { get; set; }

    /// <summary>Field-level validation problems, keyed by field name.</summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}

public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string UnknownMethod = "unknown_method";
    public const string Validation = "validation_failed";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string BusinessRule = "business_rule_violation";
    public const string Internal = "internal_error";
}

public sealed class RpcResponse
{
    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RpcError? Error { get; set; }

    public static RpcResponse Ok(string id, object? result) => new() { Id = id, Result = result };

    public static RpcResponse Fail(string id, string code, string message, bool retryable = false,
        Dictionary<string, string[]>? validationErrors = null) =>
        new()
        {
            Id = id,
            Error = new RpcError
            {
                Code = code,
                Message = message,
                Retryable = retryable,
                ValidationErrors = validationErrors
            }
        };
}

public static class RpcJson
{
    /// <summary>
    /// camelCase to match TypeScript conventions on the other side of the pipe.
    /// Never indented: one response must occupy exactly one line.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // PropertyNamingPolicy does NOT cover dictionary keys. Without this, validation errors
        // come back keyed "FirstName" while the DTO field is "firstName", forcing the frontend
        // to handle two casing conventions to map an error onto its form field.
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,

        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
