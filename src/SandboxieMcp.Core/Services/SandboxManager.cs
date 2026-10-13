using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Services;

public sealed partial class SandboxManager(ISandboxieApi api, ServerConfiguration configuration, PolicyGuard guard)
{
    public ISandboxieApi Api { get; } = api;
    public PolicyGuard Guard { get; } = guard;
    public ServerConfiguration Configuration { get; } = configuration;
    public SemaphoreSlim MutationGate { get; } = new(1, 1);

    public IReadOnlyList<SandboxSummary> List()
    {
        Guard.Demand("sandbox_read");
        return [.. Api.GetSandboxes().Where(Guard.CanSeeSandbox)
            .Select(name => new SandboxSummary(name, Api.IsEnabled(name), Api.GetProcesses(name).Count))];
    }

    public string RequireBox(string name, string operation)
    {
        ValidateName(name);
        Guard.Demand(operation, name);
        return Api.GetSandboxes().FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new SandboxieException("not_found", "The sandbox does not exist.");
    }

    public SandboxPaths Paths(string name, string operation)
    {
        var paths = Api.GetPaths(RequireBox(name, operation));
        PathSafety.NoLinks(paths.FileRoot);
        ProtectServerPath(paths.FileRoot);
        var diskRoot = Path.GetPathRoot(paths.FileRoot);
        if (string.Equals(paths.FileRoot.TrimEnd('\\'), diskRoot?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new SandboxieException("unsafe_path", "A volume root cannot be used as sandbox storage.");
        return paths;
    }

    public void ProtectServerPath(string path)
    {
        if (PathSafety.IsWithin(Configuration.StateDirectory, path) ||
            PathSafety.IsWithin(path, Configuration.StateDirectory) ||
            Configuration.ProtectedPaths.Any(x => PathSafety.IsWithin(path, x) || PathSafety.IsWithin(x, path)))
            throw new SandboxieException("access_denied", "MCP configuration and state paths are protected from file operations.");
    }

    public string ExternalPath(string path)
    {
        path = Guard.DemandExternalPath(path);
        ProtectServerPath(path);
        return path;
    }

    public void RequireStopped(string name)
    {
        if (Api.HasProcesses(name))
            throw new SandboxieException("sandbox_busy", "Stop all processes in the sandbox first.");
    }

    public ConfigurationSnapshot ReadConfiguration(string section, bool effective = false)
    {
        ValidateSection(section);
        Guard.DemandSection("config_read", section);
        var data = ReadRaw(section, effective);
        var visible = data.Where(x => !IsSecret(x.Key)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        return new(section, visible, Revision(data));
    }

    internal Dictionary<string, string[]> ReadRaw(string section, bool effective = false) =>
        Api.Query(section, null, effective).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x, x => Api.Query(section, x, effective), StringComparer.OrdinalIgnoreCase);

    public object Preview(string section, SettingChange[] changes)
    {
        ValidateChanges(section, changes);
        var current = ReadConfiguration(section);
        return new
        {
            current.Revision,
            changes = changes.Select(x => new
            {
                x.Setting,
                before = current.Settings.GetValueOrDefault(x.Setting, []),
                after = x.Values
            }),
            restartProcessesRequired = Api.GetSandboxes().Contains(section, StringComparer.OrdinalIgnoreCase) && Api.GetProcesses(section).Count > 0
        };
    }

    public async Task<object> Apply(string section, SettingChange[] changes, string expectedRevision, CancellationToken token)
    {
        ValidateChanges(section, changes);
        await MutationGate.WaitAsync(token);
        try
        {
            var before = ReadRaw(section);
            if (!Revision(before).Equals(expectedRevision, StringComparison.Ordinal))
                throw new SandboxieException("configuration_conflict", "Configuration changed; read it again before writing.");
            var backup = Backup(section, before);
            var completed = new List<string>();
            var attempted = new List<string>();
            try
            {
                var expected = before;
                foreach (var change in changes)
                {
                    token.ThrowIfCancellationRequested();
                    if (Revision(ReadRaw(section)) != Revision(expected))
                        throw new SandboxieException("configuration_conflict", "Another writer changed configuration during the update.");
                    attempted.Add(change.Setting);
                    SetValues(section, change.Setting, change.Values);
                    expected = ReadRaw(section);
                    if (!Api.Query(section, change.Setting).SequenceEqual(change.Values, StringComparer.Ordinal))
                        throw new SandboxieException("verification_failed", "Setting values did not match the requested values.");
                    completed.Add(change.Setting);
                }
                return new
                {
                    section,
                    applied = completed,
                    revision = Revision(ReadRaw(section)),
                    backup,
                    restartProcessesRequired = Api.GetSandboxes().Contains(section, StringComparer.OrdinalIgnoreCase) && Api.GetProcesses(section).Count > 0
                };
            }
            catch (Exception ex) when (attempted.Count > 0)
            {
                throw new SandboxieException("partial_failure", "Configuration update completed only partially.",
                    new { completed, attempted, backup, cause = ex is SandboxieException se ? se.Code : "io_error" });
            }
        }
        finally { MutationGate.Release(); }
    }

    public async Task<object> Create(string name, string? copyFrom, CancellationToken token)
    {
        ValidateName(name);
        Guard.Demand("sandbox_create", name);
        var copied = copyFrom is null ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) :
            ReadRaw(RequireBox(copyFrom, "config_read"));
        copied = copied.Where(x => !x.Key.EndsWith("RootPath", StringComparison.OrdinalIgnoreCase) && !IsSecret(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in copied)
        {
            Guard.DemandSetting(name, pair.Key);
            FeatureRequirements.CheckSetting(Api.Features, pair.Key, pair.Value);
        }
        await MutationGate.WaitAsync(token);
        try
        {
            if (Api.GetSandboxes().Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new SandboxieException("already_exists", "A sandbox with this name already exists.");
            Api.Update(name, "Enabled", "y");
            try
            {
                foreach (var pair in copied.Where(x => !x.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase) &&
                    !x.Key.EndsWith("RootPath", StringComparison.OrdinalIgnoreCase) && !IsSecret(x.Key)))
                {
                    token.ThrowIfCancellationRequested();
                    SetValues(name, pair.Key, pair.Value);
                }
                Api.Reload();
                _ = RequireBox(name, "sandbox_create");
                return new { name, created = true, copiedFrom = copyFrom };
            }
            catch
            {
                Api.Update(name, "*", null);
                throw;
            }
        }
        finally { MutationGate.Release(); }
    }

    public async Task<object> Rename(string name, string newName, CancellationToken token)
    {
        name = RequireBox(name, "sandbox_rename");
        ValidateName(newName);
        Guard.Demand("sandbox_rename", newName);
        Guard.Demand("sandbox_create", newName);
        Guard.Demand("sandbox_delete", name);
        RequireStopped(name);
        if (name.Equals(newName, StringComparison.OrdinalIgnoreCase)) return new { name, renamed = false };
        // Preserve data paths: renaming the configuration must not silently relocate content.
        var root = Paths(name, "sandbox_rename").FileRoot;
        Guard.DemandSetting(newName, "FileRootPath");
        await MutationGate.WaitAsync(token);
        try
        {
            RequireStopped(name);
            if (Api.GetSandboxes().Contains(newName, StringComparer.OrdinalIgnoreCase))
                throw new SandboxieException("already_exists", "The destination sandbox already exists.");
            var data = ReadRaw(name);
            var backup = Backup(name, data);
            foreach (var setting in data.Keys) Guard.DemandSetting(newName, setting);
            try
            {
                foreach (var pair in data.Where(x => !IsSecret(x.Key))) SetValues(newName, pair.Key, pair.Value);
                Api.Update(newName, "FileRootPath", root);
                Api.Update(name, "*", null);
                Api.Reload();
                _ = RequireBox(newName, "sandbox_rename");
                return new { name = newName, previousName = name, fileRoot = root, backup };
            }
            catch (Exception ex)
            {
                throw new SandboxieException("partial_failure", "Rename could not be completed; inspect both sandbox names.",
                    new { source = name, destination = newName, backup, cause = ex is SandboxieException se ? se.Code : "io_error" });
            }
        }
        finally { MutationGate.Release(); }
    }

    public async Task<object> Delete(string name, CancellationToken token)
    {
        name = RequireBox(name, "sandbox_delete");
        await MutationGate.WaitAsync(token);
        try
        {
            RequireStopped(name);
            var root = Paths(name, "sandbox_delete").FileRoot;
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new SandboxieException("not_empty", "Clear sandbox content before deleting its definition.");
            if (Api.Query(name, "NeverDelete", true).Contains("y", StringComparer.OrdinalIgnoreCase))
                throw new SandboxieException("access_denied", "Sandboxie deletion protection is enabled.");
            var backup = Backup(name, ReadRaw(name));
            Api.Update(name, "*", null);
            Api.Reload();
            if (Api.GetSandboxes().Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new SandboxieException("verification_failed", "The sandbox definition still exists.");
            return new { name, deleted = true, backup };
        }
        finally { MutationGate.Release(); }
    }

    internal void SetValues(string section, string setting, string[] values)
    {
        Api.Update(section, setting, values.FirstOrDefault());
        foreach (var value in values.Skip(1)) Api.Update(section, setting, value, 'a');
    }

    internal string Backup(string section, IReadOnlyDictionary<string, string[]> settings)
    {
        var folder = Path.Combine(Configuration.StateDirectory, "configuration-backups");
        PathSafety.NoLinks(folder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            section,
            settings = settings.Where(x => !IsSecret(x.Key))
            .ToDictionary(x => x.Key, x => x.Value)
        }, ServerConfiguration.JsonOptions));
        return Path.GetFileName(path);
    }

    public static string Revision(IReadOnlyDictionary<string, string[]> settings) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            settings.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Value))));

    public static bool IsSecret(string setting) => setting.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        setting.Contains("certificate", StringComparison.OrdinalIgnoreCase) || setting.Contains("token", StringComparison.OrdinalIgnoreCase);

    public static void ValidateName(string name)
    {
        if (name is null || !SandboxName().IsMatch(name) || name.Equals("GlobalSettings", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("UserSettings", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Template_", StringComparison.OrdinalIgnoreCase) ||
            ReservedDevice().IsMatch(name)) throw new ArgumentException("Sandbox names require 1..32 ASCII letters, digits or underscores and cannot be reserved names.");
    }

    public static void ValidateSection(string section)
    {
        if (section is null || section.Length is < 1 or > 64 || !SectionName().IsMatch(section))
            throw new ArgumentException("Invalid configuration section.");
    }

    private void ValidateChanges(string section, SettingChange[] changes)
    {
        ValidateSection(section);
        if (changes is null || changes.Length is < 1 or > 128 || changes.Any(x => x is null) ||
            changes.Select(x => x.Setting).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Length)
            throw new ArgumentException("Supply 1..128 changes with distinct setting names.");
        if (!section.Equals("GlobalSettings", StringComparison.OrdinalIgnoreCase) &&
            !section.StartsWith("Template_", StringComparison.OrdinalIgnoreCase) &&
            !section.StartsWith("UserSettings", StringComparison.OrdinalIgnoreCase))
        {
            ValidateName(section);
            if (!Api.GetSandboxes().Contains(section, StringComparer.OrdinalIgnoreCase)) Guard.Demand("sandbox_create", section);
        }
        foreach (var change in changes)
        {
            if (!SettingName().IsMatch(change.Setting) || change.Setting.Length > 64 || IsSecret(change.Setting) ||
                change.Values is null || change.Values.Length > 512 ||
                change.Values.Any(x => x is null || x.Length > 4000 || x.IndexOfAny(['\r', '\n', '\0']) >= 0))
                throw new ArgumentException("Invalid setting name or value; secret settings cannot be edited through MCP.");
            Guard.DemandSetting(section, change.Setting);
            FeatureRequirements.CheckSetting(Api.Features, change.Setting, change.Values);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,32}$")] private static partial Regex SandboxName();
    [GeneratedRegex("^[A-Za-z0-9_]+$")] private static partial Regex SectionName();
    [GeneratedRegex("^[A-Za-z0-9_.]+$")] private static partial Regex SettingName();
    [GeneratedRegex("^(CON|PRN|AUX|NUL|CLOCK\\$|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDevice();
}

public static class FeatureRequirements
{
    public static void CheckSetting(uint flags, string setting, string[] values)
    {
        if (!values.Any(x => !x.Equals("n", StringComparison.OrdinalIgnoreCase) && x.Length > 0)) return;
        var required = setting.ToLowerInvariant() switch
        {
            "useprivacymode" => 0x04u,
            "nosecurityisolation" => 0x08u,
            "usesecuritymode" or "syscalllockdown" => 0x40u,
            "confidentialbox" or "usefileimage" or "enableefs" => 0x80u,
            "networkuseproxy" or "networkdnsfilter" => 0x100u,
            _ => 0u
        };
        if (required != 0 && ((flags & required) == 0 || (flags & 0x80000000u) == 0))
            throw new SandboxieException("feature_unavailable", "This setting requires an active Sandboxie feature entitlement.", new { setting });
    }
}
