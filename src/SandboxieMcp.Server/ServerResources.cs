using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server;

[McpServerResourceType]
public sealed class ServerResources(SandboxManager boxes, DiagnosticsManager diagnostics, ToolExecutor execute)
{
    [McpServerResource(UriTemplate = "sandboxie://status", Name = "status", MimeType = "application/json"), Description("Runtime version, service state and capabilities.")]
    public async Task<string> Status(CancellationToken token) =>
        JsonSerializer.Serialize((await execute.Run("diagnostics", null, diagnostics.Status, token)).StructuredContent);

    [McpServerResource(UriTemplate = "sandboxie://sandboxes", Name = "sandboxes", MimeType = "application/json"), Description("Sandboxes visible under the access policy.")]
    public async Task<string> Sandboxes(CancellationToken token) =>
        JsonSerializer.Serialize((await execute.Run("sandbox_read", null, () => boxes.List(), token)).StructuredContent);

    [McpServerResource(UriTemplate = "sandboxie://policy", Name = "policy", MimeType = "application/json"), Description("The configured permission profile and its explicit restrictions; contains no secrets.")]
    public string Policy() => JsonSerializer.Serialize(boxes.Configuration.Policy,
        SandboxieMcp.Core.Configuration.ServerConfiguration.JsonOptions);
}
