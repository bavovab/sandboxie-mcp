using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Integration;

public sealed class NativeSandboxieApi : ISandboxieApi
{
    private const int BoxNameCharacters = 40;
    private const int RunRequestBytes = 124;
    private const int RunCommandOffset = 88;
    private readonly nint module;
    private readonly object sync = new();
    private bool disposed;
    private readonly string? configurationPassword;
    private readonly Dictionary<uint, (string Sandbox, ulong Created, SafeProcessHandle Handle)> startedProcesses = [];
    public string InstallationPath { get; }
    public string Version { get; }

    public NativeSandboxieApi(string? installationPath, string? configurationPasswordEnvironmentVariable = null)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Sandboxie MCP requires Windows x64.");
        InstallationPath = PathSafety.Absolute(installationPath ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sandboxie-Plus"));
        PathSafety.NoLinks(InstallationPath);
        var library = Path.Combine(InstallationPath, "SbieDll.dll");
        if (!File.Exists(library)) throw new SandboxieException("not_found", "SbieDll.dll was not found in sandboxiePath.");
        module = LoadLibraryEx(library, 0, 0x1100); // Search only this DLL's directory and System32.
        if (module == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot load Sandboxie x64 API.");
        Version = FileVersionInfo.GetVersionInfo(Path.Combine(InstallationPath, "SandMan.exe")).ProductVersion ?? "unknown";
        if (configurationPasswordEnvironmentVariable is not null)
            configurationPassword = Environment.GetEnvironmentVariable(configurationPasswordEnvironmentVariable)
                ?? throw new ArgumentException("The configuration password environment variable is not set.");
    }

    private T Export<T>(string name) where T : Delegate
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    }

    private static void Check(int status) => Check(unchecked((uint)status));
    private static void Check(uint status)
    {
        if (status != 0) throw new SandboxieException("native_error", "Sandboxie returned a nonzero status.",
            new { status = $"0x{status:X8}" });
    }

    public uint Features
    {
        get
        {
            lock (sync)
            {
                Check(Export<QueryDriver>("SbieApi_QueryDrvInfo")(0, out var flags, 4));
                return flags;
            }
        }
    }

    public IReadOnlyList<string> GetSandboxes()
    {
        lock (sync)
        {
            var result = new List<string>();
            var index = -1;
            var enumerate = Export<EnumBoxes>("SbieApi_EnumBoxesEx");
            while (true)
            {
                var name = new StringBuilder(BoxNameCharacters);
                index = enumerate(index, name, 1);
                if (index == -1) break;
                if (result.Count >= 4096) throw new SandboxieException("native_error", "Too many sandbox entries.");
                var section = name.ToString();
                if (!section.Equals("GlobalSettings", StringComparison.OrdinalIgnoreCase) &&
                    !section.StartsWith("UserSettings", StringComparison.OrdinalIgnoreCase) &&
                    !section.StartsWith("Template_", StringComparison.OrdinalIgnoreCase) &&
                    Query(section, "Enabled").Length > 0) result.Add(section);
            }
            return result;
        }
    }

    public bool IsEnabled(string sandbox)
    {
        lock (sync) return Export<IsBoxEnabled>("SbieApi_IsBoxEnabled")(sandbox) == 0;
    }

    public string[] Query(string? section, string? setting, bool effective = false, bool expand = false)
    {
        lock (sync)
        {
            var query = Export<QueryConf>("SbieApi_QueryConf");
            var result = new List<string>();
            var flags = (expand ? 0u : 0x20000000u) | (effective ? 0u : 0x50000000u);
            for (uint i = 0; i < 8192; i++)
            {
                var buffer = new StringBuilder(8192);
                var status = query(section, setting, flags | i, buffer, (uint)buffer.Capacity * 2);
                if (unchecked((uint)status) is 0x8000001A or 0xC0000034 or 0xC000008B) return [.. result];
                Check(status);
                result.Add(buffer.ToString());
            }
            throw new SandboxieException("native_error", "Configuration query capacity exceeded.");
        }
    }

    public void Update(string section, string setting, string? value, char operation = 's')
    {
        lock (sync) Check(Export<UpdateConf>("SbieDll_UpdateConf")(operation, configurationPassword, section, setting, value));
    }

    public void Reload()
    {
        lock (sync) Check(Export<ReloadConf>("SbieApi_ReloadConf")(uint.MaxValue, 0));
    }

    public SandboxPaths GetPaths(string sandbox)
    {
        lock (sync)
        {
            var query = Export<QueryBoxPath>("SbieApi_QueryBoxPath");
            uint fileSize = 32768, registrySize = 32768, ipcSize = 32768;
            var file = new StringBuilder(16384);
            var registry = new StringBuilder(16384);
            var ipc = new StringBuilder(16384);
            Check(query(sandbox, file, registry, ipc, ref fileSize, ref registrySize, ref ipcSize));
            _ = Export<TranslatePath>("SbieDll_TranslateNtToDosPath")(file);
            return new(PathSafety.Absolute(file.ToString()), registry.ToString(), ipc.ToString());
        }
    }

    public BoxedProcess? QueryProcess(uint pid)
    {
        lock (sync)
        {
            var box = new StringBuilder(BoxNameCharacters);
            var image = new StringBuilder(1024);
            var sid = new StringBuilder(96);
            var status = Export<QueryProcessEx>("SbieApi_QueryProcessEx2")((nint)pid, 1024, box, image, sid,
                out var session, out var created);
            return status == 0 ? new(pid, box.ToString(), image.ToString(), session, created) : null;
        }
    }

    public IReadOnlyList<BoxedProcess> GetProcesses(string sandbox, bool allSessions = false)
    {
        lock (sync)
        {
            var box = sandbox.PadRight(BoxNameCharacters - 1, '\0');
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Check(Export<EnumProcessCount>("SbieApi_EnumProcessEx")(box, (byte)(allSessions ? 1 : 0), uint.MaxValue, 0, out var count));
                if (count == 0) return [];
                if (count > 65536) throw new SandboxieException("native_error", "Sandboxie process-list capacity exceeded.");
                var ids = new uint[count + 128];
                count = (uint)ids.Length;
                Check(Export<EnumProcessList>("SbieApi_EnumProcessEx")(box, (byte)(allSessions ? 1 : 0), uint.MaxValue, ids, ref count));
                if (count >= ids.Length) continue;
                var result = new List<BoxedProcess>();
                for (var i = 0; i < count; i++) if (QueryProcess(ids[i]) is { } info) result.Add(info);
                return result;
            }
            throw new SandboxieException("native_error", "The process list kept growing during enumeration; retry the read.");
        }
    }

    public bool HasProcesses(string sandbox, bool allSessions = true)
    {
        lock (sync)
        {
            Check(Export<EnumProcessCount>("SbieApi_EnumProcessEx")(sandbox.PadRight(BoxNameCharacters - 1, '\0'),
                (byte)(allSessions ? 1 : 0), uint.MaxValue, 0, out var count));
            return count > 0;
        }
    }

    public LaunchResult Run(string sandbox, string commandLine, string directory, bool hidden,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        lock (sync)
        {
            return RunWithEnvironment(sandbox, commandLine, directory, hidden, environment ?? new Dictionary<string, string>());
        }
    }

    private LaunchResult RunWithEnvironment(string sandbox, string commandLine, string directory, bool hidden,
        IReadOnlyDictionary<string, string> environment)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "SystemDrive", "ALLUSERSPROFILE", "windir", "ComSpec", "PATH", "TEMP", "TMP", "USERPROFILE",
            "APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "PUBLIC",
            "HOMEDRIVE", "HOMEPATH", "USERNAME", "COMPUTERNAME", "OS", "PATHEXT", "PSModulePath",
            "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "PROCESSOR_LEVEL", "PROCESSOR_REVISION",
            "NUMBER_OF_PROCESSORS", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432", "ProgramW6432" })
            if (Environment.GetEnvironmentVariable(name) is { } value) merged[name] = value;
        foreach (var pair in environment)
        {
            if (pair.Key.Length is < 1 or > 128 || pair.Key.Contains('=') || pair.Key.Contains('\0') ||
                pair.Value.Contains('\0') || pair.Value.Length > 8192)
                throw new ArgumentException("Invalid environment variable.");
            merged[pair.Key] = pair.Value;
        }
        var env = string.Join('\0', merged.Select(x => x.Key + "=" + x.Value)) + "\0";
        var parts = new[] { commandLine, directory, env };
        var request = new byte[RunRequestBytes + parts.Sum(x => (x.Length + 1) * 2)];
        Encoding.Unicode.GetBytes(sandbox).CopyTo(request, 8);
        var cursor = RunRequestBytes;
        for (var i = 0; i < parts.Length; i++)
        {
            BitConverter.TryWriteBytes(request.AsSpan(RunCommandOffset + i * 8), cursor);
            BitConverter.TryWriteBytes(request.AsSpan(RunCommandOffset + 4 + i * 8), parts[i].Length);
            Encoding.Unicode.GetBytes(parts[i]).CopyTo(request, cursor);
            cursor += (parts[i].Length + 1) * 2;
        }
        BitConverter.TryWriteBytes(request.AsSpan(112), 1);
        BitConverter.TryWriteBytes(request.AsSpan(116), hidden ? 0 : 1);
        var response = CallServer(0x1205, request);
        if (response.Length < 32) throw new SandboxieException("native_error", "Invalid process-creation response.");
        CloseHandle((nint)BitConverter.ToInt64(response, 16));
        var handle = new SafeProcessHandle((nint)BitConverter.ToInt64(response, 8), true);
        if (!GetProcessTimes(handle, out var created, out _, out _, out _))
        { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        var pid = BitConverter.ToUInt32(response, 24);
        if (startedProcesses.Count >= 256)
        {
            var oldest = startedProcesses.First(); oldest.Value.Handle.Dispose(); startedProcesses.Remove(oldest.Key);
        }
        if (startedProcesses.Remove(pid, out var previous)) previous.Handle.Dispose();
        startedProcesses[pid] = (sandbox, created, handle);
        return new(pid, BitConverter.ToUInt32(response, 28));
    }

    public StartedProcessStatus? QueryStartedProcess(uint pid)
    {
        lock (sync)
        {
            if (!startedProcesses.TryGetValue(pid, out var process)) return null;
            var wait = WaitForSingleObject(process.Handle, 0);
            if (wait == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            int? exitCode = null;
            if (wait == 0)
            {
                if (!GetExitCodeProcess(process.Handle, out var code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                exitCode = unchecked((int)code);
            }
            return new(pid, process.Sandbox, process.Created, wait == 0, exitCode);
        }
    }

    public void SetSuspended(string sandbox, uint? pid, bool suspended)
    {
        var request = new byte[pid.HasValue ? 16 : 96];
        if (pid is { } id)
        {
            BitConverter.TryWriteBytes(request.AsSpan(8), id);
            request[12] = (byte)(suspended ? 1 : 0);
        }
        else
        {
            BitConverter.TryWriteBytes(request.AsSpan(8), uint.MaxValue);
            Encoding.Unicode.GetBytes(sandbox).CopyTo(request, 12);
            request[92] = (byte)(suspended ? 1 : 0);
        }
        _ = CallServer(pid.HasValue ? 0x120Au : 0x120Bu, request);
    }

    public ProcessExecutionInfo GetProcessDetails(uint pid)
    {
        var request = new byte[16];
        BitConverter.TryWriteBytes(request.AsSpan(8), pid);
        BitConverter.TryWriteBytes(request.AsSpan(12), 3); // Basic + execution info; no command-line secrets.
        var response = CallServer(0x1209, request);
        if (response.Length < 17) throw new SandboxieException("native_error", "Invalid process-info response.");
        return new(BitConverter.ToUInt32(response, 8), BitConverter.ToUInt32(response, 12), response[16] != 0);
    }

    public void Terminate(string sandbox, uint? pid)
    {
        lock (sync)
        {
            var success = pid is { } id ? Export<KillOne>("SbieDll_KillOne")(id) :
                Export<KillAll>("SbieDll_KillAll")(uint.MaxValue, sandbox);
            if (success == 0) throw new SandboxieException("native_error", "Sandboxie could not terminate the process(es).");
        }
    }

    public void Mount(string sandbox, string? password, bool protect)
    {
        lock (sync)
            if (Export<MountBox>("SbieDll_Mount")(sandbox, password, (byte)(protect ? 1 : 0)) == 0)
                throw new SandboxieException("native_error", "Sandboxie could not mount the image.");
    }

    public void Unmount(string sandbox)
    {
        lock (sync)
            if (Export<UnmountBox>("SbieDll_Unmount")(sandbox) == 0)
                throw new SandboxieException("native_error", "Sandboxie could not unmount the image.");
    }

    public void CreateImage(string sandbox, long sizeKiB, string? password)
    {
        var root = @"\??\" + GetPaths(sandbox).FileRoot;
        if ((password?.Length ?? 0) > 128) throw new ArgumentException("Image passwords are limited to 128 characters.");
        var buffer = new byte[280 + root.Length * 2];
        BitConverter.TryWriteBytes(buffer.AsSpan(8), sizeKiB);
        Encoding.Unicode.GetBytes(password ?? "").CopyTo(buffer, 16);
        Encoding.Unicode.GetBytes(root).CopyTo(buffer, 274);
        _ = CallServer(0x1D01, buffer);
    }

    public object QueryImage(string sandbox)
    {
        var buffer = new byte[8 + 256 * 2];
        var root = GetPaths(sandbox).RegistryRoot;
        if (root.Length >= 256) throw new ArgumentException("Image registry root exceeds protocol capacity.");
        Encoding.Unicode.GetBytes(root).CopyTo(buffer, 8);
        var result = CallServer(0x1D05, buffer);
        if (result.Length < 26) throw new SandboxieException("native_error", "Invalid image response.");
        return new
        {
            diskBytes = BitConverter.ToInt64(result, 8),
            usedBytes = BitConverter.ToInt64(result, 16),
            mountRoot = Encoding.Unicode.GetString(result, 24, result.Length - 24).TrimEnd('\0')
        };
    }

    private byte[] CallServer(uint messageId, byte[] buffer)
    {
        lock (sync)
        {
            BitConverter.TryWriteBytes(buffer, buffer.Length);
            BitConverter.TryWriteBytes(buffer.AsSpan(4), messageId);
            var memory = Marshal.AllocHGlobal(buffer.Length);
            nint response = 0;
            try
            {
                Marshal.Copy(buffer, 0, memory, buffer.Length);
                response = Export<CallServerDelegate>("SbieDll_CallServer")(memory);
                if (response == 0) throw new SandboxieException("native_error", "Sandboxie service returned no response.");
                var length = Marshal.ReadInt32(response);
                if (length is < 8 or > 1_048_576) throw new SandboxieException("native_error", "Invalid service response length.");
                Check(Marshal.ReadInt32(response, 4));
                var result = new byte[length];
                Marshal.Copy(response, result, 0, length);
                return result;
            }
            finally
            {
                // Password-bearing requests are erased before releasing unmanaged memory.
                Marshal.Copy(new byte[buffer.Length], 0, memory, buffer.Length);
                Array.Clear(buffer);
                Marshal.FreeHGlobal(memory);
                if (response != 0) Export<FreeMem>("SbieDll_FreeMem")(response);
            }
        }
    }

    public IReadOnlyList<SandboxMessage> GetMessages(uint after, int limit)
    {
        lock (sync)
        {
            var result = new List<SandboxMessage>();
            var cursor = after;
            using var currentProcess = Process.GetCurrentProcess();
            var session = (uint)currentProcess.SessionId;
            for (var i = 0; i < limit; i++)
            {
                var bytes = new byte[16384];
                var buffer = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, buffer, bytes.Length);
                    var status = Export<GetMessage>("SbieApi_GetMessage")(ref cursor, session, out var code,
                        out var pid, buffer, (uint)bytes.Length);
                    if (status is 0x8000001A or 0xC0000034) break;
                    if (status == 0xC0000022)
                        throw new SandboxieException("feature_unavailable", "The native popup journal requires Sandboxie's session leader. Use the Windows event-log source for recorded SbieDrv/SbieSvc events.",
                            new { status = "0xC0000022", requirement = "sandboxie_session_leader" });
                    Check(status);
                    if (code == 0) break;
                    Marshal.Copy(buffer, bytes, 0, bytes.Length);
                    var insertions = Encoding.Unicode.GetString(bytes).TrimEnd('\0').Split('\0');
                    result.Add(new(cursor, code, pid, string.Join(" | ", insertions), insertions));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return result;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            foreach (var process in startedProcesses.Values) process.Handle.Dispose();
            startedProcesses.Clear();
            NativeLibrary.Free(module);
        }
    }


    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int EnumBoxes(int index, StringBuilder name, byte ignoreHidden);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int IsBoxEnabled(string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int QueryConf(string? section, string? setting, uint index, StringBuilder buffer, uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate uint UpdateConf(char op, string? password, string section, string setting, string? value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ReloadConf(uint session, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int QueryDriver(uint infoClass, out uint flags, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int QueryBoxPath(string box, StringBuilder file, StringBuilder key, StringBuilder ipc,
        ref uint fileSize, ref uint keySize, ref uint ipcSize);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate byte TranslatePath(StringBuilder path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int QueryProcessEx(nint pid, uint imageChars, StringBuilder box, StringBuilder image,
        StringBuilder sid, out uint session, out ulong creation);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int EnumProcessList(string box, byte allSessions, uint session, [Out] uint[] pids, ref uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int EnumProcessCount(string box, byte allSessions, uint session, nint pids, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate byte KillOne(uint pid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate byte KillAll(uint session, string box);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate byte MountBox(string box, string? password, byte protect);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] private delegate byte UnmountBox(string box);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint CallServerDelegate(nint request);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void FreeMem(nint memory);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate uint GetMessage(ref uint number, uint session, out uint code, out uint pid, nint buffer, uint bytes);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string name, nint file, uint flags);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out ulong creation, out ulong exit, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
