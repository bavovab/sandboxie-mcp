using System.Runtime.InteropServices;
using SandboxieMcp.Core.Services;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class ProcessArgumentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("simple")]
    [InlineData("has spaces")]
    [InlineData("Русский Unicode")]
    [InlineData("quote\"inside")]
    [InlineData("space and slash\\")]
    [InlineData("\\\\\"multiple")]
    [InlineData("a\tb")]
    public void ArgumentsRoundTripThroughWindowsParser(string argument)
    {
        var pointer = CommandLineToArgvW("program.exe " + ProcessManager.Quote(argument), out var count);
        Assert.NotEqual(0, pointer);
        try
        {
            Assert.Equal(2, count);
            Assert.Equal(argument, Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, IntPtr.Size)));
        }
        finally { _ = LocalFree(pointer); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
