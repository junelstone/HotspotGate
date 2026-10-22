using System.Net;

namespace StonesHotspot.Network.Neighbors;

/// <summary>
/// Windows ICS writes the names of the devices its DHCP server served to
/// <c>%SystemRoot%\System32\drivers\etc\hosts.ics</c> ("192.168.137.23  Galaxy-A14  # …").
/// </summary>
public static class IcsHostsFile
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts.ics");

    /// <summary>IP → hostname. Reads nothing (empty map) if the file is missing or locked.</summary>
    public static IReadOnlyDictionary<IPAddress, string> Read(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file) ? Parse(File.ReadAllLines(file)) : new Dictionary<IPAddress, string>();
        }
        catch (IOException)
        {
            return new Dictionary<IPAddress, string>();
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<IPAddress, string>();
        }
    }

    public static IReadOnlyDictionary<IPAddress, string> Parse(IEnumerable<string> lines)
    {
        var map = new Dictionary<IPAddress, string>();
        foreach (var raw in lines)
        {
            var line = raw.Split('#')[0].Trim();
            var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out var ip))
                map[ip] = parts[1];
        }
        return map;
    }
}
