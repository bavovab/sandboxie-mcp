using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SandboxieMcp.Core.Services;

public static class WindowsServices
{
    public static object Status(string name)
    {
        using var handle = Open(name, 0x0004);
        var state = Query(handle.Handle);
        return new { name, state = StateName(state.State), state.ExitCode, state.ServiceExitCode };
    }

    public static async Task<object> Change(string name, string action, CancellationToken token)
    {
        if (name is not ("SbieSvc" or "SbieDrv") || action is not ("start" or "stop"))
            throw new ArgumentException("Only start/stop of SbieSvc or SbieDrv is supported.");
        var desired = action == "start" ? 4u : 1u;
        using var query = Open(name, 0x0004);
        if (Query(query.Handle).State == desired) return Status(name);
        using var handle = Open(name, 0x0004 | (action == "start" ? 0x0010u : 0x0020u));
        if (Query(handle.Handle).State == desired) return Status(name);
        var ok = action == "start" ? StartService(handle.Handle, 0, 0) : ControlService(handle.Handle, 1, out _);
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
        for (var i = 0; i < 600; i++)
        {
            token.ThrowIfCancellationRequested();
            if (Query(handle.Handle).State == desired) return Status(name);
            await Task.Delay(100, token);
        }
        throw new SandboxieException("timeout", "The service did not reach the requested state.");
    }

    private static HandleOwner Open(string name, uint access)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var manager = OpenSCManager(null, null, 1);
        if (manager == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(manager, name, access);
            if (service == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(service);
        }
        finally { CloseServiceHandle(manager); }
    }

    private static ServiceStatus Query(nint handle)
    {
        if (!QueryServiceStatus(handle, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return status;
    }

    private static string StateName(uint state) => state switch
    { 1 => "stopped", 2 => "start_pending", 3 => "stop_pending", 4 => "running", 5 => "continue_pending", 6 => "pause_pending", 7 => "paused", _ => "unknown" };

    private sealed class HandleOwner(nint handle) : IDisposable
    {
        public nint Handle { get; } = handle;
        public void Dispose() => CloseServiceHandle(Handle);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type; public uint State; public uint Controls; public uint ExitCode;
        public uint ServiceExitCode; public uint CheckPoint; public uint WaitHint;
    }
    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint OpenService(nint manager, string service, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatus(nint service, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(nint service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartService(nint service, uint count, nint arguments);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint service);
}
