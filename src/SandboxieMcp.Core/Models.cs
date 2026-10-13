namespace SandboxieMcp.Core;

public sealed record SandboxPaths(string FileRoot, string RegistryRoot, string IpcRoot);
public sealed record BoxedProcess(uint ProcessId, string Sandbox, string ImageName, uint SessionId, ulong CreationTime);
public sealed record LaunchResult(uint ProcessId, uint ThreadId);
public sealed record ProcessExecutionInfo(uint ParentId, uint Flags, bool Suspended);
public sealed record StartedProcessStatus(uint ProcessId, string Sandbox, ulong CreationTime, bool Exited, int? ExitCode);
public sealed record SandboxMessage(uint Number, uint Code, uint ProcessId, string Text, string[] Insertions);
public sealed record SandboxSummary(string Name, bool Enabled, int ProcessCount);
public sealed record ConfigurationSnapshot(string Section, IReadOnlyDictionary<string, string[]> Settings, string Revision);
public sealed record SettingChange(string Setting, string[] Values);
public sealed record FileEntry(string Path, bool Directory, long Bytes, DateTime LastWriteUtc);
public sealed record SnapshotInfo(string Id, string Name, string? Parent, string? Description, long CreatedUnix);
public sealed record SnapshotState(string? Current, string? Default, IReadOnlyList<SnapshotInfo> Snapshots);

public interface ISandboxieApi : IDisposable
{
    string InstallationPath { get; }
    string Version { get; }
    uint Features { get; }
    IReadOnlyList<string> GetSandboxes();
    bool IsEnabled(string sandbox);
    string[] Query(string? section, string? setting, bool effective = false, bool expand = false);
    void Update(string section, string setting, string? value, char operation = 's');
    void Reload();
    SandboxPaths GetPaths(string sandbox);
    IReadOnlyList<BoxedProcess> GetProcesses(string sandbox, bool allSessions = false);
    bool HasProcesses(string sandbox, bool allSessions = true);
    BoxedProcess? QueryProcess(uint pid);
    StartedProcessStatus? QueryStartedProcess(uint pid);
    LaunchResult Run(string sandbox, string commandLine, string directory, bool hidden, IReadOnlyDictionary<string, string>? environment = null);
    void Terminate(string sandbox, uint? pid);
    void SetSuspended(string sandbox, uint? pid, bool suspended);
    ProcessExecutionInfo GetProcessDetails(uint pid);
    void Mount(string sandbox, string? password, bool protect);
    void Unmount(string sandbox);
    void CreateImage(string sandbox, long sizeKiB, string? password);
    object QueryImage(string sandbox);
    IReadOnlyList<SandboxMessage> GetMessages(uint after, int limit);
}
