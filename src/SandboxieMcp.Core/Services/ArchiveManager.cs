using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SandboxieMcp.Core.Configuration;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Services;

public sealed record ArchiveManifest(int FormatVersion, string SourceSandbox, string SandboxieVersion,
    DateTime CreatedUtc, Dictionary<string, string[]> Settings, bool IncludesContent);

public sealed class ArchiveManager(SandboxManager boxes)
{
    public async Task<object> Export(string sandbox, string destination, bool includeContent, CancellationToken token)
    {
        sandbox = boxes.RequireBox(sandbox, "archive_export");
        var settings = boxes.ReadConfiguration(sandbox).Settings
            .Where(x => !x.Key.EndsWith("RootPath", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var root = boxes.Paths(sandbox, "file_read").FileRoot;
        destination = boxes.ExternalPath(destination);
        if (PathSafety.IsWithin(root, destination)) throw new ArgumentException("Export destination must be outside the sandbox.");
        if (File.Exists(destination)) throw new SandboxieException("already_exists", "The export destination already exists.");
        await boxes.MutationGate.WaitAsync(token);
        var temp = destination + ".mcp-" + Guid.NewGuid().ToString("N");
        try
        {
            boxes.RequireStopped(sandbox);
            PathSafety.ValidateTree(root, token);
            var files = includeContent && Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray() : [];
            if (files.Length > 100000 || files.Sum(x => new FileInfo(x).Length) > boxes.Configuration.MaxArchiveBytes)
                throw new ArgumentException("Sandbox content exceeds archive limits.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("manifest.json");
                await using (var manifestStream = entry.Open())
                    await JsonSerializer.SerializeAsync(manifestStream,
                        new ArchiveManifest(1, sandbox, boxes.Api.Version, DateTime.UtcNow, settings, includeContent),
                        ServerConfiguration.JsonOptions, token);
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    PathSafety.NoLinks(file);
                    var item = archive.CreateEntry("content/" + Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Fastest);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var content = item.Open();
                    await input.CopyToAsync(content, token);
                }
            }
            if (new FileInfo(temp).Length > boxes.Configuration.MaxArchiveBytes) throw new ArgumentException("The resulting archive exceeds maxArchiveBytes.");
            token.ThrowIfCancellationRequested();
            PathSafety.NoLinks(destination);
            File.Move(temp, destination);
            await using var verify = File.OpenRead(destination);
            return new
            {
                exported = true,
                files = files.Length,
                bytes = verify.Length,
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(verify, token)),
                format = "sandboxie-mcp/1"
            };
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            boxes.MutationGate.Release();
        }
    }

    public async Task<object> Import(string sandbox, string source, CancellationToken token)
    {
        SandboxManager.ValidateName(sandbox);
        boxes.Guard.Demand("archive_import", sandbox);
        boxes.Guard.Demand("sandbox_create", sandbox);
        boxes.Guard.Demand("file_import", sandbox);
        source = boxes.ExternalPath(source);
        if (!File.Exists(source)) throw new SandboxieException("not_found", "The archive does not exist.");
        if (new FileInfo(source).Length > boxes.Configuration.MaxArchiveBytes) throw new ArgumentException("The archive exceeds maxArchiveBytes.");
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new ArgumentException("Missing archive manifest.");
        if (manifestEntry.Length > 1_048_576) throw new ArgumentException("The archive manifest is too large.");
        await using var manifestStream = manifestEntry.Open();
        using var manifestBuffer = new MemoryStream();
        await BoundedCopy.Copy(manifestStream, manifestBuffer, 1_048_576, token);
        var manifest = JsonSerializer.Deserialize<ArchiveManifest>(manifestBuffer.ToArray(), ServerConfiguration.JsonOptions)
            ?? throw new ArgumentException("Invalid archive manifest.");
        if (manifest.FormatVersion != 1 || manifest.Settings is null || manifest.Settings.Count > 128)
            throw new ArgumentException("Unsupported archive format or too many settings.");
        var changes = manifest.Settings.Where(x => !x.Key.EndsWith("RootPath", StringComparison.OrdinalIgnoreCase))
            .Select(x => new SettingChange(x.Key, x.Value)).ToArray();
        foreach (var change in changes)
        {
            if (SandboxManager.IsSecret(change.Setting)) throw new ArgumentException("Secret settings cannot be imported.");
            boxes.Guard.DemandSetting(sandbox, change.Setting);
            FeatureRequirements.CheckSetting(boxes.Api.Features, change.Setting, change.Values);
        }
        if (changes.Length > 0) _ = boxes.Preview(sandbox, changes);
        var entries = archive.Entries.Where(x => x.FullName != "manifest.json").ToArray();
        if (entries.Length > 100000 || entries.Sum(x => x.Length) > boxes.Configuration.MaxArchiveBytes)
            throw new ArgumentException("The archive exceeds extraction limits.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!entry.FullName.StartsWith("content/", StringComparison.Ordinal) || entry.FullName.EndsWith('/') ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new ArgumentException("Unsupported archive entry.");
            var relative = entry.FullName[8..];
            var canonical = PathSafety.Within(Path.Combine(boxes.Configuration.StateDirectory, "archive-validation"), relative);
            if (!paths.Add(canonical)) throw new ArgumentException("Duplicate archive path.");
            if (relative.Equals("Snapshots.ini", StringComparison.OrdinalIgnoreCase))
            {
                await using var metadataStream = entry.Open();
                using var metadata = new MemoryStream();
                await BoundedCopy.Copy(metadataStream, metadata, 1_048_576, token);
                _ = SnapshotManager.ValidateMetadata(IniFile.Parse(System.Text.Encoding.UTF8.GetString(metadata.ToArray())));
            }
        }
        if (!manifest.IncludesContent && entries.Length > 0) throw new ArgumentException("Manifest and content disagree.");
        await boxes.Create(sandbox, null, token);
        var completed = new List<string>();
        try
        {
            if (changes.Length > 0)
                _ = await boxes.Apply(sandbox, changes, boxes.ReadConfiguration(sandbox).Revision, token);
            var root = boxes.Paths(sandbox, "file_import").FileRoot;
            await boxes.MutationGate.WaitAsync(token);
            try
            {
                boxes.RequireStopped(sandbox);
                if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                    throw new SandboxieException("not_empty", "The new sandbox's storage root contains existing data; import refused.");
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = entry.FullName[8..];
                    var target = PathSafety.Within(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var content = entry.Open();
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await BoundedCopy.Copy(content, output, entry.Length, token);
                    if (output.Length != entry.Length) throw new SandboxieException("verification_failed", "Extracted file size mismatch.");
                    completed.Add(relative);
                }
                return new
                {
                    imported = true,
                    sandbox,
                    files = completed.Count,
                    sourceSandbox = manifest.SourceSandbox,
                    sourceVersion = manifest.SandboxieVersion,
                    rootsRebound = true
                };
            }
            finally { boxes.MutationGate.Release(); }
        }
        catch (Exception ex)
        {
            throw new SandboxieException("partial_failure", "The sandbox was created, but archive import did not finish. Inspect it before retrying.",
                new { sandbox, extractedFiles = completed, cause = ex is SandboxieException se ? se.Code : "io_error" });
        }
    }
}
