using SandboxieMcp.Core;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Security;
using SandboxieMcp.Core.Services;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class PermissionRegressionTests
{
    [Theory]
    [InlineData("event-log")]
    [InlineData("native")]
    public void MessageSourcesRequireSharedVisibility(string source)
    {
        using var env = new TestEnvironment(new() { Profile = AccessProfile.ReadOnly });
        var error = Assert.Throws<SandboxieException>(() => new DiagnosticsManager(env.Boxes).Messages(0, 10, source));
        Assert.Equal("access_denied", error.Code);
    }

    [Fact]
    public void SharedVisibilityDoesNotGrantMessageReading()
    {
        using var env = new TestEnvironment(new() { Profile = AccessProfile.Custom, AllowedOperations = ["global_config"] });
        var error = Assert.Throws<SandboxieException>(() => new DiagnosticsManager(env.Boxes).Messages(0, 10, "event-log"));
        Assert.Equal("access_denied", error.Code);
    }

    [Theory]
    [InlineData(-1L, 10, "event-log")]
    [InlineData(0L, 0, "event-log")]
    [InlineData(0L, 201, "event-log")]
    [InlineData(0L, 10, "other")]
    [InlineData(4294967296L, 10, "native")]
    public void InvalidMessageQueriesAreRejectedBeforeAccess(long after, int limit, string source)
    {
        using var env = new TestEnvironment();
        Assert.Throws<ArgumentException>(() => new DiagnosticsManager(env.Boxes).Messages(after, limit, source));
    }

    [Fact]
    public void MisspelledDenyCannotSilentlyGrantFullAccess() => Assert.Throws<ArgumentException>(() =>
        new AccessPolicy { Profile = AccessProfile.Full, DeniedOperations = ["proces_start"] }.Validate());

    [Theory]
    [InlineData("AutoExec")]
    [InlineData("StartCommand")]
    [InlineData("InjectDll")]
    public void HooksCannotBypassExplicitProcessDenial(string setting) => Assert.Throws<SandboxieException>(() =>
        new PolicyGuard(new() { Profile = AccessProfile.Full, DeniedOperations = ["process_start"] }).DemandSetting("TestBox", setting));

    [Fact]
    public void ConfigurationCannotBypassSandboxCreationDenial()
    {
        using var env = new TestEnvironment(new() { Profile = AccessProfile.Full, DeniedOperations = ["sandbox_create"] });
        Assert.Throws<SandboxieException>(() => env.Boxes.Preview("NewBox", [new("Enabled", ["y"])]));
        Assert.DoesNotContain("NewBox", env.Api.GetSandboxes());
    }

    [Fact]
    public void MalformedChangeArrayIsRejectedBeforeWrites()
    {
        using var env = new TestEnvironment(); var before = env.Api.Writes;
        Assert.Throws<ArgumentException>(() => env.Boxes.Preview("TestBox", [null!]));
        Assert.Equal(before, env.Api.Writes);
    }
}
