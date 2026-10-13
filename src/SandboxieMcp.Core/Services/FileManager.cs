using System.Security.Cryptography;
using System.Text;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Services;

public sealed class FileManager(SandboxManager boxes)
{
    public object List(string sandbox, string relative, int offset, int limit, CancellationToken token)
    {
        var root = boxes.Paths(sandbox, "file_read").FileRoot;
        boxes.RequireStopped(sandbox);
        var folder = PathSafety.Within(root, relative);
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentException("offset must be nonnegative; limit must be 1..200.");
        if (!Directory.Exists(folder)) return new { entries = Array.Empty<FileEntry>(), nextOffset = (int?)null };
        var selected = Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.OrdinalIgnoreCase)
            .Skip(offset).Take(limit + 1).ToArray();
        var entries = selected.Take(limit).Select(path =>
        {
            token.ThrowIfCancellationRequested();
            PathSafety.NoLinks(path);
            var directory = Directory.Exists(path);
            return new FileEntry(Path.GetRelativePath(root, path), directory,
                directory ? 0 : new FileInfo(path).Length, File.GetLastWriteTimeUtc(path));
        }).ToArray();
        return new { entries, nextOffset = selected.Length > limit ? (int?)(offset + limit) : null };
    }

    public object Read(string sandbox, string relative, bool base64)
    {
        var root = boxes.Paths(sandbox, "file_read").FileRoot;
        boxes.RequireStopped(sandbox);
        var path = PathSafety.Within(root, relative);
        if (!File.Exists(path)) throw new SandboxieException("not_found", "The file does not exist.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        PathSafety.NoLinks(path);
        if (stream.Length > boxes.Configuration.MaxFileBytes)
            throw new ArgumentException("The file exceeds maxFileBytes. Recover it to an allowed external path instead.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        var content = base64 ? Convert.ToBase64String(bytes) : new UTF8Encoding(false, true).GetString(bytes);
        return new
        {
            path = relative,
            encoding = base64 ? "base64" : "utf-8",
            bytes = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            content
        };
    }

    public async Task<object> Import(string sandbox, string source, string destination, bool overwrite, CancellationToken token)
    {
        var root = boxes.Paths(sandbox, "file_import").FileRoot;
        source = boxes.ExternalPath(source);
        var target = PathSafety.Within(root, destination);
        if (!IsContentRelative(Path.GetRelativePath(root, target))) throw new ArgumentException("Import destinations must be inside drive, user or share.");
        return await Copy(sandbox, source, target, overwrite, token);
    }

    public Task<object> Recover(string sandbox, string source, string destination, bool overwrite, CancellationToken token)
    {
        var root = boxes.Paths(sandbox, "file_recover").FileRoot;
        var path = PathSafety.Within(root, source);
        destination = boxes.ExternalPath(destination);
        return Copy(sandbox, path, destination, overwrite, token);
    }

    private async Task<object> Copy(string sandbox, string source, string destination, bool overwrite, CancellationToken token)
    {
        boxes.RequireStopped(sandbox);
        if (!File.Exists(source)) throw new SandboxieException("not_found", "The source file does not exist.");
        if (new FileInfo(source).Length > boxes.Configuration.MaxArchiveBytes) throw new ArgumentException("The source exceeds maxArchiveBytes.");
        await boxes.MutationGate.WaitAsync(token);
        var temporary = destination + ".mcp-" + Guid.NewGuid().ToString("N");
        try
        {
            boxes.RequireStopped(sandbox);
            PathSafety.NoLinks(source);
            PathSafety.NoLinks(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, token);
            token.ThrowIfCancellationRequested();
            PathSafety.NoLinks(destination);
            File.Move(temporary, destination, overwrite);
            using var verified = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(verified, token));
            return new { copied = true, bytes = verified.Length, sha256 = hash };
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            boxes.MutationGate.Release();
        }
    }

    public object Size(string sandbox, CancellationToken token)
    {
        var root = boxes.Paths(sandbox, "file_read").FileRoot;
        boxes.RequireStopped(sandbox);
        PathSafety.ValidateTree(root, token);
        long bytes = 0;
        var count = 0;
        if (Directory.Exists(root))
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                bytes += new FileInfo(path).Length;
                count++;
            }
        return new { bytes, files = count };
    }

    public async Task<object> Clean(string sandbox, CancellationToken token)
    {
        var root = boxes.Paths(sandbox, "sandbox_clean").FileRoot;
        await boxes.MutationGate.WaitAsync(token);
        var removed = new List<string>();
        try
        {
            boxes.RequireStopped(sandbox);
            if (boxes.Api.Query(sandbox, "NeverDelete", true).Contains("y", StringComparer.OrdinalIgnoreCase))
                throw new SandboxieException("access_denied", "Sandboxie deletion protection is enabled.");
            PathSafety.ValidateTree(root, token);
            if (!Directory.Exists(root)) return new { cleaned = true, removed };
            foreach (var child in Directory.EnumerateFileSystemEntries(root))
            {
                token.ThrowIfCancellationRequested();
                PathSafety.NoLinks(child);
                if (Directory.Exists(child)) Directory.Delete(child, true); else File.Delete(child);
                removed.Add(Path.GetFileName(child));
            }
            if (Directory.EnumerateFileSystemEntries(root).Any())
                throw new SandboxieException("verification_failed", "Sandbox content remains after cleanup.");
            return new { cleaned = true, removed, snapshotsRemoved = true };
        }
        catch (Exception ex) when (removed.Count > 0)
        {
            throw new SandboxieException("partial_failure", "Cleanup was only partially completed.",
                new { removed, cause = ex is SandboxieException se ? se.Code : "io_error" });
        }
        finally { boxes.MutationGate.Release(); }
    }

    public static bool IsContentRelative(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        return new[] { "drive/", "user/", "share/" }.Any(x => normalized.StartsWith(x, StringComparison.OrdinalIgnoreCase));
    }
}
