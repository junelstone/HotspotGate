using System.Net;
using System.Net.NetworkInformation;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Ics;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;

namespace StonesHotspot.Tests.Infrastructure;

public static class FakeAdapters
{
    public const string EthernetId = "{11111111-0000-0000-0000-000000000001}";
    public const string LanId = "{22222222-0000-0000-0000-000000000002}";
    public const string WifiId = "{33333333-0000-0000-0000-000000000003}";
    public const string HotspotId = "{44444444-0000-0000-0000-000000000004}";

    public static NetworkAdapterInfo Adapter(
        string id, string name, NetworkInterfaceType type = NetworkInterfaceType.Ethernet, bool up = true,
        string? cidr = null, string? gateway = null, string description = "Realtek PCIe GbE", int index = 0,
        string mac = "00:11:22:33:44:55") =>
        new(id, name, description, type, up, index, mac,
            cidr is null ? [] : [Ipv4Subnet.Parse(cidr)],
            gateway is null ? [] : [IPAddress.Parse(gateway)]);

    /// <summary>A typical shop PC: Ethernet to the router (WAN), second Ethernet to the access point (LAN), Wi-Fi idle.</summary>
    public static List<NetworkAdapterInfo> TypicalPc() =>
    [
        Adapter(EthernetId, "Ethernet", cidr: "192.168.1.20/24", gateway: "192.168.1.1", index: 4),
        Adapter(LanId, "Ethernet 2", cidr: "169.254.10.5/16", description: "USB 3.0 Gigabit Ethernet", index: 7, mac: "00:E0:4C:68:00:01"),
        Adapter(WifiId, "Wi-Fi", NetworkInterfaceType.Wireless80211, up: false, description: "Intel Wi-Fi 6 AX201", index: 9),
    ];
}

public sealed class FakeAdapterProvider(List<NetworkAdapterInfo> adapters) : INetworkAdapterProvider
{
    public List<NetworkAdapterInfo> Adapters { get; } = adapters;
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => Adapters;
}

public sealed class FakeIcsController : IIcsController
{
    public List<IcsShare> Shares { get; } = [];
    public string? FailWith { get; set; }
    public int EnableCalls { get; private set; }

    public IcsStatus GetStatus() => new(true, Shares.ToList());

    public void Enable(string wanId, string lanId)
    {
        EnableCalls++;
        if (FailWith is not null)
            throw new IcsException(FailWith);
        Shares.Clear();
        Shares.Add(new IcsShare(wanId, "WAN", IcsRole.Public));
        Shares.Add(new IcsShare(lanId, "LAN", IcsRole.Private));
    }

    public void Disable() => Shares.Clear();
}

public sealed class FakePrivileges(bool admin = true, bool windows = true) : IPrivilegeChecker
{
    public bool IsAdministrator { get; set; } = admin;
    public bool IsWindows { get; set; } = windows;
}

public sealed class FakeNeighborTable : INeighborTable
{
    public List<NeighborEntry> Entries { get; } = [];
    public IReadOnlyList<NeighborEntry> GetIpv4Entries() => Entries;
}
