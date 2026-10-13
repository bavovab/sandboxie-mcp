using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class ArchiveTools(ArchiveManager archives, ToolExecutor execute)
{
    [McpServerTool(Name = "sandbox_export", Destructive = true), Description("Export local configuration and optional materialized content/snapshots to a new ZIP in sandboxie-mcp/1 format. Requires a stopped sandbox; secret settings and root paths are omitted.")]
    public Task<CallToolResult> Export(string sandbox, string destinationPath, CancellationToken token, bool includeContent = true) =>
        execute.Run("archive_export", sandbox, ct => archives.Export(sandbox, destinationPath, includeContent, ct), token);

    [McpServerTool(Name = "sandbox_import", Destructive = true), Description("Import a sandboxie-mcp/1 ZIP into a new sandbox. Rejects traversal, links and extraction-limit violations. Does not overwrite an existing sandbox.")]
    public Task<CallToolResult> Import(string sandbox, string sourcePath, CancellationToken token) =>
        execute.Run("archive_import", sandbox, ct => archives.Import(sandbox, sourcePath, ct), token);
}
