using SandboxieMcp.Core.Configuration;

namespace SandboxieMcp.Core.Security;

public sealed class PolicyGuard(AccessPolicy policy)
{
    private static readonly HashSet<string> KnownOperations = ["diagnostics", "sandbox_read", "config_read", "process_read",
        "file_read", "snapshot_read", "message_read", "image_read", "audit_read", "sandbox_create", "sandbox_rename", "sandbox_delete",
        "config_write", "process_start", "process_stop", "process_suspend", "file_import", "file_recover", "sandbox_clean",
        "snapshot_create", "snapshot_restore", "snapshot_delete", "snapshot_write", "archive_export", "archive_import",
        "global_config", "maintenance", "isolation_change", "image_write"];
    public static bool IsKnownOperation(string operation) => KnownOperations.Contains(operation);
    private static readonly HashSet<string> ReadOperations = ["diagnostics", "sandbox_read", "config_read",
        "process_read", "file_read", "snapshot_read", "message_read", "image_read", "audit_read"];
    private static readonly HashSet<string> OperatorOperations = [.. ReadOperations, "sandbox_create",
        "sandbox_delete", "config_write", "process_start", "process_stop", "process_suspend", "file_import",
        "file_recover", "sandbox_clean", "snapshot_create", "snapshot_restore", "snapshot_delete", "snapshot_write",
        "archive_export", "archive_import"];
    private static readonly HashSet<string> OperatorSettings = new(StringComparer.OrdinalIgnoreCase)
        { "Enabled", "BoxAlias", "BorderColor", "BoxNameTitle", "BlockNetworkFiles", "BlockNetParam" };

    public bool Can(string operation) =>
        !policy.DeniedOperations.Contains(operation, StringComparer.Ordinal) &&
        (policy.Profile switch
        {
            AccessProfile.Full => true,
            AccessProfile.Operator => OperatorOperations.Contains(operation),
            AccessProfile.ReadOnly => ReadOperations.Contains(operation),
            AccessProfile.Custom => policy.AllowedOperations.Contains(operation, StringComparer.Ordinal),
            _ => false
        });

    public void Demand(string operation, string? sandbox = null)
    {
        if (!Can(operation) || (sandbox is not null && !CanSeeSandbox(sandbox)))
            throw new SandboxieException("access_denied", "The operation is denied by the access policy.");
    }

    public bool CanSeeSandbox(string name) => policy.AllowedSandboxes.Any(x => Matches(x, name)) &&
        !policy.DeniedSandboxes.Any(x => Matches(x, name));

    public void DemandSection(string operation, string section)
    {
        if (section.Equals("GlobalSettings", StringComparison.OrdinalIgnoreCase) ||
            section.StartsWith("Template_", StringComparison.OrdinalIgnoreCase) ||
            section.StartsWith("UserSettings", StringComparison.OrdinalIgnoreCase))
        {
            Demand("global_config");
            Demand(operation);
            return;
        }
        Demand(operation, section);
    }

    public void DemandSetting(string section, string setting)
    {
        DemandSection("config_write", section);
        if (setting.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("AutoExec", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Start", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Inject", StringComparison.OrdinalIgnoreCase))
        {
            Demand("process_start", section);
            Demand("isolation_change", section);
        }
        if (setting.StartsWith("Open", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Force", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Breakout", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("NoSecurity", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("UseSecurityMode", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("UsePrivacyMode", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("DropAdminRights", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("NetworkAccess", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("AllowNetworkAccess", StringComparison.OrdinalIgnoreCase))
            Demand("isolation_change", section);
        if (policy.Profile == AccessProfile.Full) return;
        // Implicit writes, arbitrary path roots, hooks, templates and command launch settings
        // require full access; a value allow list alone cannot confine their side effects.
        if (setting is "*" || setting.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Template", StringComparison.OrdinalIgnoreCase) ||
            setting.EndsWith("RootPath", StringComparison.OrdinalIgnoreCase) ||
            setting.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Auto", StringComparison.OrdinalIgnoreCase) ||
            setting.StartsWith("Start", StringComparison.OrdinalIgnoreCase))
            throw new SandboxieException("access_denied", "This setting requires the full profile.");
        if (!(policy.AllowedSettings.Contains(setting, StringComparer.OrdinalIgnoreCase) ||
            (policy.Profile == AccessProfile.Operator && OperatorSettings.Contains(setting))))
            throw new SandboxieException("access_denied", "The setting is not explicitly allowed.");
    }

    public void DemandExecutable(string executable)
    {
        if (policy.Profile != AccessProfile.Full &&
            !policy.AllowedExecutables.Any(x => Path.GetFullPath(x).Equals(executable, StringComparison.OrdinalIgnoreCase)))
            throw new SandboxieException("access_denied", "The executable is not explicitly allowed.");
    }

    public string DemandExternalPath(string path)
    {
        var resolved = PathSafety.Absolute(path);
        PathSafety.NoLinks(resolved);
        if (policy.Profile != AccessProfile.Full &&
            !policy.AllowedExternalRoots.Any(x => PathSafety.IsWithin(Path.GetFullPath(x), resolved)))
            throw new SandboxieException("access_denied", "The external path is not explicitly allowed.");
        return resolved;
    }

    public static bool Matches(string pattern, string name) => pattern.EndsWith('*')
        ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
        : name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
}
