using SandboxieMcp.Core;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Services;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public async Task StaleRevisionNeverWrites()
    {
        using var env = new TestEnvironment();
        var revision = env.Boxes.ReadConfiguration("TestBox").Revision;
        env.Api.Update("TestBox", "BoxAlias", "Other writer");
        var writes = env.Api.Writes;
        var error = await Assert.ThrowsAsync<SandboxieException>(() => env.Boxes.Apply("TestBox", [new("BoxAlias", ["Requested"])], revision, default));
        Assert.Equal("configuration_conflict", error.Code);
        Assert.Equal(writes, env.Api.Writes);
        Assert.Equal("Other writer", env.Api.Query("TestBox", "BoxAlias").Single());
    }

    [Fact]
    public async Task PartialWriteIsExplicitAndHasBackup()
    {
        using var env = new TestEnvironment();
        env.Api.FailOnSetting = "BorderColor";
        var error = await Assert.ThrowsAsync<SandboxieException>(() => env.Boxes.Apply("TestBox",
            [new("BoxAlias", ["Applied"]), new("BorderColor", ["#123456"])], env.Boxes.ReadConfiguration("TestBox").Revision, default));
        Assert.Equal("partial_failure", error.Code);
        Assert.Equal("Applied", env.Api.Query("TestBox", "BoxAlias").Single());
        Assert.Single(Directory.GetFiles(Path.Combine(env.Root, "state", "configuration-backups")));
    }

    [Fact]
    public void SecretSettingsNeverAppearInReadResult()
    {
        using var env = new TestEnvironment();
        env.Api.Update("TestBox", "ConfigPassword", "sensitive");
        Assert.DoesNotContain("ConfigPassword", env.Boxes.ReadConfiguration("TestBox").Settings.Keys);
    }

    [Fact]
    public async Task CopyDoesNotShareSourceStorageRoots()
    {
        using var env = new TestEnvironment();
        env.Api.Update("TestBox", "FileRootPath", @"C:\SourceRoot");
        env.Api.Update("TestBox", "BoxAlias", "Copy me");
        await env.Boxes.Create("OtherBox", "TestBox", default);
        Assert.Empty(env.Api.Query("OtherBox", "FileRootPath"));
        Assert.Equal("Copy me", env.Api.Query("OtherBox", "BoxAlias").Single());
    }

    [Fact]
    public void OtherSessionProcessesPreventFilesystemMutation()
    {
        using var env = new TestEnvironment(); env.Api.BusyInOtherSession = true;
        Assert.Throws<SandboxieException>(() => env.Boxes.RequireStopped("TestBox"));
    }

    [Theory]
    [InlineData("GlobalSettings")]
    [InlineData("Template_Local")]
    [InlineData("CON")]
    [InlineData("../Other")]
    [InlineData("Has space")]
    [InlineData("")]
    public void SandboxNamesCannotAddressOtherSectionKinds(string name) => Assert.Throws<ArgumentException>(() => SandboxManager.ValidateName(name));

    [Theory]
    [InlineData("UsePrivacyMode")]
    [InlineData("UseFileImage")]
    [InlineData("NetworkUseProxy")]
    [InlineData("UseSecurityMode")]
    public void PremiumOptionsNeedEntitlementButCanBeDisabled(string setting)
    {
        Assert.Throws<SandboxieException>(() => FeatureRequirements.CheckSetting(0, setting, ["y"]));
        FeatureRequirements.CheckSetting(0, setting, ["n"]);
    }

    [Fact]
    public void StrictConfigurationRejectsMisspelledFields()
    {
        using var env = new TestEnvironment();
        var file = Path.Combine(env.Root, "configuration.json");
        File.WriteAllText(file, "{\"polciy\":{}}");
        Assert.Throws<System.Text.Json.JsonException>(() => ServerConfiguration.Load(file));
    }
}
