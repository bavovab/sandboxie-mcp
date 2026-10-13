using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Services;

public sealed partial class SnapshotManager(SandboxManager boxes)
{
    private static readonly string[] ContentFolders = ["drive", "user", "share"];
    private static readonly string[] DataFiles = ["RegHive", "RegPaths.dat", "FilePaths.dat"];

    public SnapshotState List(string sandbox)
    {
        var root = boxes.Paths(sandbox, "snapshot_read").FileRoot;
        return State(IniFile.Read(PathSafety.Within(root, "Snapshots.ini")));
    }

    public static SnapshotState ValidateMetadata(IniFile ini)
    {
        var state = new SnapshotState(ini.Get("Current", "Snapshot"), ini.Get("Current", "Default"),
            ini.Sections.Where(x => x.Key.StartsWith("Snapshot_", StringComparison.OrdinalIgnoreCase))
            .Select(x => new SnapshotInfo(x.Key[9..], x.Value.GetValueOrDefault("Name", x.Key[9..]),
                x.Value.GetValueOrDefault("Parent"), x.Value.GetValueOrDefault("Description"),
                long.TryParse(x.Value.GetValueOrDefault("SnapshotDate"), out var date) ? date : 0)).ToArray());
        var ids = state.Snapshots.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var id in new[] { state.Current, state.Default }.Concat(state.Snapshots.Select(x => x.Parent)).Where(x => !string.IsNullOrEmpty(x)))
        {
            ValidateId(id!);
            if (!ids.ContainsKey(id!)) throw new ArgumentException("Snapshot metadata references a missing layer.");
        }
        foreach (var snapshot in state.Snapshots)
        {
            ValidateId(snapshot.Id);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = snapshot.Id;
            while (!string.IsNullOrEmpty(current))
            {
                if (!seen.Add(current)) throw new ArgumentException("Snapshot metadata contains a parent cycle.");
                current = ids[current].Parent;
            }
        }
        return state;
    }

    private static SnapshotState State(IniFile ini) => ValidateMetadata(ini);

    public async Task<object> Create(string sandbox, string name, CancellationToken token)
    {
        ValidateText(name);
        var root = boxes.Paths(sandbox, "snapshot_create").FileRoot;
        await boxes.MutationGate.WaitAsync(token);
        var moved = new List<string>();
        string? destination = null;
        try
        {
            boxes.RequireStopped(sandbox);
            PathSafety.ValidateTree(root, token);
            if (!File.Exists(Path.Combine(root, "RegHive")))
                throw new SandboxieException("not_initialized", "Initialize the sandbox by running a program before taking a snapshot.");
            var metadata = PathSafety.Within(root, "Snapshots.ini");
            var revision = Hash(metadata);
            var ini = IniFile.Read(metadata);
            var state = State(ini);
            var id = Enumerable.Range(1, 1_000_000).Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .First(x => !state.Snapshots.Any(s => s.Id == x) && !Directory.Exists(Path.Combine(root, "snapshot-" + x)));
            destination = PathSafety.Within(root, "snapshot-" + id);
            Directory.CreateDirectory(destination);
            foreach (var file in DataFiles)
                if (File.Exists(Path.Combine(root, file))) File.Copy(Path.Combine(root, file), Path.Combine(destination, file));
            foreach (var folder in ContentFolders)
            {
                token.ThrowIfCancellationRequested();
                var source = Path.Combine(root, folder);
                if (!Directory.Exists(source)) continue;
                Directory.Move(source, Path.Combine(destination, folder));
                moved.Add(folder);
            }
            if (Hash(metadata) != revision) throw new SandboxieException("configuration_conflict", "Snapshot metadata changed during creation.");
            ini.Set("Snapshot_" + id, "Name", name);
            ini.Set("Snapshot_" + id, "SnapshotDate", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(state.Current)) ini.Set("Snapshot_" + id, "Parent", state.Current);
            ini.Set("Current", "Snapshot", id);
            // FilePaths.dat is incremental and belongs to the frozen layer.
            var paths = Path.Combine(root, "FilePaths.dat");
            if (File.Exists(paths)) File.Delete(paths);
            ini.Save(metadata);
            return new { snapshot = State(ini).Snapshots.Single(x => x.Id == id), current = id };
        }
        catch (Exception original)
        {
            try
            {
                if (destination is not null)
                {
                    foreach (var folder in moved.AsEnumerable().Reverse())
                        if (Directory.Exists(Path.Combine(destination, folder))) Directory.Move(Path.Combine(destination, folder), Path.Combine(root, folder));
                    var paths = Path.Combine(destination, "FilePaths.dat");
                    if (File.Exists(paths) && !File.Exists(Path.Combine(root, "FilePaths.dat"))) File.Copy(paths, Path.Combine(root, "FilePaths.dat"));
                    if (Directory.Exists(destination)) Directory.Delete(destination, true);
                }
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            {
                throw new SandboxieException("partial_failure", "Snapshot creation and rollback both failed. Preserve the remaining layer.",
                new { retainedLayer = Path.GetFileName(destination), moved, cause = original is SandboxieException se ? se.Code : "io_error" });
            }
            throw;
        }
        finally { boxes.MutationGate.Release(); }
    }

    public async Task<object> Restore(string sandbox, string id, CancellationToken token)
    {
        ValidateId(id, allowEmpty: true);
        var root = boxes.Paths(sandbox, "snapshot_restore").FileRoot;
        await boxes.MutationGate.WaitAsync(token);
        var staging = Path.Combine(root, ".mcp-restore-" + Guid.NewGuid().ToString("N"));
        var moved = new List<string>();
        try
        {
            boxes.RequireStopped(sandbox);
            PathSafety.ValidateTree(root, token);
            var metadata = PathSafety.Within(root, "Snapshots.ini");
            var revision = Hash(metadata);
            var ini = IniFile.Read(metadata);
            _ = State(ini);
            if (id.Length > 0 && !ini.Sections.ContainsKey("Snapshot_" + id))
                throw new SandboxieException("not_found", "The snapshot does not exist.");
            var source = PathSafety.Within(root, "snapshot-" + id);
            if (id.Length > 0 && !File.Exists(Path.Combine(source, "RegHive")))
                throw new SandboxieException("not_found", "The snapshot registry hive is missing.");
            Directory.CreateDirectory(staging);
            foreach (var item in ContentFolders.Concat(DataFiles))
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(root, item);
                if (Directory.Exists(path)) { Directory.Move(path, Path.Combine(staging, item)); moved.Add(item); }
                else if (File.Exists(path)) { File.Move(path, Path.Combine(staging, item)); moved.Add(item); }
            }
            if (id.Length > 0)
                foreach (var file in DataFiles.Where(x => x != "FilePaths.dat"))
                    if (File.Exists(Path.Combine(source, file))) File.Copy(Path.Combine(source, file), Path.Combine(root, file));
            if (Hash(metadata) != revision) throw new SandboxieException("configuration_conflict", "Snapshot metadata changed during restore.");
            ini.Set("Current", "Snapshot", id);
            ini.Save(metadata);
            try { Directory.Delete(staging, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return new { restored = true, current = id, retainedRecoveryDirectory = Path.GetFileName(staging) }; }
            return new { restored = true, current = id, retainedRecoveryDirectory = (string?)null };
        }
        catch (Exception original)
        {
            try
            {
                foreach (var item in moved.AsEnumerable().Reverse())
                {
                    var target = Path.Combine(root, item);
                    if (Directory.Exists(target)) Directory.Delete(target, true); else if (File.Exists(target)) File.Delete(target);
                    var saved = Path.Combine(staging, item);
                    if (Directory.Exists(saved)) Directory.Move(saved, target); else File.Move(saved, target);
                }
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            {
                throw new SandboxieException("partial_failure", "Snapshot restore and rollback both failed. Preserve the recovery directory.",
                new { recoveryDirectory = Path.GetFileName(staging), moved, cause = original is SandboxieException se ? se.Code : "io_error" });
            }
            throw;
        }
        finally { boxes.MutationGate.Release(); }
    }

    public async Task<object> Delete(string sandbox, string id, CancellationToken token)
    {
        ValidateId(id);
        var root = boxes.Paths(sandbox, "snapshot_delete").FileRoot;
        await boxes.MutationGate.WaitAsync(token);
        try
        {
            boxes.RequireStopped(sandbox);
            var metadata = PathSafety.Within(root, "Snapshots.ini");
            var revision = Hash(metadata);
            var ini = IniFile.Read(metadata);
            var state = State(ini);
            if (!state.Snapshots.Any(x => x.Id == id)) throw new SandboxieException("not_found", "The snapshot does not exist.");
            if (state.Current == id || state.Default == id || state.Snapshots.Any(x => x.Parent == id))
                throw new SandboxieException("snapshot_in_use", "Only an unused leaf snapshot can be deleted. Change current/default or remove descendants first.");
            var folder = PathSafety.Within(root, "snapshot-" + id);
            PathSafety.ValidateTree(folder, token);
            var staging = PathSafety.Within(root, ".mcp-delete-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(folder)) Directory.Move(folder, staging);
            try
            {
                if (Hash(metadata) != revision) throw new SandboxieException("configuration_conflict", "Snapshot metadata changed during deletion.");
                ini.Sections.Remove("Snapshot_" + id);
                ini.Save(metadata);
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Move(staging, folder);
                throw;
            }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return new { deleted = true, id, retainedRecoveryDirectory = Path.GetFileName(staging) }; }
            return new { deleted = true, id, retainedRecoveryDirectory = (string?)null };
        }
        finally { boxes.MutationGate.Release(); }
    }

    public async Task<object> Update(string sandbox, string id, string? name, string? description, bool makeDefault, CancellationToken token)
    {
        ValidateId(id, allowEmpty: makeDefault);
        if (id.Length == 0 && (name is not null || description is not null))
            throw new ArgumentException("The base state has no name or description; only its default selection can change.");
        if (name is not null) ValidateText(name);
        if (description is not null) ValidateText(description);
        var root = boxes.Paths(sandbox, "snapshot_write").FileRoot;
        await boxes.MutationGate.WaitAsync(token);
        try
        {
            boxes.RequireStopped(sandbox);
            var metadata = PathSafety.Within(root, "Snapshots.ini");
            var ini = IniFile.Read(metadata);
            _ = State(ini);
            if (id.Length > 0 && !ini.Sections.ContainsKey("Snapshot_" + id))
                throw new SandboxieException("not_found", "The snapshot does not exist.");
            if (name is not null) ini.Set("Snapshot_" + id, "Name", name);
            if (description is not null) ini.Set("Snapshot_" + id, "Description", description);
            if (makeDefault) ini.Set("Current", "Default", id);
            ini.Save(metadata);
            return State(ini);
        }
        finally { boxes.MutationGate.Release(); }
    }

    private static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing";
    private static void ValidateText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 1024 || value.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new ArgumentException("Snapshot text must be a single line up to 1024 characters.");
    }
    public static void ValidateId(string id, bool allowEmpty = false)
    {
        if (id is null || !(allowEmpty && id.Length == 0) && !SnapshotId().IsMatch(id))
            throw new ArgumentException("Snapshot IDs must be numeric.");
    }
    [GeneratedRegex("^[1-9][0-9]{0,8}$")] private static partial Regex SnapshotId();
}
