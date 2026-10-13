using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class SystemTools(DiagnosticsManager diagnostics, AuditJournal journal, SandboxManager boxes, ToolExecutor execute)
{
    [McpServerTool(Name = "sandboxie_status", ReadOnly = true), Description("Read Sandboxie version, component states, current access profile and detected feature flags. Does not expose certificates or secrets.")]
    public Task<CallToolResult> Status(CancellationToken token) => execute.Run("diagnostics", null, diagnostics.Status, token);

    [McpServerTool(Name = "sandboxie_messages", ReadOnly = true), Description("Read recorded SbieDrv/SbieSvc Windows System events after a numeric cursor. source=native requests the current-session popup journal, which requires Sandboxie's session leader. Sources have separate cursors. Requires global_config because messages may contain cross-sandbox paths.")]
    public Task<CallToolResult> Messages(CancellationToken token, long after = 0, int limit = 100, string source = "event-log") =>
        execute.Run("message_read", null, () => diagnostics.Messages(after, limit, source), token);

    [McpServerTool(Name = "audit_read", ReadOnly = true), Description("Read recent metadata-only MCP audit entries filtered by sandbox visibility. Arguments, command lines and secrets are never logged.")]
    public Task<CallToolResult> Audit(CancellationToken token, int limit = 50) => execute.Run("audit_read", null, () => journal.Read(limit, boxes.Guard), token);

    [McpServerTool(Name = "sandboxie_component", Destructive = true), Description("Start or stop Sandboxie's service or driver. Requires maintenance permission and Windows rights; stopping is refused while sandboxed processes are running.")]
    public Task<CallToolResult> Component(string component, string action, CancellationToken token) =>
        execute.Run("maintenance", null, ct => diagnostics.Maintain(component, action, ct), token);
}

[McpServerToolType]
public sealed class ImageTools(ImageManager images, ToolExecutor execute)
{
    [McpServerTool(Name = "image_info", ReadOnly = true), Description("Query the mounted image associated with a sandbox through Sandboxie's mount manager.")]
    public Task<CallToolResult> Info(string sandbox, CancellationToken token) => execute.Run("image_read", sandbox, () => images.Query(sandbox), token);

    [McpServerTool(Name = "image_create", Destructive = true), Description("Create an image for an empty stopped sandbox. Requires an active encryption entitlement. Passwords are referenced through user-configured environment-variable names.")]
    public Task<CallToolResult> Create(string sandbox, long sizeMiB, CancellationToken token, string? secretEnvironmentVariable = null) =>
        execute.Run("image_write", sandbox, ct => images.Create(sandbox, sizeMiB, secretEnvironmentVariable, ct), token);

    [McpServerTool(Name = "image_mount", Destructive = true), Description("Mount a sandbox image with an optional configured secret reference. Password values are not placed on process command lines.")]
    public Task<CallToolResult> Mount(string sandbox, CancellationToken token, string? secretEnvironmentVariable = null, bool protect = true) =>
        execute.Run("image_write", sandbox, ct => images.Mount(sandbox, secretEnvironmentVariable, protect, ct), token);

    [McpServerTool(Name = "image_unmount", Destructive = true), Description("Unmount a stopped sandbox image through the Sandboxie service.")]
    public Task<CallToolResult> Unmount(string sandbox, CancellationToken token) => execute.Run("image_write", sandbox, ct => images.Unmount(sandbox, ct), token);
}
