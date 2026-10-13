using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class TemplateTools(TemplateManager templates, ToolExecutor execute)
{
    [McpServerTool(Name = "template_list", ReadOnly = true), Description("List shared configuration templates. Requires global_config and config_read.")]
    public Task<CallToolResult> List(CancellationToken token) => execute.Run("config_read", null, templates.List, token);

    [McpServerTool(Name = "template_assign", Destructive = true), Description("Replace template assignments against a revision. Templates must exist. Manage custom template sections with configuration_read/apply.")]
    public Task<CallToolResult> Assign(string sandbox, string[] values, string expectedRevision, CancellationToken token) =>
        execute.Run("config_write", sandbox, ct => templates.Assign(sandbox, values, expectedRevision, ct), token);

    [McpServerTool(Name = "template_delete", Destructive = true), Description("Delete an unused local template with a configuration backup. Built-in templates are immutable. Requires global_config and config_write.")]
    public Task<CallToolResult> Delete(string template, CancellationToken token) =>
        execute.Run("config_write", null, ct => templates.Delete(template, ct), token);
}
