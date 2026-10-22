using System.Net;
using StonesHotspot.Core.Rules;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Neighbors;

namespace StonesHotspot.Network.Setup;

public sealed record ConnectedDevice(string Mac, IPAddress Ip, string? Hostname, NeighborState State)
{
    public bool IsRandomMac => MacAddress.IsRandomized(Mac);
}

public sealed record DeviceScanResult(DateTime ScannedAt, IReadOnlyList<ConnectedDevice> Devices, IReadOnlyList<NetworkProblem> Problems)
{
    public static DeviceScanResult Empty { get; } = new(DateTime.MinValue, [], []);
}

/// <summary>Turns the raw neighbour table into the list of customer devices on the LAN adapter.</summary>
public static class ConnectedDeviceScanner
{
    public static DeviceScanResult Scan(
        IReadOnlyList<NeighborEntry> entries, NetworkAdapterInfo lan, IReadOnlyDictionary<IPAddress, string> hostnames, DateTime now)
    {
        var problems = new List<NetworkProblem>();
        var devices = new List<ConnectedDevice>();
        var lanSubnets = lan.Addresses;
        var gateways = lanSubnets.Select(s => s.Address).ToHashSet();
        var outside = new List<string>();

        foreach (var entry in entries.Where(e => e.InterfaceIndex == lan.InterfaceIndex))
        {
            if (!IsUnicastMac(entry.Mac) || IsMulticastOrBroadcast(entry.Ip, lanSubnets))
                continue;

            if (gateways.Contains(entry.Ip))
            {
                // Another device answers on the PC's own gateway address: the classic "access point in
                // router mode" or a second DHCP server on the network.
                if (entry.IsLive && !string.Equals(entry.Mac, lan.Mac, StringComparison.OrdinalIgnoreCase))
                    problems.Add(NetworkProblem.Error(NetworkProblemCode.GatewayIpConflict, $"{entry.Ip} = {entry.Mac}"));
                continue;
            }

            if (!entry.IsLive)
                continue;

            if (lanSubnets.Count > 0 && !lanSubnets.Any(s => s.Contains(entry.Ip)))
            {
                outside.Add($"{entry.Ip} ({entry.Mac})");
                continue;
            }

            devices.Add(new ConnectedDevice(entry.Mac, entry.Ip, hostnames.GetValueOrDefault(entry.Ip), entry.State));
        }

        if (outside.Count > 0)
            problems.Add(NetworkProblem.Warning(NetworkProblemCode.DevicesOutsideSubnet, string.Join(", ", outside)));

        return new DeviceScanResult(now, devices.OrderBy(d => Ipv4Subnet.ToUInt32(d.Ip)).ToList(), problems);
    }

    private static bool IsUnicastMac(string mac) =>
        MacAddress.TryNormalize(mac, out var m)
        && m != "00:00:00:00:00:00"
        && (Convert.ToByte(m[..2], 16) & 0x01) == 0; // group bit = multicast/broadcast

    private static bool IsMulticastOrBroadcast(IPAddress ip, IReadOnlyList<Ipv4Subnet> subnets)
    {
        var value = Ipv4Subnet.ToUInt32(ip);
        if (value >= 0xE0000000) // 224.0.0.0 and above: multicast, reserved, 255.255.255.255
            return true;
        return subnets.Any(s => s.PrefixLength < 31 && value == (s.Network | ~s.Mask));
    }
}
