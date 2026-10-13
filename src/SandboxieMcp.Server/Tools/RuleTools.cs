using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class RuleTools(RuleManager rules, ToolExecutor execute)
{
    [McpServerTool(Name = "rules_read", ReadOnly = true), Description("Read local resource, network and program rules and the revision used to replace them.")]
    public Task<CallToolResult> Read(string sandbox, CancellationToken token) =>
        execute.Run("config_read", sandbox, () => rules.Inventory(sandbox), token);

    [McpServerTool(Name = "resource_rules_replace", Destructive = true), Description("Replace one file, registry, IPC, window-class or COM rule list. Uses Sandboxie's native rule syntax and an expected revision.")]
    public Task<CallToolResult> Resources(string sandbox, string setting, string[] values, string expectedRevision, CancellationToken token) =>
        execute.Run("config_write", sandbox, ct => rules.Resources(sandbox, setting, values, expectedRevision, ct), token);

    [McpServerTool(Name = "program_rules_replace", Destructive = true), Description("Replace forced programs/folders, breakout programs, leader/linger programs or process groups against a revision. Some rules affect isolation.")]
    public Task<CallToolResult> Programs(string sandbox, string setting, string[] values, string expectedRevision, CancellationToken token) =>
        execute.Run("config_write", sandbox, ct => rules.Programs(sandbox, setting, values, expectedRevision, ct), token);

    [McpServerTool(Name = "network_rules_replace", Destructive = true), Description("Replace NetworkAccess rules. Actions are allow/block; protocols Any/TCP/UDP/ICMP. Numeric ports and IP addresses support comma sets and ranges. Read WFP status before relying on enforcement.")]
    public Task<CallToolResult> Network(string sandbox, NetworkRule[] values, string expectedRevision, CancellationToken token) =>
        execute.Run("config_write", sandbox, ct => rules.Network(sandbox, values, expectedRevision, ct), token);
}
