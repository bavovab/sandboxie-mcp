using System.IO.Compression;
using System.Text.Json;
using SandboxieMcp.Core;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Services;
using Xunit;

namespace SandboxieMcp.Tests;

public sealed class StorageTests
{
    [Fact]
    public async Task SnapshotBranchesRestoreAndRejectDeletingParents()
    {
        using var env = new TestEnvironment();
        var root = env.Api.GetPaths("TestBox").FileRoot;
        Directory.CreateDirectory(Path.Combine(root, "drive", "C"));
        File.WriteAllText(Path.Combine(root, "RegHive"), "test hive data");
        File.WriteAllText(Path.Combine(root, "drive", "C", "first.txt"), "first");
        var snapshots = new SnapshotManager(env.Boxes);
        await snapshots.Create("TestBox", "Первый снимок", default);
        Directory.CreateDirectory(Path.Combine(root, "drive", "C"));
        File.WriteAllText(Path.Combine(root, "drive", "C", "second.txt"), "second");
        await snapshots.Create("TestBox", "Second", default);
        Assert.Equal("1", snapshots.List("TestBox").Snapshots.Single(x => x.Id == "2").Parent);
        Assert.Equal("snapshot_in_use", (await Assert.ThrowsAsync<SandboxieException>(() => snapshots.Delete("TestBox", "1", default))).Code);
        await snapshots.Restore("TestBox", "1", default);
        await snapshots.Delete("TestBox", "2", default);
        Assert.False(Directory.Exists(Path.Combine(root, "drive")));
        Assert.Equal("first", File.ReadAllText(Path.Combine(root, "snapshot-1", "drive", "C", "first.txt")));
        Assert.Equal("Первый снимок", snapshots.List("TestBox").Snapshots.Single().Name);
    }

    [Fact]
    public async Task CancelledSnapshotKeepsOriginalContent()
    {
        using var env = new TestEnvironment();
        var root = env.Api.GetPaths("TestBox").FileRoot;
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "RegHive"), "hive");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SnapshotManager(env.Boxes).Create("TestBox", "Cancelled", cancellation.Token));
        Assert.Equal("hive", File.ReadAllText(Path.Combine(root, "RegHive")));
        Assert.False(File.Exists(Path.Combine(root, "Snapshots.ini")));
    }

    [Fact]
    public async Task ArchiveRoundTripRebindsStorageAndPreservesData()
    {
        using var env = new TestEnvironment();
        var root = env.Api.GetPaths("TestBox").FileRoot;
        Directory.CreateDirectory(Path.Combine(root, "drive", "C"));
        File.WriteAllBytes(Path.Combine(root, "drive", "C", "binary.dat"), [0, 1, 2, 255]);
        env.Api.Update("TestBox", "BoxAlias", "Unicode Русский");
        env.Api.Update("TestBox", "FileRootPath", root);
        var archive = new ArchiveManager(env.Boxes);
        var target = Path.Combine(env.Root, "export.zip");
        await archive.Export("TestBox", target, true, default);
        await archive.Import("ImportedBox", target, default);
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, File.ReadAllBytes(Path.Combine(env.Api.GetPaths("ImportedBox").FileRoot, "drive", "C", "binary.dat")));
        Assert.Empty(env.Api.Query("ImportedBox", "FileRootPath"));
        Assert.Equal("Unicode Русский", env.Api.Query("ImportedBox", "BoxAlias").Single());
    }

    [Theory]
    [InlineData("content/../../escape.txt")]
    [InlineData("content/C:/escape.txt")]
    [InlineData("content/file:stream")]
    [InlineData("content/CON.txt")]
    public async Task HostileArchiveCannotCreateSandboxOrExtractData(string entryName)
    {
        using var env = new TestEnvironment();
        var target = Path.Combine(env.Root, "hostile.zip");
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            using (var manifest = zip.CreateEntry("manifest.json").Open())
                JsonSerializer.Serialize(manifest, new ArchiveManifest(1, "Source", "1.18.5", DateTime.UtcNow, new() { ["Enabled"] = ["y"] }, true), ServerConfiguration.JsonOptions);
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); writer.Write("bad");
        }
        var writes = env.Api.Writes;
        await Assert.ThrowsAsync<SandboxieException>(() => new ArchiveManager(env.Boxes).Import("ImportedBox", target, default));
        Assert.Equal(writes, env.Api.Writes);
        Assert.DoesNotContain("ImportedBox", env.Api.GetSandboxes());
    }

    [Fact]
    public async Task ImportCannotOverwriteSnapshotMetadata()
    {
        using var env = new TestEnvironment();
        var fixture = Path.Combine(env.Root, "source.txt"); File.WriteAllText(fixture, "source");
        await Assert.ThrowsAsync<ArgumentException>(() => new FileManager(env.Boxes).Import("TestBox", fixture, "Snapshots.ini", false, default));
    }

    [Fact]
    public void ServerConfigurationAndStateAreProtected()
    {
        using var env = new TestEnvironment();
        Assert.Throws<SandboxieException>(() => env.Boxes.ExternalPath(Path.Combine(env.Root, "state", "audit.jsonl")));
    }
}
