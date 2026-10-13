using System.Text;

namespace SandboxieMcp.Core.Services;

/// <summary>Small UTF-8 INI reader/writer for Sandboxie snapshot metadata, not Sandboxie.ini.</summary>
public sealed class IniFile
{
    public Dictionary<string, Dictionary<string, string>> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Get(string section, string key) => Sections.GetValueOrDefault(section)?.GetValueOrDefault(key);
    public void Set(string section, string key, string value)
    {
        if (!Sections.TryGetValue(section, out var values)) Sections[section] = values = new(StringComparer.OrdinalIgnoreCase);
        values[key] = value;
    }

    public static IniFile Read(string path)
    {
        var ini = new IniFile();
        if (!File.Exists(path)) return ini;
        if (new FileInfo(path).Length > 1_048_576) throw new ArgumentException("Snapshot metadata exceeds 1 MiB.");
        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var separator = line.IndexOf('=');
            if (separator > 0) ini.Set(section, line[..separator].Trim(), Unescape(line[(separator + 1)..].Trim()));
        }
        return ini;
    }

    public void Save(string path)
    {
        var text = new StringBuilder();
        foreach (var section in Sections)
        {
            text.Append('[').Append(section.Key).AppendLine("]");
            foreach (var pair in section.Value) text.Append(pair.Key).Append('=').AppendLine(Escape(pair.Value));
            text.AppendLine();
        }
        var temp = path + ".mcp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Escape(string text) => text.Contains(',') || text.Contains(';') || text.Contains('=') || text.StartsWith(' ') || text.EndsWith(' ')
        ? "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : text.Replace("\\", "\\\\");
    private static string Unescape(string text)
    {
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2) text = text[1..^1];
        return text.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
