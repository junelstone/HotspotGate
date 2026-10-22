using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using static StonesHotspot.Tests.Infrastructure.FakeAdapters;

namespace StonesHotspot.Tests;

public class NetworkRulesTests
{
    // ---- Subnets ----

    [Theory]
    [InlineData("192.168.137.1/24", "192.168.137.200", true)]
    [InlineData("192.168.137.1/24", "192.168.138.1", false)]
    [InlineData("10.0.0.5/8", "10.255.1.1", true)]
    public void Subnet_contains(string cidr, string ip, bool expected) =>
        Assert.Equal(expected, Ipv4Subnet.Parse(cidr).Contains(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("192.168.137.50/24", true)]
    [InlineData("192.168.0.10/16", true)]   // a /16 covering 192.168.137.x
    [InlineData("192.168.1.20/24", false)]
    public void Subnet_overlap_with_ics_network(string cidr, bool expected) =>
        Assert.Equal(expected, Ipv4Subnet.Parse(cidr).Overlaps(NetworkSetupValidator.IcsSubnet));

    // ---- Adapter suggestions ----

    [Fact]
    public void Suggests_the_adapter_with_a_gateway_as_wan_and_the_other_ethernet_first_as_lan()
    {
        var suggestion = AdapterAdvisor.Suggest(TypicalPc());

        Assert.Equal(EthernetId, suggestion.Wan?.Id);
        Assert.Equal(LanId, suggestion.LanCandidates[0].Id);
        Assert.DoesNotContain(suggestion.LanCandidates, a => a.Id == EthernetId);
    }

    [Fact]
    public void Prefers_cable_over_wifi_for_wan_and_puts_mobile_hotspot_first_for_lan()
    {
        var adapters = new List<NetworkAdapterInfo>
        {
            Adapter(WifiId, "Wi-Fi", NetworkInterfaceType.Wireless80211, cidr: "192.168.8.10/24", gateway: "192.168.8.1"),
            Adapter(EthernetId, "Ethernet", cidr: "192.168.1.20/24", gateway: "192.168.1.1"),
            Adapter(HotspotId, "Connexion au réseau local* 10", NetworkInterfaceType.Wireless80211, cidr: "192.168.137.1/24",
                description: "Microsoft Wi-Fi Direct Virtual Adapter #2"),
        };

        var suggestion = AdapterAdvisor.Suggest(adapters);

        Assert.Equal(EthernetId, suggestion.Wan?.Id);
        Assert.Equal(HotspotId, suggestion.LanCandidates[0].Id);
        Assert.True(suggestion.LanCandidates[0].IsMobileHotspot);
    }

    // ---- Validation ----

    private static IReadOnlyList<NetworkProblemCode> Codes(IEnumerable<NetworkProblem> problems) => problems.Select(p => p.Code).ToList();

    [Fact]
    public void Valid_choice_has_no_errors()
    {
        var problems = NetworkSetupValidator.Validate(TypicalPc(), EthernetId, LanId, isAdministrator: true, isWindows: true);
        Assert.False(problems.HasErrors());
    }

    [Fact]
    public void Reports_missing_admin_rights_and_non_windows()
    {
        var codes = Codes(NetworkSetupValidator.Validate(TypicalPc(), EthernetId, LanId, isAdministrator: false, isWindows: false));
        Assert.Contains(NetworkProblemCode.NotAdministrator, codes);
        Assert.Contains(NetworkProblemCode.NotWindows, codes);
    }

    [Fact]
    public void Reports_no_lan_adapter_when_the_pc_has_only_one()
    {
        var onlyWan = TypicalPc().Take(1).ToList();
        Assert.Contains(NetworkProblemCode.NoLanAdapter, Codes(NetworkSetupValidator.Validate(onlyWan, EthernetId, null, true, true)));
    }

    [Fact]
    public void Rejects_same_adapter_and_unknown_adapter()
    {
        Assert.Equal([NetworkProblemCode.SameAdapter], Codes(NetworkSetupValidator.Validate(TypicalPc(), EthernetId, EthernetId, true, true)));
        Assert.Contains(NetworkProblemCode.AdapterNotFound, Codes(NetworkSetupValidator.Validate(TypicalPc(), EthernetId, "{gone}", true, true)));
    }

    [Fact]
    public void Detects_ip_conflict_when_the_internet_side_already_uses_192_168_137()
    {
        var adapters = TypicalPc();
        adapters[0] = Adapter(EthernetId, "Ethernet", cidr: "192.168.137.20/24", gateway: "192.168.137.1");

        var problem = Assert.Single(NetworkSetupValidator.Validate(adapters, EthernetId, LanId, true, true));
        Assert.Equal(NetworkProblemCode.IcsSubnetConflict, problem.Code);
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
    }

    [Fact]
    public void Warns_when_lan_cable_is_unplugged_or_wan_has_no_gateway()
    {
        var adapters = TypicalPc();
        adapters[0] = Adapter(EthernetId, "Ethernet", cidr: "192.168.1.20/24");
        adapters[1] = adapters[1] with { IsUp = false };

        var problems = NetworkSetupValidator.Validate(adapters, EthernetId, LanId, true, true);
        Assert.False(problems.HasErrors());
        Assert.Equal([NetworkProblemCode.WanNoGateway, NetworkProblemCode.LanDown], Codes(problems));
    }

    // ---- Neighbour table ----

    private static byte[] NeighborTable(params (ushort Family, int Index, string Ip, string Mac, int MacLength, NeighborState State)[] rows)
    {
        var buffer = new byte[NeighborTableParser.HeaderSize + rows.Length * NeighborTableParser.RowSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var row = buffer.AsSpan(NeighborTableParser.HeaderSize + i * NeighborTableParser.RowSize);
            var (family, index, ip, mac, macLength, state) = rows[i];
            BinaryPrimitives.WriteUInt16LittleEndian(row, family);
            IPAddress.Parse(ip).GetAddressBytes().CopyTo(row[4..]);
            BinaryPrimitives.WriteUInt32LittleEndian(row[28..], (uint)index);
            Convert.FromHexString(mac.Replace(":", "")).CopyTo(row[40..]);
            BinaryPrimitives.WriteUInt32LittleEndian(row[72..], (uint)macLength);
            BinaryPrimitives.WriteInt32LittleEndian(row[76..], (int)state);
        }
        return buffer;
    }

    [Fact]
    public void Parses_the_raw_neighbour_table()
    {
        var buffer = NeighborTable(
            (2, 7, "192.168.137.23", "da:a1:19:00:00:01", 6, NeighborState.Reachable),
            (23, 7, "0.0.0.0", "00:00:00:00:00:00", 6, NeighborState.Reachable),   // IPv6 row: skipped
            (2, 7, "192.168.137.255", "ff:ff:ff:ff:ff:ff", 6, NeighborState.Permanent),
            (2, 4, "192.168.1.1", "00:00:00:00:00:00", 0, NeighborState.Incomplete)); // no MAC yet: skipped

        var entries = NeighborTableParser.Parse(buffer);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new NeighborEntry(7, IPAddress.Parse("192.168.137.23"), "DA:A1:19:00:00:01", NeighborState.Reachable), entries[0]);
        Assert.Equal(NeighborState.Permanent, entries[1].State);
    }

    [Fact]
    public void Truncated_neighbour_table_does_not_crash() =>
        Assert.Empty(NeighborTableParser.Parse(NeighborTable((2, 7, "192.168.137.23", "00:11:22:33:44:55", 6, NeighborState.Reachable)).AsSpan(0, 50)));

    [Fact]
    public void Reads_hostnames_from_hosts_ics()
    {
        var map = IcsHostsFile.Parse(
        [
            "# Copyright (c) 1993-2001 Microsoft Corp.",
            "192.168.137.1 DESKTOP-SHOP.mshome.net # 2026 3 2 8 0 0 0",
            "192.168.137.23\tGalaxy-A14.mshome.net # 2026 3 2 8 5 0 0",
            "",
            "not-an-ip something",
        ]);

        Assert.Equal(2, map.Count);
        Assert.Equal("Galaxy-A14.mshome.net", map[IPAddress.Parse("192.168.137.23")]);
    }

    // ---- Connected devices ----

    private static readonly NetworkAdapterInfo IcsLan =
        Adapter(LanId, "Ethernet 2", cidr: "192.168.137.1/24", index: 7, mac: "00:E0:4C:68:00:01");

    private static NeighborEntry Entry(string ip, string mac, NeighborState state = NeighborState.Reachable, int index = 7) =>
        new(index, IPAddress.Parse(ip), mac, state);

    [Fact]
    public void Lists_live_unicast_devices_on_the_lan_with_hostnames()
    {
        var entries = new[]
        {
            Entry("192.168.137.80", "00:1A:2B:3C:4D:5E", NeighborState.Stale),
            Entry("192.168.137.23", "DA:A1:19:00:00:01"),
            Entry("192.168.137.99", "00:1A:2B:3C:4D:99", NeighborState.Unreachable),  // gone
            Entry("192.168.137.255", "FF:FF:FF:FF:FF:FF", NeighborState.Permanent),   // broadcast
            Entry("224.0.0.22", "01:00:5E:00:00:16", NeighborState.Permanent),        // multicast
            Entry("192.168.1.1", "00:AA:BB:CC:DD:EE", index: 4),                      // WAN router
        };
        var hostnames = new Dictionary<IPAddress, string> { [IPAddress.Parse("192.168.137.23")] = "Galaxy-A14" };

        var result = ConnectedDeviceScanner.Scan(entries, IcsLan, hostnames, DateTime.UtcNow);

        Assert.Empty(result.Problems);
        Assert.Equal(["192.168.137.23", "192.168.137.80"], result.Devices.Select(d => d.Ip.ToString()));
        Assert.Equal("Galaxy-A14", result.Devices[0].Hostname);
        Assert.True(result.Devices[0].IsRandomMac);
        Assert.False(result.Devices[1].IsRandomMac);
    }

    [Fact]
    public void Detects_another_device_on_the_gateway_address()
    {
        var result = ConnectedDeviceScanner.Scan([Entry("192.168.137.1", "C4:6E:1F:00:00:01")], IcsLan,
            new Dictionary<IPAddress, string>(), DateTime.UtcNow);

        var problem = Assert.Single(result.Problems);
        Assert.Equal(NetworkProblemCode.GatewayIpConflict, problem.Code);
        Assert.Contains("C4:6E:1F:00:00:01", problem.Detail);
        Assert.Empty(result.Devices);
    }

    [Fact]
    public void Warns_about_devices_outside_the_hotspot_subnet()
    {
        var result = ConnectedDeviceScanner.Scan([Entry("192.168.0.100", "00:1A:2B:3C:4D:5E")], IcsLan,
            new Dictionary<IPAddress, string>(), DateTime.UtcNow);

        Assert.Equal(NetworkProblemCode.DevicesOutsideSubnet, Assert.Single(result.Problems).Code);
        Assert.Empty(result.Devices);
    }
}
