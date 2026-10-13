using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class FileTools(FileManager files, ToolExecutor execute)
{
    [McpServerTool(Name = "file_list", ReadOnly = true), Description("List materialized files under a stopped sandbox's storage root. Paths are relative; results are paginated.")]
    public Task<CallToolResult> List(string sandbox, CancellationToken token, string path = "", int offset = 0, int limit = 100) =>
        execute.Run("file_read", sandbox, ct => Task.FromResult(files.List(sandbox, path, offset, limit, ct)), token);

    [McpServerTool(Name = "file_read", ReadOnly = true), Description("Read a materialized sandbox file as strict UTF-8 or base64, within maxFileBytes. Requires a stopped sandbox.")]
    public Task<CallToolResult> Read(string sandbox, string path, CancellationToken token, bool base64 = false) =>
        execute.Run("file_read", sandbox, () => files.Read(sandbox, path, base64), token);

    [McpServerTool(Name = "file_import", Destructive = true), Description("Copy an allowed external file into a stopped sandbox's drive/user/share storage. Existing files require overwrite=true.")]
    public Task<CallToolResult> Import(string sandbox, string sourcePath, string destinationPath, CancellationToken token, bool overwrite = false) =>
        execute.Run("file_import", sandbox, ct => files.Import(sandbox, sourcePath, destinationPath, overwrite, ct), token);

    [McpServerTool(Name = "file_recover", Destructive = true), Description("Copy a materialized file out of a stopped sandbox into an allowed absolute external path. This writes to the host.")]
    public Task<CallToolResult> Recover(string sandbox, string sourcePath, string destinationPath, CancellationToken token, bool overwrite = false) =>
        execute.Run("file_recover", sandbox, ct => files.Recover(sandbox, sourcePath, destinationPath, overwrite, ct), token);

    [McpServerTool(Name = "sandbox_size", ReadOnly = true), Description("Count materialized files and bytes, including snapshots, in a stopped sandbox. Rejects reparse points.")]
    public Task<CallToolResult> Size(string sandbox, CancellationToken token) => execute.Run("file_read", sandbox, ct => Task.FromResult(files.Size(sandbox, ct)), token);

    [McpServerTool(Name = "sandbox_clean", Destructive = true), Description("Permanently delete all content and snapshots of a stopped sandbox while retaining its definition. Respects NeverDelete.")]
    public Task<CallToolResult> Clean(string sandbox, CancellationToken token) => execute.Run("sandbox_clean", sandbox, ct => files.Clean(sandbox, ct), token);
}
