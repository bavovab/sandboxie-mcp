using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace SandboxieMcp.Core.Services;

public static class SandboxieEventLog
{
    public static object Read(long after, int limit)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Event Log requires Windows.");
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System[(Provider[@Name='SbieDrv'] or Provider[@Name='SbieSvc']) and EventRecordID > " +
            after.ToString(CultureInfo.InvariantCulture) + "]]");
        using var reader = new EventLogReader(query);
        var messages = new List<object>();
        var cursor = after;
        for (var i = 0; i < limit; i++)
        {
            using var record = reader.ReadEvent();
            if (record is null) break;
            var number = record.RecordId ?? throw new InvalidDataException("The event has no record identifier.");
            var insertions = new List<string>();
            foreach (var property in record.Properties)
                insertions.Add(Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? "");
            string? text = null;
            int? formatError = null;
            try { text = record.FormatDescription(); }
            catch (EventLogException error) { formatError = error.HResult; }
            messages.Add(new
            {
                number,
                code = record.Id,
                processId = record.ProcessId,
                provider = record.ProviderName,
                timeUtc = record.TimeCreated?.ToUniversalTime(),
                text = text ?? string.Join(" | ", insertions),
                insertions,
                formatted = text is not null,
                formatError
            });
            cursor = number;
        }
        return new { source = "event-log", nextCursor = cursor, messages };
    }
}
