using System.Net;
using System.Net.NetworkInformation;

namespace StonesHotspot.Network.Adapters;

/// <summary>An IPv4 address with its prefix length, e.g. 192.168.137.1/24.</summary>
public readonly record struct Ipv4Subnet(IPAddress Address, int PrefixLength)
{
    public uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);
    public uint Network => ToUInt32(Address) & Mask;

    public bool Contains(IPAddress ip) => (ToUInt32(ip) & Mask) == Network;

    public bool Overlaps(Ipv4Subnet other)
    {
        var mask = PrefixLength < other.PrefixLength ? Mask : other.Mask;
        return (ToUInt32(Address) & mask) == (ToUInt32(other.Address) & mask);
    }

    public override string ToString() => $"{Address}/{PrefixLength}";

    public static uint ToUInt32(IPAddress ip)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    public static Ipv4Subnet Parse(string cidr)
    {
        var parts = cidr.Split('/');
        return new Ipv4Subnet(IPAddress.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 32);
    }
}

/// <param name="Id">Windows adapter GUID, e.g. "{1A2B…}". Stable across reboots; used to remember the choice.</param>
/// <param name="InterfaceIndex">IPv4 interface index, used to read the neighbour table.</param>
/// <param name="Mac">AA:BB:CC:DD:EE:FF, or empty for adapters without one.</param>
public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    NetworkInterfaceType Type,
    bool IsUp,
    int InterfaceIndex,
    string Mac,
    IReadOnlyList<Ipv4Subnet> Addresses,
    IReadOnlyList<IPAddress> Gateways)
{
    /// <summary>IPv4 DNS servers configured on this adapter (used as upstream for the customers' DNS).</summary>
    public IReadOnlyList<IPAddress> DnsServers { get; init; } = [];

    public bool HasDefaultGateway => Gateways.Any(g => !g.Equals(IPAddress.Any));

    /// <summary>Windows "Mobile hotspot" creates this virtual adapter for the PC's own Wi-Fi access point.</summary>
    public bool IsMobileHotspot =>
        Description.Contains("Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
        || Description.Contains("Hosted Network Virtual Adapter", StringComparison.OrdinalIgnoreCase);

    public bool IsWireless => Type == NetworkInterfaceType.Wireless80211;
}
