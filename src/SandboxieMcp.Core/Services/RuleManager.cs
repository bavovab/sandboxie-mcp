using System.Globalization;
using System.Net;

namespace SandboxieMcp.Core.Services;

public sealed record NetworkRule(string Action, string Process = "*", string? Ports = null,
    string? Addresses = null, string Protocol = "Any");

public sealed class RuleManager(SandboxManager boxes)
{
    private static readonly HashSet<string> ResourceSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpenFilePath", "ClosedFilePath", "ReadFilePath", "WriteFilePath", "NormalFilePath",
        "OpenKeyPath", "ClosedKeyPath", "ReadKeyPath", "WriteKeyPath", "NormalKeyPath",
        "OpenIpcPath", "ClosedIpcPath", "OpenWinClass", "ClosedWinClass", "OpenClsid", "ClosedClsid"
    };
    private static readonly HashSet<string> ProgramSettings = new(StringComparer.OrdinalIgnoreCase)
    { "ForceProcess", "ForceFolder", "BreakoutProcess", "LeaderProcess", "LingerProcess", "ProcessGroup" };

    public object Inventory(string sandbox)
    {
        sandbox = boxes.RequireBox(sandbox, "config_read");
        var config = boxes.ReadConfiguration(sandbox);
        return new
        {
            config.Revision,
            rules = config.Settings.Where(x => ResourceSettings.Contains(x.Key) ||
            ProgramSettings.Contains(x.Key) || x.Key.Equals("NetworkAccess", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value)
        };
    }

    public Task<object> Resources(string sandbox, string setting, string[] rules, string revision, CancellationToken token)
    {
        _ = boxes.RequireBox(sandbox, "config_write");
        if (!ResourceSettings.Contains(setting)) throw new ArgumentException("Unsupported resource rule setting.");
        return boxes.Apply(sandbox, [new(setting, rules)], revision, token);
    }

    public Task<object> Programs(string sandbox, string setting, string[] rules, string revision, CancellationToken token)
    {
        _ = boxes.RequireBox(sandbox, "config_write");
        if (!ProgramSettings.Contains(setting)) throw new ArgumentException("Unsupported program rule setting.");
        return boxes.Apply(sandbox, [new(setting, rules)], revision, token);
    }

    public Task<object> Network(string sandbox, NetworkRule[] rules, string revision, CancellationToken token)
    {
        _ = boxes.RequireBox(sandbox, "config_write");
        if (rules is null || rules.Length > 512) throw new ArgumentException("Supply at most 512 network rules.");
        return boxes.Apply(sandbox, [new("NetworkAccess", rules.Select(Format).ToArray())], revision, token);
    }

    public static string Format(NetworkRule rule)
    {
        if (rule is null || rule.Action is not ("allow" or "block") ||
            rule.Process is null || rule.Process.Length is < 1 or > 128 ||
            rule.Process.IndexOfAny([',', ';', '\r', '\n', '\0']) >= 0 ||
            rule.Protocol is not ("Any" or "TCP" or "UDP" or "ICMP"))
            throw new ArgumentException("Invalid network action, process selector or protocol.");
        var result = rule.Process + "," + (rule.Action == "allow" ? "Allow" : "Block");
        if (rule.Ports is not null)
        {
            ValidateSet(rule.Ports, part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535);
            foreach (var item in rule.Ports.Split(','))
            {
                var range = item.Split('-');
                if (range.Length == 2 && int.Parse(range[0], CultureInfo.InvariantCulture) > int.Parse(range[1], CultureInfo.InvariantCulture))
                    throw new ArgumentException("Port ranges must be ascending.");
            }
            result += ";Port=" + rule.Ports;
        }
        if (rule.Addresses is not null)
        {
            ValidateSet(rule.Addresses, part => IPAddress.TryParse(part, out _));
            foreach (var item in rule.Addresses.Split(','))
            {
                var range = item.Split('-');
                if (range.Length == 2)
                {
                    var start = IPAddress.Parse(range[0]).GetAddressBytes();
                    var end = IPAddress.Parse(range[1]).GetAddressBytes();
                    if (start.Length != end.Length || start.AsSpan().SequenceCompareTo(end) > 0)
                        throw new ArgumentException("Address ranges must have the same family and be ascending.");
                }
            }
            result += ";Address=" + rule.Addresses;
        }
        if (rule.Protocol != "Any") result += ";Protocol=" + rule.Protocol;
        return result;
    }

    private static void ValidateSet(string value, Func<string, bool> validate)
    {
        if (value.Length is < 1 or > 2048) throw new ArgumentException("Invalid network endpoint set.");
        foreach (var entry in value.Split(','))
        {
            var range = entry.Split('-');
            if (range.Length is < 1 or > 2 || range.Any(x => !validate(x)))
                throw new ArgumentException("Use numeric ports or IP addresses, with comma sets or ascending ranges.");
        }
    }
}
