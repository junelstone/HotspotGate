using System.Net;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Neighbors;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public class AccessTableTests
{
    private const string PhoneMac = "DA:A1:19:00:00:01";
    private const string LaptopMac = "00:1A:2B:3C:4D:5E";

    private static NeighborEntry N(string ip, string mac, NeighborState state = NeighborState.Reachable) =>
        new(7, IPAddress.Parse(ip), mac, state);

    [Fact]
    public void Device_keeps_access_when_its_ip_changes()
    {
        var table = AccessTableBuilder.Build(new AccessInputs(
            [new AccessSession(7, PhoneMac, "192.168.137.23")], [], [],
            [N("192.168.137.88", PhoneMac), N("192.168.137.23", LaptopMac)], HotspotAllowed: true));

        // New IP from the neighbour table; the old IP now belongs to the laptop, so it is NOT authorised.
        Assert.Equal(new AccessEntry(PhoneMac, 7), table[Ip("192.168.137.88")]);
        Assert.False(table.ContainsKey(Ip("192.168.137.23")));
    }

    [Fact]
    public void Session_ip_is_used_while_the_device_is_not_in_the_neighbour_table_yet()
    {
        var table = AccessTableBuilder.Build(new AccessInputs([new AccessSession(7, PhoneMac, "192.168.137.23")], [], [], [], true));
        Assert.Equal(7, table[Ip("192.168.137.23")].SessionId);
    }

    [Fact]
    public void Whitelisted_devices_pass_without_session_blocked_ones_never()
    {
        var table = AccessTableBuilder.Build(new AccessInputs(
            [new AccessSession(9, LaptopMac, "192.168.137.50")], [PhoneMac, LaptopMac], [LaptopMac],
            [N("192.168.137.23", PhoneMac), N("192.168.137.50", LaptopMac)], true));

        Assert.Equal(new AccessEntry(PhoneMac, null), Assert.Single(table).Value);
    }

    [Fact]
    public void Unreachable_neighbours_are_ignored_and_nothing_passes_when_the_hotspot_is_stopped()
    {
        var inputs = new AccessInputs([], [PhoneMac], [], [N("192.168.137.23", PhoneMac, NeighborState.Unreachable)], true);
        Assert.Empty(AccessTableBuilder.Build(inputs));

        var stopped = new AccessInputs([], [PhoneMac], [], [N("192.168.137.23", PhoneMac)], HotspotAllowed: false);
        Assert.Empty(AccessTableBuilder.Build(stopped));
    }
}
