using System.Buffers.Binary;
using StonesHotspot.Network.Gate.Packets;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public class PacketTests
{
    [Fact]
    public void Parses_tcp_with_ip_options()
    {
        var packet = Tcp("192.168.137.23", 50123, "93.184.216.34", 80, TcpFlags.Syn, seq: 42, payload: [1, 2, 3], ipOptionsBytes: 8);

        Assert.True(Ipv4Packet.TryParse(packet, out var p));
        Assert.Equal((28, IpProtocol.Tcp), (p.HeaderLength, p.Protocol));
        Assert.Equal(Ip("192.168.137.23"), p.Source);
        Assert.Equal(Ip("93.184.216.34"), p.Destination);
        Assert.Equal((50123, 80), (p.SourcePort, p.DestinationPort));
        Assert.True(p.HasFlag(TcpFlags.Syn));
        Assert.Equal((42u, 3), (p.Sequence, p.PayloadLength));
    }

    [Fact]
    public void Rejects_truncated_and_non_ipv4_packets()
    {
        var packet = Udp("192.168.137.23", 5000, "8.8.8.8", 53, [1, 2, 3, 4]);
        Assert.False(Ipv4Packet.TryParse(packet.AsSpan(0, 20), out _));   // total length says more
        packet[0] = 0x60;
        Assert.False(Ipv4Packet.TryParse(packet, out _));                 // IPv6
    }

    [Fact]
    public void Our_test_packets_have_valid_checksums_and_production_code_agrees()
    {
        var packets = new[]
        {
            Tcp("192.168.137.23", 50123, "93.184.216.34", 80, TcpFlags.Ack | TcpFlags.Psh, payload: "GET / HTTP/1.1\r\n"u8.ToArray()),
            Udp("192.168.137.23", 5353, "8.8.8.8", 53, [1, 2, 3]),  // odd length
        };
        foreach (var packet in packets)
        {
            Assert.True(ChecksumsValid(packet));
            var copy = (byte[])packet.Clone();
            Ipv4Packet.UpdateChecksums(copy);
            Assert.Equal(packet, copy);
        }
    }

    [Fact]
    public void Rewriting_destination_and_source_keeps_checksums_valid()
    {
        var packet = Tcp("192.168.137.23", 50123, "93.184.216.34", 80, TcpFlags.Syn);

        Ipv4Packet.SetDestination(packet, Ip("192.168.137.1"), 8081);
        Assert.True(Ipv4Packet.TryParse(packet, out var p));
        Assert.Equal((Ip("192.168.137.1"), (ushort)8081), (p.Destination, p.DestinationPort));
        Assert.True(ChecksumsValid(packet));

        Ipv4Packet.SetSource(packet, Ip("10.0.0.1"), 1234);
        Assert.True(Ipv4Packet.TryParse(packet, out p));
        Assert.Equal((Ip("10.0.0.1"), (ushort)1234), (p.Source, p.SourcePort));
        Assert.True(ChecksumsValid(packet));
    }

    [Fact]
    public void Reset_for_a_syn_acknowledges_it()
    {
        var syn = Tcp("192.168.137.23", 50123, "142.250.1.1", 443, TcpFlags.Syn, seq: 1000);

        var rst = Ipv4Packet.BuildTcpReset(syn);

        Assert.True(Ipv4Packet.TryParse(rst, out var p));
        Assert.Equal((Ip("142.250.1.1"), (ushort)443), (p.Source, p.SourcePort));
        Assert.Equal((Ip("192.168.137.23"), (ushort)50123), (p.Destination, p.DestinationPort));
        Assert.Equal(TcpFlags.Rst | TcpFlags.Ack, p.Flags);
        Assert.Equal((0u, 1001u), (p.Sequence, p.Acknowledgement));
        Assert.True(ChecksumsValid(rst));
    }

    [Fact]
    public void Reset_for_a_segment_with_ack_uses_its_ack_number()
    {
        var data = Tcp("192.168.137.23", 50123, "142.250.1.1", 443, TcpFlags.Ack | TcpFlags.Psh, seq: 5000, ack: 777, payload: [1, 2]);

        Assert.True(Ipv4Packet.TryParse(Ipv4Packet.BuildTcpReset(data), out var p));
        Assert.Equal(TcpFlags.Rst, p.Flags);
        Assert.Equal(777u, p.Sequence);
    }

    [Fact]
    public void Udp_reply_swaps_endpoints_and_carries_the_payload()
    {
        var query = Udp("192.168.137.23", 40000, "8.8.8.8", 53, [9, 9]);

        var reply = Ipv4Packet.BuildUdpReply(query, [1, 2, 3, 4, 5]);

        Assert.True(Ipv4Packet.TryParse(reply, out var p));
        Assert.Equal((Ip("8.8.8.8"), (ushort)53, Ip("192.168.137.23"), (ushort)40000), (p.Source, p.SourcePort, p.Destination, p.DestinationPort));
        Assert.Equal([1, 2, 3, 4, 5], reply.AsSpan(p.PayloadOffset, p.PayloadLength).ToArray());
        Assert.Equal(13, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(24)));
        Assert.True(ChecksumsValid(reply));
    }
}
