using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class SandboxTools(SandboxManager boxes, ToolExecutor execute)
{
    [McpServerTool(Name = "sandbox_list", ReadOnly = true), Description("List sandboxes visible under the current access policy, with enabled state and process counts.")]
    public Task<CallToolResult> List(CancellationToken token) => execute.Run("sandbox_read", null, () => boxes.List(), token);

    [McpServerTool(Name = "sandbox_info", ReadOnly = true), Description("Read a sandbox's resolved storage roots, enabled state, process count and local configuration revision.")]
    public Task<CallToolResult> Info(string sandbox, CancellationToken token) => execute.Run("sandbox_read", sandbox,
        () => new
        {
            name = boxes.RequireBox(sandbox, "sandbox_read"),
            paths = boxes.Paths(sandbox, "sandbox_read"),
            enabled = boxes.Api.IsEnabled(sandbox),
            processCount = boxes.Api.GetProcesses(sandbox).Count,
            configuration = boxes.ReadConfiguration(sandbox)
        }, token);

    [McpServerTool(Name = "sandbox_create", Destructive = false), Description("Create a named sandbox. Optionally copy another sandbox's configuration; the copy is subject to setting permissions.")]
    public Task<CallToolResult> Create(string sandbox, CancellationToken token, string? copyFrom = null) =>
        execute.Run("sandbox_create", sandbox, ct => boxes.Create(sandbox, copyFrom, ct), token);

    [McpServerTool(Name = "sandbox_rename", Destructive = true), Description("Rename a stopped sandbox while preserving its current file storage root. Read the returned backup and root.")]
    public Task<CallToolResult> Rename(string sandbox, string newName, CancellationToken token) =>
        execute.Run("sandbox_rename", sandbox, ct => boxes.Rename(sandbox, newName, ct), token);

    [McpServerTool(Name = "sandbox_delete", Destructive = true), Description("Delete an empty, stopped sandbox definition. Content cleanup is a separate operation; deletion protection is respected.")]
    public Task<CallToolResult> Delete(string sandbox, CancellationToken token) =>
        execute.Run("sandbox_delete", sandbox, ct => boxes.Delete(sandbox, ct), token);

    [McpServerTool(Name = "configuration_read", ReadOnly = true), Description("Read local or effective settings and a revision. Secret settings are omitted. Global and template sections require global_config permission.")]
    public Task<CallToolResult> Read(string section, CancellationToken token, bool effective = false) =>
        execute.Run("config_read", section, () => boxes.ReadConfiguration(section, effective), token);

    [McpServerTool(Name = "configuration_preview", ReadOnly = true), Description("Validate proposed setting changes and show the before/after values without writing. Requires the same permissions as applying them.")]
    public Task<CallToolResult> Preview(string section, SettingChange[] changes, CancellationToken token) =>
        execute.Run("config_write", section, () => boxes.Preview(section, changes), token);

    [McpServerTool(Name = "configuration_apply", Destructive = true), Description("Apply validated setting changes against an expected local revision. Empty values delete a setting. Produces a backup and reports partial failures.")]
    public Task<CallToolResult> Apply(string section, SettingChange[] changes, string expectedRevision, CancellationToken token) =>
        execute.Run("config_write", section, ct => boxes.Apply(section, changes, expectedRevision, ct), token);

    [McpServerTool(Name = "configuration_reload", Destructive = false, Idempotent = true), Description("Reload Sandboxie configuration. Existing sandboxed processes may need restarting to use changed rules.")]
    public Task<CallToolResult> Reload(CancellationToken token) => execute.Run("global_config", null, () =>
    {
        boxes.Guard.Demand("global_config"); boxes.Api.Reload(); return new { reloaded = true, existingProcessesUnchanged = true };
    }, token);
}
