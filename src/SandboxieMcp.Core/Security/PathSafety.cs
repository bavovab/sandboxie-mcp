namespace SandboxieMcp.Core.Security;

public static class PathSafety
{
    public static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains('\0') ||
            path.Skip(2).Contains(':'))
            throw new SandboxieException("unsafe_path", "Use an absolute local path without alternate data streams.");
        ValidateSegments(path[(Path.GetPathRoot(path)?.Length ?? 0)..]);
        return Path.GetFullPath(path);
    }

    public static bool IsWithin(string root, string path) =>
        path.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public static string Within(string root, string relative)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relative);
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\0'))
            throw new SandboxieException("unsafe_path", "A relative path without alternate data streams is required.");
        ValidateSegments(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsWithin(root, path)) throw new SandboxieException("unsafe_path", "The path escapes its root.");
        NoLinks(path);
        return path;
    }

    private static void ValidateSegments(string path)
    {
        foreach (var segment in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || segment.Any(char.IsControl))
                throw new SandboxieException("unsafe_path", "Ambiguous path segments are not allowed.");
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])))
                throw new SandboxieException("unsafe_path", "Windows device names are not allowed.");
        }
    }

    public static void NoLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SandboxieException("unsafe_path", "Reparse points are not allowed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static void ValidateTree(string root, CancellationToken cancellationToken)
    {
        NoLinks(root);
        if (!Directory.Exists(root)) return;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in Directory.EnumerateFileSystemEntries(folder))
            {
                NoLinks(child);
                if (Directory.Exists(child)) pending.Push(child);
            }
        }
    }
}
