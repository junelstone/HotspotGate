using System.Buffers.Binary;

namespace StonesHotspot.Network.Gate.Packets;

public static class IpProtocol
{
    public const byte Icmp = 1;
    public const byte Tcp = 6;
    public const byte Udp = 17;
}

[Flags]
public enum TcpFlags : byte
{
    None = 0,
    Fin = 0x01,
    Syn = 0x02,
    Rst = 0x04,
    Psh = 0x08,
    Ack = 0x10,
}

/// <summary>
/// The parts of an IPv4 packet the gate looks at. Addresses are host-order <see cref="uint"/>s (192.168.137.1 =
/// 0xC0A88901). Ports and flags are zero for protocols other than TCP/UDP.
/// </summary>
public readonly record struct PacketInfo(
    int HeaderLength,
    int TotalLength,
    byte Protocol,
    uint Source,
    uint Destination,
    ushort SourcePort,
    ushort DestinationPort,
    TcpFlags Flags,
    uint Sequence,
    uint Acknowledgement,
    int PayloadOffset,
    int PayloadLength)
{
    public bool IsTcp => Protocol == IpProtocol.Tcp;
    public bool IsUdp => Protocol == IpProtocol.Udp;
    public bool HasFlag(TcpFlags flag) => (Flags & flag) == flag;
}

/// <summary>Reads, rewrites and builds IPv4 TCP/UDP packets in place, with correct checksums.</summary>
public static class Ipv4Packet
{
    public const int MinHeaderLength = 20;
    private const int TcpMinHeaderLength = 20;
    private const int UdpHeaderLength = 8;

    public static bool TryParse(ReadOnlySpan<byte> packet, out PacketInfo info)
    {
        info = default;
        if (packet.Length < MinHeaderLength || packet[0] >> 4 != 4)
            return false;

        var headerLength = (packet[0] & 0x0F) * 4;
        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if (headerLength < MinHeaderLength || totalLength < headerLength || totalLength > packet.Length)
            return false;

        var protocol = packet[9];
        var source = BinaryPrimitives.ReadUInt32BigEndian(packet[12..]);
        var destination = BinaryPrimitives.ReadUInt32BigEndian(packet[16..]);
        var fragmentOffset = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) & 0x1FFF;
        var transport = packet[headerLength..totalLength];

        // Later fragments carry no transport header: treat them as port-less.
        if (fragmentOffset == 0 && protocol == IpProtocol.Tcp && transport.Length >= TcpMinHeaderLength)
        {
            var tcpHeaderLength = (transport[12] >> 4) * 4;
            if (tcpHeaderLength < TcpMinHeaderLength || tcpHeaderLength > transport.Length)
                return false;
            info = new PacketInfo(headerLength, totalLength, protocol, source, destination,
                BinaryPrimitives.ReadUInt16BigEndian(transport),
                BinaryPrimitives.ReadUInt16BigEndian(transport[2..]),
                (TcpFlags)(transport[13] & 0x3F),
                BinaryPrimitives.ReadUInt32BigEndian(transport[4..]),
                BinaryPrimitives.ReadUInt32BigEndian(transport[8..]),
                headerLength + tcpHeaderLength,
                transport.Length - tcpHeaderLength);
            return true;
        }

        if (fragmentOffset == 0 && protocol == IpProtocol.Udp && transport.Length >= UdpHeaderLength)
        {
            info = new PacketInfo(headerLength, totalLength, protocol, source, destination,
                BinaryPrimitives.ReadUInt16BigEndian(transport),
                BinaryPrimitives.ReadUInt16BigEndian(transport[2..]),
                TcpFlags.None, 0, 0,
                headerLength + UdpHeaderLength,
                transport.Length - UdpHeaderLength);
            return true;
        }

        info = new PacketInfo(headerLength, totalLength, protocol, source, destination, 0, 0, TcpFlags.None, 0, 0,
            headerLength, transport.Length);
        return true;
    }

    /// <summary>Changes the source address and port (TCP/UDP), then fixes the checksums.</summary>
    public static void SetSource(Span<byte> packet, uint address, ushort port)
    {
        BinaryPrimitives.WriteUInt32BigEndian(packet[12..], address);
        WritePort(packet, 0, port);
        UpdateChecksums(packet);
    }

    /// <summary>Changes the destination address and port (TCP/UDP), then fixes the checksums.</summary>
    public static void SetDestination(Span<byte> packet, uint address, ushort port)
    {
        BinaryPrimitives.WriteUInt32BigEndian(packet[16..], address);
        WritePort(packet, 2, port);
        UpdateChecksums(packet);
    }

    private static void WritePort(Span<byte> packet, int offset, ushort port)
    {
        var protocol = packet[9];
        if (protocol is IpProtocol.Tcp or IpProtocol.Udp)
            BinaryPrimitives.WriteUInt16BigEndian(packet[((packet[0] & 0x0F) * 4 + offset)..], port);
    }

    /// <summary>Recomputes the IPv4 header checksum and the full TCP/UDP checksum.</summary>
    public static void UpdateChecksums(Span<byte> packet)
    {
        var headerLength = (packet[0] & 0x0F) * 4;
        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);

        packet[10] = packet[11] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet[10..], Fold(Sum(packet[..headerLength], 0)));

        var fragmentOffset = BinaryPrimitives.ReadUInt16BigEndian(packet[6..]) & 0x1FFF;
        if (fragmentOffset != 0)
            return;

        var protocol = packet[9];
        var segment = packet[headerLength..totalLength];
        int checksumOffset;
        if (protocol == IpProtocol.Tcp && segment.Length >= TcpMinHeaderLength)
            checksumOffset = 16;
        else if (protocol == IpProtocol.Udp && segment.Length >= UdpHeaderLength)
            checksumOffset = 6;
        else
            return;

        segment[checksumOffset] = segment[checksumOffset + 1] = 0;
        var pseudo = Sum(packet.Slice(12, 8), 0) + protocol + (uint)segment.Length;
        var checksum = Fold(Sum(segment, pseudo));
        if (protocol == IpProtocol.Udp && checksum == 0)
            checksum = 0xFFFF; // 0 means "no checksum" for UDP
        BinaryPrimitives.WriteUInt16BigEndian(segment[checksumOffset..], checksum);
    }

    /// <summary>True when the IPv4 header and TCP/UDP checksums are correct (used by tests and diagnostics).</summary>
    public static bool ChecksumsAreValid(ReadOnlySpan<byte> packet)
    {
        var copy = packet.ToArray();
        UpdateChecksums(copy);
        return copy.AsSpan().SequenceEqual(packet);
    }

    /// <summary>
    /// Builds the TCP reset the server side would send in answer to <paramref name="segment"/> (RFC 9293 §3.10.7.1):
    /// the device's connection attempt fails at once instead of waiting for a timeout.
    /// </summary>
    public static byte[] BuildTcpReset(ReadOnlySpan<byte> segment)
    {
        if (!TryParse(segment, out var p) || !p.IsTcp)
            throw new ArgumentException("Not a TCP packet.", nameof(segment));

        var packet = new byte[MinHeaderLength + TcpMinHeaderLength];
        WriteIpHeader(packet, IpProtocol.Tcp, p.Destination, p.Source);
        var tcp = packet.AsSpan(MinHeaderLength);
        BinaryPrimitives.WriteUInt16BigEndian(tcp, p.DestinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..], p.SourcePort);

        if (p.HasFlag(TcpFlags.Ack))
        {
            BinaryPrimitives.WriteUInt32BigEndian(tcp[4..], p.Acknowledgement);
            tcp[13] = (byte)TcpFlags.Rst;
        }
        else
        {
            var length = (uint)p.PayloadLength + (p.HasFlag(TcpFlags.Syn) ? 1u : 0) + (p.HasFlag(TcpFlags.Fin) ? 1u : 0);
            BinaryPrimitives.WriteUInt32BigEndian(tcp[8..], p.Sequence + length);
            tcp[13] = (byte)(TcpFlags.Rst | TcpFlags.Ack);
        }

        tcp[12] = TcpMinHeaderLength / 4 << 4;
        UpdateChecksums(packet);
        return packet;
    }

    /// <summary>Builds the UDP answer to <paramref name="request"/>: addresses and ports swapped, new payload.</summary>
    public static byte[] BuildUdpReply(ReadOnlySpan<byte> request, ReadOnlySpan<byte> payload)
    {
        if (!TryParse(request, out var p) || !p.IsUdp)
            throw new ArgumentException("Not a UDP packet.", nameof(request));
        if (payload.Length > ushort.MaxValue - MinHeaderLength - UdpHeaderLength)
            throw new ArgumentException("Payload too large.", nameof(payload));

        var packet = new byte[MinHeaderLength + UdpHeaderLength + payload.Length];
        WriteIpHeader(packet, IpProtocol.Udp, p.Destination, p.Source);
        var udp = packet.AsSpan(MinHeaderLength);
        BinaryPrimitives.WriteUInt16BigEndian(udp, p.DestinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[2..], p.SourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)(UdpHeaderLength + payload.Length));
        payload.CopyTo(udp[UdpHeaderLength..]);
        UpdateChecksums(packet);
        return packet;
    }

    private static void WriteIpHeader(Span<byte> packet, byte protocol, uint source, uint destination)
    {
        packet[0] = 0x45;                                              // IPv4, 20-byte header
        BinaryPrimitives.WriteUInt16BigEndian(packet[2..], (ushort)packet.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet[6..], 0x4000);   // don't fragment
        packet[8] = 64;                                                // TTL
        packet[9] = protocol;
        BinaryPrimitives.WriteUInt32BigEndian(packet[12..], source);
        BinaryPrimitives.WriteUInt32BigEndian(packet[16..], destination);
    }

    private static uint Sum(ReadOnlySpan<byte> data, uint sum)
    {
        var i = 0;
        for (; i + 1 < data.Length; i += 2)
            sum += BinaryPrimitives.ReadUInt16BigEndian(data[i..]);
        if (i < data.Length)
            sum += (uint)data[i] << 8;
        return sum;
    }

    private static ushort Fold(uint sum)
    {
        while (sum >> 16 != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return (ushort)~sum;
    }
}
