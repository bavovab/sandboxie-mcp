namespace SandboxieMcp.Core;

public static class BuildInfo
{
    public static string Version { get; } = typeof(BuildInfo).Assembly.GetName().Version!.ToString(3);
}
