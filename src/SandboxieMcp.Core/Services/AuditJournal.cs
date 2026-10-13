using System.Text.Json;
using SandboxieMcp.Core.Security;

namespace SandboxieMcp.Core.Services;

public sealed class AuditJournal(string stateDirectory)
{
    private readonly object sync = new();
    public void Record(string operation, string? sandbox, string outcome)
    {
        lock (sync)
        {
            PathSafety.NoLinks(stateDirectory);
            Directory.CreateDirectory(stateDirectory);
            var path = Path.Combine(stateDirectory, "audit.jsonl");
            PathSafety.NoLinks(path);
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, operation, sandbox, outcome }) + "\n");
        }
    }

    public object Read(int limit, PolicyGuard guard)
    {
        guard.Demand("audit_read");
        if (limit is < 1 or > 200) throw new ArgumentException("limit must be 1..200.");
        var path = Path.Combine(stateDirectory, "audit.jsonl");
        PathSafety.NoLinks(path);
        lock (sync)
        {
            var rows = File.Exists(path) ? File.ReadLines(path).TakeLast(1000).Select(x => JsonDocument.Parse(x)).ToArray() : [];
            try
            {
                return rows.Where(x => x.RootElement.GetProperty("sandbox").ValueKind == JsonValueKind.Null ||
                    guard.CanSeeSandbox(x.RootElement.GetProperty("sandbox").GetString()!))
                    .TakeLast(limit).Select(x => x.RootElement.Clone()).ToArray();
            }
            finally { foreach (var row in rows) row.Dispose(); }
        }
    }
}
