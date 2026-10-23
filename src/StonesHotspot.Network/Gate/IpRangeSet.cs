using System.Net;
using StonesHotspot.Network.Adapters;

namespace StonesHotspot.Network.Gate;

/// <summary>A fixed set of IPv4 addresses and CIDR ranges (web-filter IP rules).</summary>
public sealed class IpRangeSet
{
    private readonly HashSet<uint> _addresses = [];
    private readonly List<(uint Network, uint Mask)> _ranges = [];

    public static IpRangeSet Empty { get; } = new([]);

    public IpRangeSet(IEnumerable<Ipv4Subnet> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.PrefixLength >= 32)
                _addresses.Add(Ipv4Subnet.ToUInt32(entry.Address));
            else
                _ranges.Add((entry.Network, entry.Mask));
        }
    }

    public int Count => _addresses.Count + _ranges.Count;

    public bool Contains(uint address)
    {
        if (_addresses.Contains(address))
            return true;
        foreach (var (network, mask) in _ranges)
        {
            if ((address & mask) == network)
                return true;
        }
        return false;
    }

    /// <summary>Parses "1.2.3.4" or "1.2.3.0/24"; false for anything else (e.g. a domain name).</summary>
    public static bool TryParse(string text, out Ipv4Subnet subnet)
    {
        subnet = default;
        var parts = text.Trim().Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        var prefix = 32;
        if (parts.Length == 2 && (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out prefix) || prefix is < 1 or > 32))
            return false;
        subnet = new Ipv4Subnet(ip, prefix);
        return true;
    }
}
