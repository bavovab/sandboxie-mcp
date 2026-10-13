using SandboxieMcp.Core;
using SandboxieMcp.Core.Security;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class PathTests
{
    [Theory]
    [InlineData("..\\outside")]
    [InlineData("folder/../../outside")]
    [InlineData(@"C:\outside")]
    [InlineData(@"\\server\share\file")]
    [InlineData("file:stream")]
    [InlineData("NUL.txt")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("folder\\a?b")]
    public void RejectsEscapesAndWindowsAliases(string path) =>
        Assert.Throws<SandboxieException>(() => PathSafety.Within(@"C:\McpPathTest", path));

    [Fact]
    public void SiblingPrefixIsNotInsideRoot()
    {
        Assert.False(PathSafety.IsWithin(@"C:\Test", @"C:\TestOther\file"));
        Assert.True(PathSafety.IsWithin(@"C:\Test", @"c:\test\file"));
    }

    [Fact]
    public void ReparseDirectoryCannotBeFollowed()
    {
        using var env = new TestEnvironment();
        var real = Path.Combine(env.Root, "real"); Directory.CreateDirectory(real);
        var link = Path.Combine(env.Root, "link");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Arguments = $"/d /c mklink /J \"{link}\" \"{real}\"";
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        Assert.Throws<SandboxieException>(() => PathSafety.Within(env.Root, "link/file"));
        Assert.Throws<SandboxieException>(() => PathSafety.ValidateTree(env.Root, default));
        Directory.Delete(link);
    }
}
