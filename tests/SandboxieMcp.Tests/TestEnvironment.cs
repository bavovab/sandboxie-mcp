using SandboxieMcp.Core;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Security;
using SandboxieMcp.Core.Services;

namespace SandboxieMcp.Tests;

internal sealed class TestEnvironment : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "SandboxieMcpTests", Guid.NewGuid().ToString("N"));
    public TestApi Api { get; }
    public SandboxManager Boxes { get; }

    public TestEnvironment(AccessPolicy? policy = null)
    {
        Directory.CreateDirectory(Root);
        Api = new TestApi(Path.Combine(Root, "box"));
        Api.Update("TestBox", "Enabled", "y");
        var config = new ServerConfiguration
        {
            StateDirectory = Path.Combine(Root, "state"),
            Policy = policy ?? new() { Profile = AccessProfile.Full }
        };
        Boxes = new(Api, config, new PolicyGuard(config.Policy));
    }

    public void Dispose()
    {
        if (!PathSafety.IsWithin(Path.Combine(Path.GetTempPath(), "SandboxieMcpTests"), Root))
            throw new InvalidOperationException("Invalid test cleanup root.");
        Directory.Delete(Root, true);
        Boxes.MutationGate.Dispose();
    }
}

// This in-memory test double is never linked into the server or release package.
internal sealed class TestApi(string root) : ISandboxieApi
{
    private readonly Dictionary<string, Dictionary<string, string[]>> sections = new(StringComparer.OrdinalIgnoreCase);
    public string InstallationPath => root;
    public string Version => "1.18.5";
    public uint Features { get; set; } = 0;
    public bool BusyInOtherSession { get; set; }
    public StartedProcessStatus? Started { get; set; }
    public int Writes { get; private set; }
    public string? FailOnSetting { get; set; }
    public IReadOnlyList<string> GetSandboxes() => sections.Where(x => x.Value.ContainsKey("Enabled")).Select(x => x.Key).ToArray();
    public bool IsEnabled(string sandbox) => Query(sandbox, "Enabled").Contains("y");
    public string[] Query(string? section, string? setting, bool effective = false, bool expand = false)
    {
        if (section is null) return sections.Keys.ToArray();
        if (!sections.TryGetValue(section, out var values)) return [];
        return setting is null ? values.Keys.ToArray() : values.GetValueOrDefault(setting, []).ToArray();
    }
    public void Update(string section, string setting, string? value, char operation = 's')
    {
        if (setting == FailOnSetting) throw new SandboxieException("native_error", "Injected write failure.");
        Writes++;
        if (setting == "*") { sections.Remove(section); return; }
        if (!sections.TryGetValue(section, out var values)) sections[section] = values = new(StringComparer.OrdinalIgnoreCase);
        if (value is null) values.Remove(setting);
        else values[setting] = operation == 'a' ? [.. values.GetValueOrDefault(setting, []), value] : [value];
    }
    public void Reload() { }
    public SandboxPaths GetPaths(string sandbox) => new(sandbox == "TestBox" ? root : root + "-" + sandbox, @"\Registry\Test", @"\Sandbox\Test");
    public IReadOnlyList<BoxedProcess> GetProcesses(string sandbox, bool allSessions = false) => allSessions && BusyInOtherSession ?
        [new(1, sandbox, "test.exe", 2, 1)] : [];
    public BoxedProcess? QueryProcess(uint pid) => null;
    public bool HasProcesses(string sandbox, bool allSessions = true) => allSessions && BusyInOtherSession;
    public StartedProcessStatus? QueryStartedProcess(uint pid) => Started?.ProcessId == pid ? Started : null;
    public LaunchResult Run(string sandbox, string commandLine, string directory, bool hidden, IReadOnlyDictionary<string, string>? environment = null) =>
        throw new NotSupportedException("Native launching belongs to live integration tests.");
    public void Terminate(string sandbox, uint? pid) => throw new NotSupportedException();
    public void SetSuspended(string sandbox, uint? pid, bool suspended) => throw new NotSupportedException();
    public ProcessExecutionInfo GetProcessDetails(uint pid) => throw new NotSupportedException();
    public void Mount(string sandbox, string? password, bool protect) => throw new NotSupportedException();
    public void Unmount(string sandbox) => throw new NotSupportedException();
    public void CreateImage(string sandbox, long sizeKiB, string? password) => throw new NotSupportedException();
    public object QueryImage(string sandbox) => throw new NotSupportedException();
    public IReadOnlyList<SandboxMessage> GetMessages(uint after, int limit) => [];
    public void Dispose() { }
}
