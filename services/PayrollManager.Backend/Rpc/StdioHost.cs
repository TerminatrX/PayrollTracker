using System.Text;
using System.Text.Json;
using PayrollManager.Backend.Contracts;

namespace PayrollManager.Backend.Rpc;

/// <summary>
/// Newline-delimited JSON-RPC over stdin/stdout.
///
/// THE INVARIANT: stdout carries protocol and nothing else. One request per line in, one
/// response per line out. A stray Console.WriteLine anywhere in the process corrupts the
/// stream and the host can no longer parse any response. All logging goes to stderr.
///
/// Chosen over a localhost HTTP server to avoid port conflicts, firewall prompts, service
/// discovery, and accidentally exposing a payroll endpoint to the machine.
/// </summary>
public sealed class StdioHost
{
    private readonly CommandDispatcher _dispatcher;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public StdioHost(CommandDispatcher dispatcher, TextReader input, TextWriter output)
    {
        _dispatcher = dispatcher;
        _input = input;
        _output = output;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken);

            // Null means stdin closed - the host exited, so we should too. This is the normal
            // shutdown path and must not be treated as an error.
            if (line is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = await HandleLineAsync(line, cancellationToken);
            await WriteResponseAsync(response);
        }
    }

    private async Task<RpcResponse> HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        RpcRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<RpcRequest>(line, RpcJson.Options);
        }
        catch (JsonException ex)
        {
            // No id is available, so the host cannot correlate this. Reply with an empty id
            // rather than staying silent, which would hang a caller waiting on a response.
            return RpcResponse.Fail(
                string.Empty, ErrorCodes.BadRequest, $"Request was not valid JSON: {ex.Message}");
        }

        if (request is null)
        {
            return RpcResponse.Fail(string.Empty, ErrorCodes.BadRequest, "Request was empty.");
        }

        return await _dispatcher.DispatchAsync(request, cancellationToken);
    }

    private async Task WriteResponseAsync(RpcResponse response)
    {
        string json;

        try
        {
            json = JsonSerializer.Serialize(response, RpcJson.Options);
        }
        catch (Exception ex)
        {
            // Serializing the RESULT failed. Fall back to an error response that is guaranteed
            // to serialize, so the caller gets something rather than hanging forever.
            Console.Error.WriteLine($"[error] failed to serialize response: {ex}");

            json = JsonSerializer.Serialize(
                RpcResponse.Fail(response.Id, ErrorCodes.Internal, "The response could not be serialized."),
                RpcJson.Options);
        }

        // A response must occupy exactly one line; embedded newlines would desynchronize the
        // stream. The serializer is configured non-indented, and JSON escapes newlines inside
        // string values, so this holds - assert it rather than trusting it silently.
        if (json.Contains('\n') || json.Contains('\r'))
        {
            json = json.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        await _output.WriteLineAsync(json);
        await _output.FlushAsync();
    }
}
