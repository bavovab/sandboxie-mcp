using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandboxieMcp.Core.Configuration;

public sealed record ServerConfiguration
{
    public string? SandboxiePath { get; init; }
    public string Language { get; init; } = "en";
    public string StateDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandboxieMcp");
    public int OperationTimeoutSeconds { get; init; } = 120;
    public int MaxFileBytes { get; init; } = 1_048_576;
    public long MaxArchiveBytes { get; init; } = 2_147_483_648;
    public AccessPolicy Policy { get; init; } = new();
    public string[] SecretEnvironmentVariables { get; init; } = [];
    public string? ConfigurationPasswordEnvironmentVariable { get; init; }
    [JsonIgnore] public string[] ProtectedPaths { get; init; } = [];

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<AccessProfile>(JsonNamingPolicy.KebabCaseLower) }
    };

    public static ServerConfiguration Load(string? path)
    {
        var config = path is null ? new ServerConfiguration() :
            JsonSerializer.Deserialize<ServerConfiguration>(File.ReadAllText(Path.GetFullPath(path)), JsonOptions)
            ?? throw new ArgumentException("Configuration must be a JSON object.");
        config.Validate();
        return config with
        {
            StateDirectory = Path.GetFullPath(config.StateDirectory),
            ProtectedPaths = (path is null ? Array.Empty<string>() : [Path.GetFullPath(path)])
                .Concat(new[] { AppContext.BaseDirectory, Environment.ProcessPath }.OfType<string>()).ToArray()
        };
    }

    public void Validate()
    {
        if (Language is not ("en" or "ru")) throw new ArgumentException("language must be en or ru.");
        if (!Path.IsPathFullyQualified(StateDirectory)) throw new ArgumentException("stateDirectory must be absolute.");
        if (SandboxiePath is not null && !Path.IsPathFullyQualified(SandboxiePath))
            throw new ArgumentException("sandboxiePath must be absolute.");
        if (OperationTimeoutSeconds is < 1 or > 3600) throw new ArgumentException("operationTimeoutSeconds must be 1..3600.");
        if (MaxFileBytes is < 1 or > 16_777_216) throw new ArgumentException("maxFileBytes must be 1..16777216.");
        if (MaxArchiveBytes is < 1 or > 1_099_511_627_776) throw new ArgumentException("maxArchiveBytes must be 1..1099511627776.");
        ArgumentNullException.ThrowIfNull(Policy);
        if (SecretEnvironmentVariables is null || SecretEnvironmentVariables.Any(x => string.IsNullOrWhiteSpace(x) || x.Contains('=')))
            throw new ArgumentException("secretEnvironmentVariables must contain valid environment variable names.");
        if (ConfigurationPasswordEnvironmentVariable is not null &&
            !SecretEnvironmentVariables.Contains(ConfigurationPasswordEnvironmentVariable, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("configurationPasswordEnvironmentVariable must be in secretEnvironmentVariables.");
        Policy.Validate();
    }
}

public enum AccessProfile { Full, Operator, ReadOnly, Custom }

public sealed record AccessPolicy
{
    public AccessProfile Profile { get; init; } = AccessProfile.ReadOnly;
    public string[] AllowedSandboxes { get; init; } = ["*"];
    public string[] DeniedSandboxes { get; init; } = [];
    public string[] AllowedOperations { get; init; } = [];
    public string[] DeniedOperations { get; init; } = [];
    public string[] AllowedSettings { get; init; } = [];
    public string[] AllowedExecutables { get; init; } = [];
    public string[] AllowedExternalRoots { get; init; } = [];

    public void Validate()
    {
        if (!Enum.IsDefined(Profile)) throw new ArgumentException("Unknown policy profile.");
        var lists = new[] { AllowedSandboxes, DeniedSandboxes, AllowedOperations, DeniedOperations,
            AllowedSettings, AllowedExecutables, AllowedExternalRoots };
        if (lists.Any(x => x is null || x.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Policy lists must contain nonempty strings.");
        if (AllowedOperations.Concat(DeniedOperations).Any(x => !Security.PolicyGuard.IsKnownOperation(x)))
            throw new ArgumentException("Unknown operation in the policy. Use the documented permission names.");
        foreach (var pattern in AllowedSandboxes.Concat(DeniedSandboxes))
            if (pattern.Contains('?') || pattern.Count(c => c == '*') > 1 ||
                (pattern.Contains('*') && !pattern.EndsWith('*')))
                throw new ArgumentException("Sandbox patterns support an exact name or a trailing * only.");
        if (AllowedExternalRoots.Concat(AllowedExecutables).Any(x => !Path.IsPathFullyQualified(x)))
            throw new ArgumentException("Executable and external-root allow lists require absolute paths.");
        if (Profile == AccessProfile.Custom && AllowedOperations.Length == 0)
            throw new ArgumentException("custom requires an explicit allowedOperations list.");
    }
}
