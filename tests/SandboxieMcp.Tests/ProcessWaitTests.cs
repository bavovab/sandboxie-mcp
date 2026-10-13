using System.Text.Json;
using SandboxieMcp.Core;
using SandboxieMcp.Core.Services;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class ProcessWaitTests
{
    [Fact]
    public async Task WaitPreservesExitCodeAfterProcessDisappears()
    {
        using var env = new TestEnvironment();
        env.Api.Started = new(123, "TestBox", 100, true, 47);
        var result = JsonSerializer.SerializeToElement(await new ProcessManager(env.Boxes).Wait("TestBox", 123, 1, default));
        Assert.True(result.GetProperty("exited").GetBoolean());
        Assert.Equal(47, result.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task RetainedProcessMustStillBelongToRequestedSandbox()
    {
        using var env = new TestEnvironment();
        env.Api.Started = new(123, "OtherBox", 100, true, 0);
        var error = await Assert.ThrowsAsync<SandboxieException>(() => new ProcessManager(env.Boxes).Wait("TestBox", 123, 1, default));
        Assert.Equal("not_found", error.Code);
    }

    [Fact]
    public async Task CancellationDoesNotStopTheLaunchedProcess()
    {
        using var env = new TestEnvironment();
        env.Api.Started = new(123, "TestBox", 100, false, null);
        using var cancellation = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessManager(env.Boxes).Wait("TestBox", 123, 1, cancellation.Token));
        Assert.False(env.Api.Started.Exited);
    }

    [Theory]
    [InlineData("[Snapshot_1]\nParent=1\n")]
    [InlineData("[Current]\nSnapshot=../escape\n")]
    [InlineData("[Snapshot_1]\nParent=2\n")]
    public void InvalidSnapshotGraphCannotReachTheDriver(string metadata) =>
        Assert.Throws<ArgumentException>(() => SnapshotManager.ValidateMetadata(IniFile.Parse(metadata)));
}
