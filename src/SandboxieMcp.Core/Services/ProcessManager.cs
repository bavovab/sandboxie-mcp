using System.Diagnostics;
using System.Text;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Services;

public sealed class ProcessManager(SandboxManager sandboxes)
{
    public IReadOnlyList<BoxedProcess> List(string name) => sandboxes.Api.GetProcesses(sandboxes.RequireBox(name, "process_read"));

    public object Info(string name, uint pid)
    {
        _ = sandboxes.RequireBox(name, "process_read");
        var info = RequireProcess(name, pid);
        return new { process = info, details = sandboxes.Api.GetProcessDetails(pid) };
    }

    public async Task<object> Start(string name, string executable, string[] arguments, string? directory,
        Dictionary<string, string>? environment, bool hidden, CancellationToken token)
    {
        name = sandboxes.RequireBox(name, "process_start");
        executable = PathSafety.Absolute(executable);
        PathSafety.NoLinks(executable);
        if (!File.Exists(executable)) throw new SandboxieException("not_found", "The executable does not exist.");
        sandboxes.Guard.DemandExecutable(executable);
        if (arguments is null || arguments.Length > 256 || arguments.Any(x => x is null || x.Contains('\0')))
            throw new ArgumentException("Invalid process arguments.");
        if (environment is { Count: > 64 }) throw new ArgumentException("At most 64 environment variables are allowed.");
        directory = directory is null ? Path.GetDirectoryName(executable)! : PathSafety.Absolute(directory);
        PathSafety.NoLinks(directory);
        if (!Directory.Exists(directory)) throw new SandboxieException("not_found", "The working directory does not exist.");
        var command = string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote));
        if (command.Length > 16000) throw new ArgumentException("The command line is too long.");
        await sandboxes.MutationGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var result = sandboxes.Api.Run(name, command, directory, hidden, environment);
            for (var i = 0; i < 20; i++)
            {
                var actual = sandboxes.Api.QueryProcess(result.ProcessId);
                if (actual is not null)
                {
                    if (!actual.Sandbox.Equals(name, StringComparison.OrdinalIgnoreCase))
                        throw new SandboxieException("verification_failed", "The process belongs to another sandbox.");
                    return new { launched = true, verified = true, process = actual };
                }
                // A short-lived program can exit before the verification query. Report it honestly.
                try { using var process = Process.GetProcessById((int)result.ProcessId); }
                catch (ArgumentException) { return new { launched = true, verified = false, processId = result.ProcessId, state = "exited_before_verification" }; }
                await Task.Delay(50, token);
            }
            throw new SandboxieException("verification_failed", "Cannot confirm sandbox membership after process creation.", new { result.ProcessId });
        }
        finally { sandboxes.MutationGate.Release(); }
    }

    public async Task<object> Stop(string name, uint? pid, CancellationToken token)
    {
        name = sandboxes.RequireBox(name, "process_stop");
        var original = pid is { } id ? RequireProcess(name, id) : null;
        if (original is not null) _ = RequireProcess(name, original.ProcessId, original.CreationTime);
        sandboxes.Api.Terminate(name, pid);
        for (var i = 0; i < 100; i++)
        {
            var remaining = sandboxes.Api.GetProcesses(name);
            if (pid is null ? remaining.Count == 0 : !remaining.Any(x => x.ProcessId == pid && x.CreationTime == original!.CreationTime))
                return new { stopped = true, sandbox = name, processId = pid };
            await Task.Delay(50, token);
        }
        throw new SandboxieException("verification_failed", "Sandboxed processes are still running.");
    }

    public object Suspend(string name, uint? pid, bool suspended)
    {
        name = sandboxes.RequireBox(name, "process_suspend");
        var original = pid is { } id ? new[] { RequireProcess(name, id) } : sandboxes.Api.GetProcesses(name).ToArray();
        sandboxes.Api.SetSuspended(name, pid, suspended);
        var verified = 0;
        foreach (var process in original)
        {
            var current = sandboxes.Api.QueryProcess(process.ProcessId);
            if (current is null || current.CreationTime != process.CreationTime) continue;
            if (sandboxes.Api.GetProcessDetails(process.ProcessId).Suspended != suspended)
                throw new SandboxieException("verification_failed", "The process did not reach the requested execution state.", new { process.ProcessId });
            verified++;
        }
        return new { sandbox = name, processId = pid, suspended, verifiedProcesses = verified, exitedProcesses = original.Length - verified };
    }

    public async Task<object> Wait(string name, uint pid, int seconds, CancellationToken token)
    {
        name = sandboxes.RequireBox(name, "process_read");
        if (seconds is < 1 or > 300) throw new ArgumentException("seconds must be 1..300.");
        if (sandboxes.Api.QueryStartedProcess(pid) is { } known)
        {
            if (!known.Sandbox.Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new SandboxieException("not_found", "The process was launched in another sandbox.");
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var status = sandboxes.Api.QueryStartedProcess(pid);
                if (status is null) throw new SandboxieException("not_found", "The retained process handle is no longer available.");
                if (status.Exited) return new { processId = pid, exited = true, exitCode = status.ExitCode };
                if (deadline.Elapsed >= TimeSpan.FromSeconds(seconds)) return new { processId = pid, exited = false, timedOut = true };
                await Task.Delay(50, token);
            }
        }
        var original = RequireProcess(name, pid);
        using var process = Process.GetProcessById((int)pid);
        _ = RequireProcess(name, pid, original.CreationTime);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new { processId = pid, exited = false, timedOut = true }; }
        return new { processId = pid, exited = true, exitCode = process.ExitCode };
    }

    private BoxedProcess RequireProcess(string name, uint pid, ulong? expectedCreation = null)
    {
        var info = sandboxes.Api.QueryProcess(pid);
        if (info is null || !info.Sandbox.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            (expectedCreation is not null && info.CreationTime != expectedCreation))
            throw new SandboxieException("not_found", "The process does not belong to this sandbox or has already exited.");
        return info;
    }

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"')) return argument;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
}
