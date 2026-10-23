using System.Collections.Frozen;
using Microsoft.Extensions.Time.Testing;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Dns;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public sealed class PacketProcessorTests
{
    private const string Gateway = "192.168.137.1";
    private const string Phone = "192.168.137.23";    // not logged in
    private const string Laptop = "192.168.137.50";   // logged in (session 7)
    private const string Web = "93.184.216.34";
    private const ushort PortalPort = 80;

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-03-02T08:00:00Z"));
    private readonly PacketProcessor _gate;

    public PacketProcessorTests()
    {
        _gate = new PacketProcessor(_clock);
        _gate.Update(Snapshot());
    }

    private static GateSnapshot Snapshot(FilterRules? rules = null) =>
        new(Ipv4Subnet.Parse($"{Gateway}/24"), Ip(Gateway), PortalPort,
            new Dictionary<uint, AccessEntry> { [Ip(Laptop)] = new("00:11:22:33:44:55", 7) }.ToFrozenDictionary(),
            rules ?? FilterRules.Empty);

    private GateVerdict Forward(byte[] packet) => _gate.Process(packet, PacketSource.Forward, outbound: false);

    [Fact]
    public void Unauthorised_http_is_redirected_to_the_portal_and_replies_look_like_the_web_server()
    {
        var syn = Tcp(Phone, 50123, Web, 80, TcpFlags.Syn);

        Assert.Equal(GateVerdict.RedirectToPortal, Forward(syn));
        Ipv4Packet.TryParse(syn, out var redirected);
        Assert.Equal((Ip(Gateway), PortalPort), (redirected.Destination, redirected.DestinationPort));
        Assert.True(ChecksumsValid(syn));

        // Portal → phone: rewritten so the phone's TCP stack sees the server it asked for.
        var synAck = Tcp(Gateway, PortalPort, Phone, 50123, TcpFlags.Syn | TcpFlags.Ack);
        Assert.Equal(GateVerdict.PassModified, _gate.Process(synAck, PacketSource.Local, outbound: true));
        Ipv4Packet.TryParse(synAck, out var reply);
        Assert.Equal((Ip(Web), (ushort)80), (reply.Source, reply.SourcePort));
        Assert.True(ChecksumsValid(synAck));

        Assert.Equal(1, _gate.Counters.Redirected);
        Assert.Equal(1, _gate.RedirectFlowCount);
    }

    [Fact]
    public void Portal_traffic_that_was_not_redirected_is_left_alone()
    {
        var direct = Tcp(Gateway, PortalPort, Phone, 50999, TcpFlags.Ack);
        Assert.Equal(GateVerdict.Pass, _gate.Process(direct, PacketSource.Local, outbound: true));
    }

    [Fact]
    public void Unauthorised_https_gets_a_reset_and_other_traffic_is_dropped()
    {
        Assert.Equal(GateVerdict.Reset, Forward(Tcp(Phone, 50124, "142.250.1.1", 443, TcpFlags.Syn)));
        Assert.Equal(GateVerdict.Drop, Forward(Tcp(Phone, 50124, "142.250.1.1", 443, TcpFlags.Rst)));
        Assert.Equal(GateVerdict.Drop, Forward(Udp(Phone, 4444, "142.250.1.1", 443)));   // QUIC
        Assert.Equal(GateVerdict.Drop, Forward(Icmp(Phone, "8.8.8.8")));
        Assert.Equal(GateVerdict.Drop, Forward(Tcp(Web, 443, Phone, 50124, TcpFlags.Ack))); // download
    }

    [Fact]
    public void Authorised_device_passes_in_both_directions_and_bytes_are_counted()
    {
        var up = Tcp(Laptop, 50000, Web, 443, TcpFlags.Ack, payload: new byte[100]);
        var down = Tcp(Web, 443, Laptop, 50000, TcpFlags.Ack, payload: new byte[1000]);

        Assert.Equal(GateVerdict.Pass, Forward(up));
        Assert.Equal(GateVerdict.Pass, Forward(down));

        var counts = _gate.TakeByteCounts();
        Assert.Equal((140L, 1040L), counts[Ip(Laptop)]);
        Assert.Empty(_gate.TakeByteCounts()); // taken
    }

    [Fact]
    public void Revoking_cuts_the_device_off_at_the_next_packet()
    {
        var packet = Tcp(Laptop, 50000, Web, 443, TcpFlags.Ack);
        Assert.Equal(GateVerdict.Pass, Forward((byte[])packet.Clone()));

        _gate.Update(Snapshot() with { Authorized = FrozenDictionary<uint, AccessEntry>.Empty });

        Assert.Equal(GateVerdict.Reset, Forward(packet));
    }

    [Fact]
    public void Every_dns_query_goes_to_our_responder()
    {
        Assert.Equal(GateVerdict.Dns, _gate.Process(Udp(Phone, 5000, Gateway, 53), PacketSource.Local, outbound: false));
        Assert.Equal(GateVerdict.Dns, Forward(Udp(Phone, 5000, "8.8.8.8", 53)));   // hard-coded DNS server
        Assert.Equal(GateVerdict.Dns, Forward(Udp(Laptop, 5000, "1.1.1.1", 53)));
        Assert.Equal(3, _gate.Counters.DnsQueries);
    }

    [Fact]
    public void Dhcp_broadcast_multicast_and_the_gateway_pass()
    {
        Assert.Equal(GateVerdict.Pass, Forward(Udp(Phone, 68, "255.255.255.255", 67)));
        Assert.Equal(GateVerdict.Pass, Forward(Udp(Phone, 5353, "224.0.0.251", 5353)));
        Assert.Equal(GateVerdict.Pass, Forward(Udp(Phone, 137, "192.168.137.255", 137)));
        Assert.Equal(GateVerdict.Pass, _gate.Process(Tcp(Phone, 50000, Gateway, 8080, TcpFlags.Syn), PacketSource.Local, outbound: false));
    }

    [Fact]
    public void Blocked_ips_are_refused_even_for_authorised_devices()
    {
        _gate.Update(Snapshot(FilterRules.Empty with { BlockedIps = new IpRangeSet([Ipv4Subnet.Parse("31.13.0.0/16")]) }));

        Assert.Equal(GateVerdict.Reset, Forward(Tcp(Laptop, 50000, "31.13.64.35", 443, TcpFlags.Syn)));
        Assert.Equal(GateVerdict.Pass, Forward(Tcp(Laptop, 50000, Web, 443, TcpFlags.Syn)));
    }

    [Fact]
    public void Free_access_ips_and_learnt_addresses_pass_before_login_until_they_expire()
    {
        _gate.Update(Snapshot(FilterRules.Empty with { FreeIps = new IpRangeSet([Ipv4Subnet.Parse("41.202.0.0/16")]) }));
        Assert.Equal(GateVerdict.Pass, Forward(Tcp(Phone, 50000, "41.202.10.10", 443, TcpFlags.Syn)));
        Assert.Equal(GateVerdict.Pass, Forward(Tcp("41.202.10.10", 443, Phone, 50000, TcpFlags.Syn | TcpFlags.Ack)));

        _gate.LearnFreeAddress(Ip("102.1.2.3"), TimeSpan.FromSeconds(30)); // kept at least 5 minutes
        Assert.Equal(GateVerdict.Pass, Forward(Tcp(Phone, 50001, "102.1.2.3", 443, TcpFlags.Syn)));

        _clock.Advance(TimeSpan.FromMinutes(5));
        _gate.Maintain();
        Assert.Equal(GateVerdict.Reset, Forward(Tcp(Phone, 50001, "102.1.2.3", 443, TcpFlags.Syn)));
        Assert.Equal(0, _gate.LearnedFreeCount);
    }

    [Fact]
    public void Idle_redirect_flows_are_forgotten()
    {
        Forward(Tcp(Phone, 50123, Web, 80, TcpFlags.Syn));
        _clock.Advance(PacketProcessor.RedirectIdleTimeout + TimeSpan.FromSeconds(1));
        _gate.Maintain();
        Assert.Equal(0, _gate.RedirectFlowCount);
    }

    [Fact]
    public void Without_a_snapshot_everything_is_dropped()
    {
        var closed = new PacketProcessor(_clock);
        Assert.Equal(GateVerdict.Drop, closed.Process(Tcp(Laptop, 1, Web, 80, TcpFlags.Syn), PacketSource.Forward, false));
    }

    [Fact]
    public void Filters_capture_only_the_customer_subnet()
    {
        var (forward, local) = GateFilters.Build(new GateConfig(7, Ipv4Subnet.Parse("192.168.137.1/24"), 80));

        Assert.Contains("ip.SrcAddr >= 192.168.137.1 and ip.SrcAddr <= 192.168.137.254", forward);
        Assert.Contains("ifIdx == 7", local);
        Assert.Contains("udp.DstPort == 53 and ip.DstAddr == 192.168.137.1", local);
        Assert.Contains("tcp.SrcPort == 80 and ip.SrcAddr == 192.168.137.1", local);
    }
}
