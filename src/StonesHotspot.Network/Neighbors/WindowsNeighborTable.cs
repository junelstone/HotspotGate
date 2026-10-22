using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StonesHotspot.Network.Neighbors;

/// <summary>Reads the IPv4 neighbour (ARP) table with GetIpNetTable2 from iphlpapi.dll.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsNeighborTable : INeighborTable
{
    private const ushort AfInet = 2;

    public IReadOnlyList<NeighborEntry> GetIpv4Entries()
    {
        var error = GetIpNetTable2(AfInet, out var table);
        if (error != 0)
            throw new Win32Exception((int)error, "GetIpNetTable2 failed.");

        try
        {
            var count = Marshal.ReadInt32(table);
            var size = NeighborTableParser.HeaderSize + count * NeighborTableParser.RowSize;
            unsafe
            {
                return NeighborTableParser.Parse(new ReadOnlySpan<byte>((void*)table, size));
            }
        }
        finally
        {
            FreeMibTable(table);
        }
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetIpNetTable2(ushort family, out IntPtr table);

    [LibraryImport("iphlpapi.dll")]
    private static partial void FreeMibTable(IntPtr memory);
}
