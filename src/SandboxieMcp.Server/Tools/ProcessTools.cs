using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Server.Tools;

[McpServerToolType]
public sealed class ProcessTools(ProcessManager processes, ToolExecutor execute)
{
    [McpServerTool(Name = "process_list", ReadOnly = true), Description("List current-session processes belonging to the specified sandbox.")]
    public Task<CallToolResult> List(string sandbox, CancellationToken token) => execute.Run("process_read", sandbox, () => processes.List(sandbox), token);

    [McpServerTool(Name = "process_info", ReadOnly = true), Description("Read process identity, parent and suspended state after verifying sandbox membership. Command lines are not exposed.")]
    public Task<CallToolResult> Info(string sandbox, uint processId, CancellationToken token) => execute.Run("process_read", sandbox, () => processes.Info(sandbox, processId), token);

    [McpServerTool(Name = "process_start", Destructive = true, OpenWorld = true), Description("Run an absolute executable inside a sandbox with an argument array and optional working directory/environment. Reports whether membership was verified.")]
    public Task<CallToolResult> Start(string sandbox, string executable, string[] arguments, CancellationToken token,
        string? workingDirectory = null, Dictionary<string, string>? environment = null, bool hidden = true) =>
        execute.Run("process_start", sandbox, ct => processes.Start(sandbox, executable, arguments, workingDirectory, environment, hidden, ct), token);

    [McpServerTool(Name = "process_stop", Destructive = true), Description("Stop a verified process or all current-session processes in a sandbox and verify termination. Omit processId to stop the sandbox's processes.")]
    public Task<CallToolResult> Stop(string sandbox, CancellationToken token, uint? processId = null) =>
        execute.Run("process_stop", sandbox, ct => processes.Stop(sandbox, processId, ct), token);

    [McpServerTool(Name = "process_suspend", Destructive = true, Idempotent = true), Description("Suspend or resume a process, or all current-session sandbox processes. Read process_info to verify the returned execution state.")]
    public Task<CallToolResult> Suspend(string sandbox, bool suspended, CancellationToken token, uint? processId = null) =>
        execute.Run("process_suspend", sandbox, () => processes.Suspend(sandbox, processId, suspended), token);

    [McpServerTool(Name = "process_wait", ReadOnly = true), Description("Wait 1..300 seconds for a verified process to exit. Timing out does not terminate it.")]
    public Task<CallToolResult> Wait(string sandbox, uint processId, CancellationToken token, int seconds = 30) =>
        execute.Run("process_read", sandbox, ct => processes.Wait(sandbox, processId, seconds, ct), token);
}
