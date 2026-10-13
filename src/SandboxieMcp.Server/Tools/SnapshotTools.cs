using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class SnapshotTools(SnapshotManager snapshots, ToolExecutor execute)
{
    [McpServerTool(Name = "snapshot_list", ReadOnly = true), Description("List native Sandboxie snapshots and their current/default relationships.")]
    public Task<CallToolResult> List(string sandbox, CancellationToken token) => execute.Run("snapshot_read", sandbox, () => snapshots.List(sandbox), token);

    [McpServerTool(Name = "snapshot_create", Destructive = true), Description("Freeze a stopped, initialized sandbox into a native snapshot layer. The new snapshot becomes current.")]
    public Task<CallToolResult> Create(string sandbox, string name, CancellationToken token) => execute.Run("snapshot_create", sandbox, ct => snapshots.Create(sandbox, name, ct), token);

    [McpServerTool(Name = "snapshot_restore", Destructive = true), Description("Discard current changes and select a native snapshot. Empty snapshotId selects an empty base. Requires a stopped sandbox.")]
    public Task<CallToolResult> Restore(string sandbox, string snapshotId, CancellationToken token) => execute.Run("snapshot_restore", sandbox, ct => snapshots.Restore(sandbox, snapshotId, ct), token);

    [McpServerTool(Name = "snapshot_delete", Destructive = true), Description("Delete an unused leaf snapshot. Current/default snapshots and ancestors cannot be deleted; automatic layer merging is not performed.")]
    public Task<CallToolResult> Delete(string sandbox, string snapshotId, CancellationToken token) => execute.Run("snapshot_delete", sandbox, ct => snapshots.Delete(sandbox, snapshotId, ct), token);

    [McpServerTool(Name = "snapshot_update", Destructive = false), Description("Update snapshot name/description or designate its default. makeDefault=true with an empty ID clears the default.")]
    public Task<CallToolResult> Update(string sandbox, string snapshotId, CancellationToken token, string? name = null,
        string? description = null, bool makeDefault = false) => execute.Run("snapshot_write", sandbox,
            ct => snapshots.Update(sandbox, snapshotId, name, description, makeDefault, ct), token);
}
