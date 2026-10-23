using System.Buffers.Binary;
using System.Net;
using StonesHotspot.Network.Gate.Packets;

namespace StonesHotspot.Tests.Infrastructure;

/// <summary>
/// Builds IPv4 test packets with its own straightforward checksum code, so the production checksum code is checked
/// against an independent implementation.
/// </summary>
public static class TestPackets
{
    public static uint Ip(string address) => BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(address).GetAddressBytes());

    public static byte[] Tcp(string src, ushort srcPort, string dst, ushort dstPort, TcpFlags flags,
        uint seq = 1000, uint ack = 0, byte[]? payload = null, int ipOptionsBytes = 0)
    {
        payload ??= [];
        var ihl = 20 + ipOptionsBytes;
        var packet = new byte[ihl + 20 + payload.Length];
        Header(packet, ihl, IpProtocol.Tcp, src, dst);
        var tcp = packet.AsSpan(ihl);
        BinaryPrimitives.WriteUInt16BigEndian(tcp, srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..], dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[4..], seq);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[8..], ack);
        tcp[12] = 0x50;
        tcp[13] = (byte)flags;
        BinaryPrimitives.WriteUInt16BigEndian(tcp[14..], 64240);
        payload.CopyTo(tcp[20..]);
        FillChecksums(packet);
        return packet;
    }

    public static byte[] Udp(string src, ushort srcPort, string dst, ushort dstPort, byte[]? payload = null)
    {
        payload ??= [];
        var packet = new byte[28 + payload.Length];
        Header(packet, 20, IpProtocol.Udp, src, dst);
        var udp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(udp, srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[2..], dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], (ushort)(8 + payload.Length));
        payload.CopyTo(udp[8..]);
        FillChecksums(packet);
        return packet;
    }

    public static byte[] Icmp(string src, string dst)
    {
        var packet = new byte[28];
        Header(packet, 20, IpProtocol.Icmp, src, dst);
        packet[20] = 8; // echo request
        FillChecksums(packet);
        return packet;
    }

    private static void Header(byte[] packet, int ihl, byte protocol, string src, string dst)
    {
        packet[0] = (byte)(0x40 | ihl / 4);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 0x1234);
        packet[8] = 128;
        packet[9] = protocol;
        IPAddress.Parse(src).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(dst).GetAddressBytes().CopyTo(packet, 16);
        for (var i = 20; i < ihl; i++)
            packet[i] = 1; // NOP options
    }

    private static void FillChecksums(byte[] packet)
    {
        var ihl = (packet[0] & 0x0F) * 4;
        WriteChecksum(packet, 10, OnesComplement(packet.AsSpan(0, ihl)));
        var segment = packet.AsSpan(ihl);
        switch (packet[9])
        {
            case IpProtocol.Tcp:
                WriteChecksum(segment, 16, OnesComplement([.. Pseudo(packet, segment.Length), .. segment.ToArray()]));
                break;
            case IpProtocol.Udp:
                WriteChecksum(segment, 6, OnesComplement([.. Pseudo(packet, segment.Length), .. segment.ToArray()]));
                break;
            default: // ICMP: no pseudo-header
                WriteChecksum(segment, 2, OnesComplement(segment));
                break;
        }
    }

    private static ushort OnesComplement(ReadOnlySpan<byte> bytes) => (ushort)~Sum(bytes);

    private static byte[] Pseudo(byte[] packet, int segmentLength) =>
        [.. packet.AsSpan(12, 8).ToArray(), 0, packet[9], (byte)(segmentLength >> 8), (byte)segmentLength];

    private static void WriteChecksum(Span<byte> data, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data[offset..], value);

    /// <summary>Independent check: summing a correct header/segment including its checksum gives 0xFFFF.</summary>
    public static bool ChecksumsValid(byte[] packet)
    {
        var ihl = (packet[0] & 0x0F) * 4;
        var total = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2));
        if (Sum(packet.AsSpan(0, ihl)) != 0xFFFF)
            return false;
        if (packet[9] is not (IpProtocol.Tcp or IpProtocol.Udp))
            return true;
        var segment = packet.AsSpan(ihl, total - ihl).ToArray();
        return Sum([.. Pseudo(packet, segment.Length), .. segment]) == 0xFFFF;
    }

    private static long Sum(ReadOnlySpan<byte> bytes)
    {
        long sum = 0;
        for (var i = 0; i < bytes.Length; i += 2)
            sum += (bytes[i] << 8) + (i + 1 < bytes.Length ? bytes[i + 1] : 0);
        while (sum > 0xFFFF)
            sum = (sum & 0xFFFF) + (sum >> 16);
        return sum;
    }

    // ---- DNS ----

    /// <summary>A standard recursive query for one name, with an EDNS OPT record like real phones send.</summary>
    public static byte[] DnsQuery(string name, ushort type = 1, ushort id = 0xBEEF)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 1 };
        bytes.AddRange(EncodeName(name));
        bytes.AddRange([(byte)(type >> 8), (byte)type, 0, 1]);
        bytes.AddRange([0, 0, 41, 0x10, 0x00, 0, 0, 0, 0, 0, 0]); // OPT, 4096-byte UDP payload
        return [.. bytes];
    }

    /// <summary>A response with a CNAME (name compression) followed by A records.</summary>
    public static byte[] DnsResponse(byte[] query, string cname, params (string Ip, uint Ttl)[] addresses)
    {
        var questionEnd = 12 + EncodeName(QueryName(query)).Length + 4;
        var bytes = new List<byte>(query[..questionEnd]);
        bytes[2] = 0x81;
        bytes[3] = 0x80;
        var answers = 1 + addresses.Length;
        bytes[6] = (byte)(answers >> 8);
        bytes[7] = (byte)answers;
        bytes[10] = bytes[11] = 0;

        var cnameData = EncodeName(cname);
        bytes.AddRange([0xC0, 0x0C, 0, 5, 0, 1, 0, 0, 0, 60, (byte)(cnameData.Length >> 8), (byte)cnameData.Length]);
        var cnameOffset = bytes.Count;
        bytes.AddRange(cnameData);
        foreach (var (ip, ttl) in addresses)
        {
            bytes.AddRange([(byte)(0xC0 | cnameOffset >> 8), (byte)cnameOffset, 0, 1, 0, 1,
                (byte)(ttl >> 24), (byte)(ttl >> 16), (byte)(ttl >> 8), (byte)ttl, 0, 4]);
            bytes.AddRange(IPAddress.Parse(ip).GetAddressBytes());
        }
        return [.. bytes];
    }

    public static byte[] EncodeName(string name)
    {
        var bytes = new List<byte>();
        foreach (var label in name.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(label.Select(c => (byte)c));
        }
        bytes.Add(0);
        return [.. bytes];
    }

    private static string QueryName(byte[] query)
    {
        var labels = new List<string>();
        var i = 12;
        while (query[i] != 0)
        {
            labels.Add(System.Text.Encoding.ASCII.GetString(query, i + 1, query[i]));
            i += query[i] + 1;
        }
        return string.Join('.', labels);
    }
}
