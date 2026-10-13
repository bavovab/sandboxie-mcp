namespace SandboxieMcp.Core.Services;

public sealed class DiagnosticsManager(SandboxManager boxes)
{
    public object Status()
    {
        boxes.Guard.Demand("diagnostics");
        var flags = boxes.Api.Features;
        return new
        {
            serverVersion = BuildInfo.Version,
            sandboxieVersion = boxes.Api.Version,
            installationPath = boxes.Api.InstallationPath,
            operatingSystem = Environment.OSVersion.VersionString,
            processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            services = new[] { WindowsServices.Status("SbieSvc"), WindowsServices.Status("SbieDrv") },
            policyProfile = boxes.Configuration.Policy.Profile.ToString(),
            capabilities = new
            {
                certified = (flags & 0x80000000) != 0,
                wfpLoaded = (flags & 1) != 0,
                privacyMode = (flags & 4) != 0,
                compartments = (flags & 8) != 0,
                securityMode = (flags & 0x40) != 0,
                encryption = (flags & 0x80) != 0,
                networkProxy = (flags & 0x100) != 0,
                nativeSnapshots = true,
                archiveFormat = "sandboxie-mcp/1"
            },
            compatibility = boxes.Api.Version == "1.18.5" ? "primary_test_target" : "not_in_verified_version_matrix"
        };
    }

    public object Messages(long after, int limit, string source)
    {
        boxes.Guard.Demand("message_read");
        if (limit is < 1 or > 200) throw new ArgumentException("limit must be 1..200.");
        if (after < 0) throw new ArgumentException("after must be nonnegative.");
        // Global messages may contain paths to sandboxes the caller cannot see.
        boxes.Guard.Demand("global_config");
        if (source == "event-log") return SandboxieEventLog.Read(after, limit);
        if (source != "native") throw new ArgumentException("source must be event-log or native.");
        if (after > uint.MaxValue) throw new ArgumentException("The native message cursor exceeds UInt32 capacity.");
        var messages = boxes.Api.GetMessages((uint)after, limit);
        return new { source = "native", nextCursor = messages.Count > 0 ? messages[^1].Number : after, messages };
    }

    public async Task<object> Maintain(string component, string action, CancellationToken token)
    {
        boxes.Guard.Demand("maintenance");
        if (component is not ("service" or "driver")) throw new ArgumentException("component must be service or driver.");
        await boxes.MutationGate.WaitAsync(token);
        try
        {
            if (action == "stop" && boxes.Api.HasProcesses(""))
                throw new SandboxieException("sandbox_busy", "Stop sandboxed processes before stopping Sandboxie components.");
            return await WindowsServices.Change(component == "service" ? "SbieSvc" : "SbieDrv", action, token);
        }
        finally { boxes.MutationGate.Release(); }
    }
}
