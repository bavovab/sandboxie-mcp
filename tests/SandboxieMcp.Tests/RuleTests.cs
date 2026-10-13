using SandboxieMcp.Core.Services;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class RuleTests
{
    [Fact]
    public void FormatsNativeNetworkGrammar() => Assert.Equal("app.exe,Allow;Port=80,443;Address=192.0.2.10-192.0.2.20;Protocol=TCP",
        RuleManager.Format(new("allow", "app.exe", "80,443", "192.0.2.10-192.0.2.20", "TCP")));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("http")]
    [InlineData("443-80")]
    [InlineData("80;Protocol=TCP")]
    public void RejectsInvalidPortsAndRuleInjection(string ports) => Assert.Throws<ArgumentException>(() =>
        RuleManager.Format(new("block", Ports: ports)));

    [Theory]
    [InlineData("192.0.2.0/24")]
    [InlineData("example.com")]
    [InlineData("192.0.2.20-192.0.2.10")]
    [InlineData("192.0.2.1-::1")]
    public void RejectsUnsupportedAddresses(string addresses) => Assert.Throws<ArgumentException>(() =>
        RuleManager.Format(new("allow", Addresses: addresses)));
}
