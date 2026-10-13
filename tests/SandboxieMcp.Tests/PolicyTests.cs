using SandboxieMcp.Core;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Security;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class PolicyTests
{
    [Theory]
    [InlineData("sandbox_create")]
    [InlineData("config_write")]
    [InlineData("process_start")]
    [InlineData("sandbox_clean")]
    [InlineData("image_write")]
    [InlineData("global_config")]
    public void ReadOnlyRejectsWrites(string operation) => Assert.False(new PolicyGuard(new()).Can(operation));

    [Fact]
    public void DenyWinsOverFullProfileAndSandboxAllow()
    {
        var guard = new PolicyGuard(new()
        {
            Profile = AccessProfile.Full,
            DeniedOperations = ["sandbox_clean"],
            AllowedSandboxes = ["Work_*"],
            DeniedSandboxes = ["Work_Private"]
        });
        Assert.False(guard.Can("sandbox_clean"));
        Assert.True(guard.CanSeeSandbox("work_build"));
        Assert.False(guard.CanSeeSandbox("Work_Private"));
        Assert.False(guard.CanSeeSandbox("Personal"));
    }

    [Fact]
    public void FullProfileStillHonorsIsolationDenial()
    {
        var guard = new PolicyGuard(new() { Profile = AccessProfile.Full, DeniedOperations = ["isolation_change"] });
        Assert.Throws<SandboxieException>(() => guard.DemandSetting("Box", "OpenFilePath"));
        guard.DemandSetting("Box", "BoxAlias");
    }

    [Theory]
    [InlineData("FileRootPath")]
    [InlineData("Template")]
    [InlineData("AutoExec")]
    [InlineData("StartCommand")]
    [InlineData("Password")]
    public void CustomCannotTurnSettingAllowListIntoUnboundedWrites(string setting)
    {
        var guard = new PolicyGuard(new()
        {
            Profile = AccessProfile.Custom,
            AllowedOperations = ["config_write", "isolation_change"],
            AllowedSettings = [setting]
        });
        Assert.Throws<SandboxieException>(() => guard.DemandSetting("Box", setting));
    }

    [Fact]
    public void GlobalSectionsRequireSeparatePermission()
    {
        var guard = new PolicyGuard(new() { Profile = AccessProfile.Custom, AllowedOperations = ["config_read"] });
        Assert.Throws<SandboxieException>(() => guard.DemandSection("config_read", "GlobalSettings"));
        guard.DemandSection("config_read", "WorkBox");
    }

    [Fact]
    public void OperatorRequiresExplicitExecutableAndExternalPaths()
    {
        var guard = new PolicyGuard(new()
        {
            Profile = AccessProfile.Operator,
            AllowedExecutables = [@"C:\Tools\app.exe"],
            AllowedExternalRoots = [@"C:\McpExchange"]
        });
        guard.DemandExecutable(@"C:\Tools\app.exe");
        Assert.Throws<SandboxieException>(() => guard.DemandExecutable(@"C:\Windows\System32\cmd.exe"));
        Assert.Equal(@"C:\McpExchange\new.txt", guard.DemandExternalPath(@"C:\McpExchange\new.txt"));
        Assert.Throws<SandboxieException>(() => guard.DemandExternalPath(@"C:\McpExchangeOther\new.txt"));
    }

    [Theory]
    [InlineData("Work?", false)]
    [InlineData("*Work", false)]
    [InlineData("Work*Other", false)]
    [InlineData("Work_*", true)]
    public void PatternsHaveUnambiguousGrammar(string pattern, bool valid)
    {
        var policy = new AccessPolicy { AllowedSandboxes = [pattern] };
        if (valid) policy.Validate(); else Assert.Throws<ArgumentException>(policy.Validate);
    }
}
