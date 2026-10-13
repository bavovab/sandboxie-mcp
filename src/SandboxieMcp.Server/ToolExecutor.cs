using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using SandboxieMcp.Core;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server;

public sealed class ToolExecutor(ServerConfiguration configuration, AuditJournal audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CallToolResult> Run(string operation, string? sandbox, Func<object> action, CancellationToken token = default) =>
        Run(operation, sandbox, _ => Task.FromResult(action()), token);

    public async Task<CallToolResult> Run(string operation, string? sandbox, Func<CancellationToken, Task<object>> action,
        CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.OperationTimeoutSeconds));
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            var result = await action(timeout.Token);
            Record(operation, sandbox, "success");
            return Result(new { status = "success", message = configuration.Language == "ru" ? "Операция завершена." : "Operation completed.", data = result }, false);
        }
        catch (Exception ex) when (ex is SandboxieException or ArgumentException or IOException or UnauthorizedAccessException or
            Win32Exception or EventLogException or OperationCanceledException or JsonException or InvalidDataException)
        {
            var code = ex switch
            {
                SandboxieException se => se.Code,
                OperationCanceledException => token.IsCancellationRequested ? "cancelled" : "timeout",
                ArgumentException or JsonException or InvalidDataException => "invalid_argument",
                _ => "io_error"
            };
            var english = ex is SandboxieException or ArgumentException ? ex.Message : code switch
            {
                "cancelled" => "Waiting was cancelled; already-started changes may still have taken effect.",
                "timeout" => "The operation timed out; inspect its state before retrying.",
                _ => "A Windows or filesystem operation failed. Check permissions and component state."
            };
            Record(operation, sandbox, code);
            return Result(new
            {
                status = "error",
                code,
                message = Messages.Localize(code, english, configuration.Language),
                details = ex is SandboxieException error ? error.Details : null
            }, true);
        }
    }

    private void Record(string operation, string? sandbox, string outcome)
    {
        try { audit.Record(operation, sandbox, outcome); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SandboxieException)
        { Console.Error.WriteLine("Audit write failed; check the state directory permissions."); }
    }

    private static CallToolResult Result(object result, bool error)
    {
        var json = JsonSerializer.Serialize(result, Json);
        return new()
        {
            IsError = error,
            Content = [new TextContentBlock { Text = json }],
            StructuredContent = JsonSerializer.SerializeToElement(result, Json)
        };
    }
}
