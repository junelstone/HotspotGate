using System.Buffers.Binary;
using System.Net;

namespace StonesHotspot.Network.Neighbors;

/// <summary>
/// Decodes the MIB_IPNET_TABLE2 buffer returned by GetIpNetTable2 (x64 and x86 layouts are identical):
/// <code>
/// MIB_IPNET_TABLE2: ULONG NumEntries; [4 bytes padding]; MIB_IPNET_ROW2 Table[];   rows start at offset 8
/// MIB_IPNET_ROW2 (88 bytes):
///   0  SOCKADDR_INET Address (28)   → family at 0 (AF_INET = 2), IPv4 address at 4
///   28 ULONG InterfaceIndex
///   32 NET_LUID InterfaceLuid (8)
///   40 UCHAR PhysicalAddress[32]
///   72 ULONG PhysicalAddressLength
///   76 NL_NEIGHBOR_STATE State
///   80 UCHAR Flags; [3 padding]; 84 ULONG ReachabilityTime
/// </code>
/// </summary>
public static class NeighborTableParser
{
    public const int HeaderSize = 8;
    public const int RowSize = 88;
    private const ushort AfInet = 2;

    public static IReadOnlyList<NeighborEntry> Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
            return [];

        var count = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var entries = new List<NeighborEntry>();
        for (var i = 0; i < count; i++)
        {
            var offset = HeaderSize + i * RowSize;
            if (offset + RowSize > buffer.Length)
                break;

            var row = buffer.Slice(offset, RowSize);
            if (BinaryPrimitives.ReadUInt16LittleEndian(row) != AfInet)
                continue;

            var macLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(row[72..]);
            if (macLength != 6)
                continue;

            entries.Add(new NeighborEntry(
                (int)BinaryPrimitives.ReadUInt32LittleEndian(row[28..]),
                new IPAddress(row.Slice(4, 4)),
                string.Join(':', row.Slice(40, 6).ToArray().Select(b => b.ToString("X2"))),
                (NeighborState)BinaryPrimitives.ReadInt32LittleEndian(row[76..])));
        }
        return entries;
    }
}
